using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public interface IStructureChangeListener
	{
		void OnVesselGoOnRails();
		void OnVesselGoOffRails();
		void RightBeforeStructureChange();
		void RightAfterStructureChange();
	}

	public static class StructureChangeMapper
	{
		public static void map(this List<IStructureChangeListener> l, Action<IStructureChangeListener> a)
		{
			int c = l.Count;
			for (int i = 0; i < c; i++) {
				if (l[i] == null)
					continue;
				try {
					a(l[i]);
				} catch (Exception e) {
					ModuleBaseRotate.lprint(e.StackTrace);
				}
			}
		}
	}

	public class VesselMotionManager: MonoBehaviour
	{
		public static bool trace = true;

		private Vessel vessel = null;
		private int rotCount = 0;
		private bool verboseEvents = true;
		public bool onRails = false;

		public static VesselMotionManager get(Vessel v, bool create = true)
		{
			if (!v) {
				lprint("*** WARNING *** " + nameof(VesselMotionManager) + ".get() with null vessel");
				return null;
			}

			VesselMotionManager info = v.gameObject.GetComponent<VesselMotionManager>();
			if (info) {
				if (info.vessel != v)
					lprint(nameof(VesselMotionManager) + ".vessel: " + info.desc() + " -> " + desc(v));
				info.vessel = v;
			}

			if (!info && create) {
				info = v.gameObject.AddComponent<VesselMotionManager>();
				info.vessel = v;
				lprint(nameof(VesselMotionManager) + ".get(" + desc(v) + ") created " + info.desc());
			}
			return info;
		}

		public static void resetRotCount(Vessel v)
		{
			VesselMotionManager vmm = get(v, false);
			if (!vmm)
				return;
			int c = vmm.rotCount;
			if (trace && c != 0)
				ModuleBaseRotate.lprint("resetInfo(" + vmm.desc() + "): " + c + " -> RESET");
			vmm.rotCount = 0;
		}

		public int changeCount(int delta)
		{
			int ret = rotCount + delta;
			if (ret < 0)
				ret = 0;
			if (trace && delta != 0)
				ModuleBaseRotate.lprint("changeCount(" + GetInstanceID() + ", " + delta + "): "
					+ rotCount + " -> " + ret);
			return rotCount = ret;
		}

		/******** Events ********/

		bool eventState = false;

		private void setEvents(bool cmd)
		{
			if (cmd == eventState) {
				if (verboseEvents)
					lprint(nameof(VesselMotionManager) + ".setEvents(" + cmd + ") repeated on " + desc());
				return;
			}

			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".setEvents(" + cmd + ") on " + desc());

			if (cmd) {

				GameEvents.onVesselCreate.Add(OnVesselCreate);

				GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
				GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);

				GameEvents.OnCameraChange.Add(OnCameraChange);

				// GameEvents.onActiveJointNeedUpdate.Add(RightBeforeStructureChangeJointUpdate);

				// GameEvents.onPartCouple.Add(RightBeforeStructureChangeAction);
				GameEvents.onPartCoupleComplete.Add(RightAfterStructureChangeAction);
				GameEvents.onPartDeCouple.Add(RightBeforeStructureChangePart);
				GameEvents.onPartDeCoupleComplete.Add(RightAfterStructureChangePart);

				// GameEvents.onVesselDocking.Add(RightBeforeStructureChangeIds);
				GameEvents.onDockingComplete.Add(RightAfterStructureChangeAction);
				GameEvents.onPartUndock.Add(RightBeforeStructureChangePart);
				GameEvents.onPartUndockComplete.Add(RightAfterStructureChangePart);

				GameEvents.onSameVesselDock.Add(RightAfterSameVesselDock);
				GameEvents.onSameVesselUndock.Add(RightAfterSameVesselUndock);

			} else {

				GameEvents.onVesselCreate.Remove(OnVesselCreate);

				GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
				GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);

				GameEvents.OnCameraChange.Remove(OnCameraChange);

				// GameEvents.onActiveJointNeedUpdate.Remove(RightBeforeStructureChangeJointUpdate);

				// GameEvents.onPartCouple.Remove(RightBeforeStructureChangeAction);
				GameEvents.onPartCoupleComplete.Remove(RightAfterStructureChangeAction);
				GameEvents.onPartDeCouple.Remove(RightBeforeStructureChangePart);
				GameEvents.onPartDeCoupleComplete.Remove(RightAfterStructureChangePart);

				// GameEvents.onVesselDocking.Remove(RightBeforeStructureChangeIds);
				GameEvents.onDockingComplete.Remove(RightAfterStructureChangeAction);
				GameEvents.onPartUndock.Remove(RightBeforeStructureChangePart);
				GameEvents.onPartUndockComplete.Remove(RightAfterStructureChangePart);

				GameEvents.onSameVesselDock.Remove(RightAfterSameVesselDock);
				GameEvents.onSameVesselUndock.Remove(RightAfterSameVesselUndock);

			}

			eventState = cmd;
		}

		struct StructureChangeInfo {
			public Part part;

			public void reset()
			{
				this = new StructureChangeInfo();
			}
		}

		StructureChangeInfo structureChangeInfo;

		private bool care(Vessel v, bool useStructureChangeInfo)
		{
			bool ret = v && v == vessel;
			lprint(nameof(VesselMotionManager) + ".care("
				+ desc(v)
				+ ") on "
				+ desc()
				+ " = " + ret);
			return ret;
		}

		private bool care(Part p, bool useStructureChangeInfo)
		{
			if (useStructureChangeInfo && p && p == structureChangeInfo.part) {
				lprint(nameof(VesselMotionManager) + ".care(" + p.desc() + ") = " + true);
				return true;
			}
			return p && care(p.vessel, useStructureChangeInfo);
		}

		private bool care(GameEvents.FromToAction<Part, Part> action, bool useStructureChangeInfo)
		{
			return care(action.from, useStructureChangeInfo) || care(action.to, useStructureChangeInfo);
		}

		private bool care(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action, bool useStructureChangeInfo)
		{
			return care(action.from.part, useStructureChangeInfo) || care(action.to.part, useStructureChangeInfo);
		}

		private List<IStructureChangeListener> listeners()
		{
			List<IStructureChangeListener> ret = vessel.FindPartModulesImplementing<IStructureChangeListener>();
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".listeners() on " + desc() + " finds " + ret.Count);
			return ret;
		}

		private List<IStructureChangeListener> listeners(Part p)
		{
			List<IStructureChangeListener> ret = p.FindModulesImplementing<IStructureChangeListener>();
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".listeners(" + p.desc() + ") on " + desc() + " finds " + ret.Count);
			return ret;
		}

		public void OnVesselCreate(Vessel v)
		{
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".OnVesselCreate(" + desc(v) + ") on " + desc());
			get(v);
		}

		public void OnVesselGoOnRails(Vessel v)
		{
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".OnVesselGoOnRails(" + desc(v) + ") on " + desc());
			if (!care(v, false))
				return;
			resetRotCount(vessel);
			structureChangeInfo.reset();
			onRails = true;
			listeners().map(l => l.OnVesselGoOnRails());
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".OnVesselGoOffRails(" + desc(v) + ") on " + desc());
			VesselMotionManager.get(v);
			if (!care(v, false))
				return;
			resetRotCount(vessel);
			structureChangeInfo.reset();
			onRails = false;
			listeners().map(l => l.OnVesselGoOffRails());
		}

		public void RightBeforeStructureChangePart(Part p)
		{
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".RightBeforeStructureChangePart("
					+ desc(p.vessel) + ") on " + desc());
			structureChangeInfo.reset();
			if (!care(p, false))
				return;
			structureChangeInfo.part = p;
		}

		public void RightAfterStructureChangeAction(GameEvents.FromToAction<Part, Part> action)
		{
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".RightAfterStructureChangeAction("
					+ desc(action.from.vessel) + ", " + desc(action.to.vessel)
					+ ") on " + desc());
			if (!care(action, true))
				return;
			listeners().map(l => l.RightAfterStructureChange());
		}

		public void RightAfterStructureChangePart(Part p)
		{
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".RightAfterStructureChangePart("
					+ desc(p.vessel) + ") on " + desc());
			if (!care(p, true))
				return;
			listeners().map(l => l.RightAfterStructureChange());
		}

		public void RightAfterSameVesselDock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".RightAfterSameVesselDock("
					+ desc(action.from.vessel) + ", " + desc(action.to.vessel)
					+ ") on " + desc());
			if (!care(action, false))
				return;
			listeners(action.from.part).map(l => l.RightAfterStructureChange());
			listeners(action.to.part).map(l => l.RightAfterStructureChange());
		}

		public void RightAfterSameVesselUndock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".RightAfterSameVesselUndock("
					+ desc(action.from.vessel) + ", " + desc(action.to.vessel)
					+ ") on " + desc());
			if (!care(action, false))
				return;
			listeners(action.from.part).map(l => l.RightAfterStructureChange());
			listeners(action.to.part).map(l => l.RightAfterStructureChange());
		}

		public void OnCameraChange(CameraManager.CameraMode mode)
		{
			Camera camera = CameraManager.GetCurrentCamera();
			if (verboseEvents && camera) {
				lprint(nameof(VesselMotionManager) + ".OnCameraChange(" + mode + ") on " + desc());
				Camera[] cameras = Camera.allCameras;
				for (int i = 0; i < cameras.Length; i++)
					lprint("camera[" + i + "] = " + cameras[i].desc());
			}
		}

		public void Awake()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionManager) + ".Awake() on " + desc());
			setEvents(true);
		}

		public void Start()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionManager) + ".Start() on " + desc());
		}

		public void OnDestroy()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionManager) + ".OnDestroy() on " + desc());
			setEvents(false);
		}

		private static string desc(Vessel v)
		{
			return v ? v.name + "[" + v.rootPart.flightID + "]": "null";
		}

		private string desc()
		{
			return GetInstanceID() + "-" + desc(vessel);
		}

		private static void lprint(string msg)
		{
			ModuleBaseRotate.lprint(msg);
		}
	}
}

