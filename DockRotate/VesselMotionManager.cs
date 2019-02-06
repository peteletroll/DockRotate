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
		public static void map(this List<IStructureChangeListener> ls, Action<IStructureChangeListener> a)
		{
			int c = ls.Count;
			int i = 0;
			while (i < c) {
				try {
					while (i < c) {
						IStructureChangeListener l = ls[i++];
						if (l == null)
							continue;
						a(l);
					}
				} catch (Exception e) {
					ModuleBaseRotate.log(e.StackTrace);
				}
			}
		}
	}

	public class VesselMotionManager: MonoBehaviour
	{
		private Vessel vessel = null;
		private Part vesselRoot = null;
		private int rotCount = 0;
		public bool onRails = false;

		private bool verboseEvents = true;
		private bool verboseCare = false;

		public static VesselMotionManager get(Part p)
		{
			return get(p.vessel);
		}

		public static VesselMotionManager get(Vessel v)
		{
			if (!v) {
				log("*** WARNING *** " + typeof(VesselMotionManager) + ".get() with null vessel");
				return null;
			}

			VesselMotionManager[] mgrs = v.gameObject.GetComponents<VesselMotionManager>();
			if (mgrs != null && mgrs.Length > 1)
				log(typeof(VesselMotionManager) + ".get(): *** WARNING *** found " + mgrs.Length);

			VesselMotionManager mgr = (mgrs != null && mgrs.Length > 0) ? mgrs[0] : null;
			if (mgr) {
				if (mgr.vessel != v)
					log(mgr.GetType() + ".get(): *** WARNING *** "
						+ mgr.desc() + " -> " + desc(v));
				mgr.vessel = v;
			}

			if (!mgr) {
				mgr = v.gameObject.AddComponent<VesselMotionManager>();
				mgr.vessel = v;
				log(typeof(VesselMotionManager) + ".get(" + desc(v) + ") created " + mgr.desc());
			}

			if (mgr.vessel)
				mgr.vesselRoot = mgr.vessel.rootPart;

			return mgr;
		}

		public void resetRotCount()
		{
			int c = rotCount;
			if (verboseEvents && c != 0)
				log("resetRotCount(): " + c + " -> RESET on " + desc());
			rotCount = 0;
		}

		public int changeCount(int delta)
		{
			int ret = rotCount + delta;
			if (ret < 0)
				ret = 0;
			if (verboseEvents && delta != 0)
				log("changeCount(" + delta + "): "
					+ rotCount + " -> " + ret + " on " + desc());

			if (ret == 0 && rotCount > 0) {
				// no action needed with IsJointUnlocked() logic (no smart autostruts)
				// but IsJointUnlocked() logic is bugged now
				log("securing autostruts on " + desc());
				vessel.CycleAllAutoStrut();
			}

			return rotCount = ret;
		}

		/******** Events ********/

		bool eventState = false;

		private void setEvents(bool cmd)
		{
			if (cmd == eventState) {
				if (verboseEvents)
					log(GetType() + ".setEvents(" + cmd + ") repeated on " + desc());
				return;
			}

			if (verboseEvents)
				log(GetType() + ".setEvents(" + cmd + ") on " + desc());

			if (cmd) {

				GameEvents.onVesselCreate.Add(OnVesselCreate);

				GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
				GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);

				GameEvents.OnCameraChange.Add(OnCameraChange);

				GameEvents.onActiveJointNeedUpdate.Add(RightBeforeStructureChangeJointUpdate);

				GameEvents.onPartCouple.Add(RightBeforeStructureChangeAction);
				GameEvents.onPartCoupleComplete.Add(RightAfterStructureChangeAction);
				GameEvents.onPartDeCouple.Add(RightBeforeStructureChangePart);
				GameEvents.onPartDeCoupleComplete.Add(RightAfterStructureChangePart);

				GameEvents.onVesselDocking.Add(RightBeforeStructureChangeIds);
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

				GameEvents.onActiveJointNeedUpdate.Remove(RightBeforeStructureChangeJointUpdate);

				GameEvents.onPartCouple.Remove(RightBeforeStructureChangeAction);
				GameEvents.onPartCoupleComplete.Remove(RightAfterStructureChangeAction);
				GameEvents.onPartDeCouple.Remove(RightBeforeStructureChangePart);
				GameEvents.onPartDeCoupleComplete.Remove(RightAfterStructureChangePart);

				GameEvents.onVesselDocking.Remove(RightBeforeStructureChangeIds);
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
			public int lastResetFrame;
			public string lastLabel;

			public void reset()
			{
				this = new StructureChangeInfo();
				this.lastResetFrame = Time.frameCount;
			}
		}

		StructureChangeInfo structureChangeInfo;

		private bool isRepeated(string label)
		{
			bool ret = structureChangeInfo.lastResetFrame == Time.frameCount;
			if (ret && verboseEvents) {
				log(GetType() + ".isRepeated(): repeated " + label
					+ " after " + structureChangeInfo.lastLabel
					+ " on " + desc());
			} else {
				structureChangeInfo.lastLabel = label;
			}
			return ret;
		}

		private bool care(Vessel v, bool useStructureChangeInfo)
		{
			bool ret = v && v == vessel;
			if (verboseCare)
				log(GetType() + ".care("
					+ desc(v)
					+ ") on "
					+ desc()
					+ " = " + ret);
			return ret;
		}

		private bool care(Part p, bool useStructureChangeInfo)
		{
			if (useStructureChangeInfo && p && p == structureChangeInfo.part) {
				if (verboseCare)
					log(GetType() + ".care(" + p.desc() + ") on " + desc() + " = " + true);
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

		private bool care(uint id1, uint id2, bool useStructureChangeInfo)
		{
			bool ret = vessel && (vessel.persistentId == id1 || vessel.persistentId == id2);
			if (verboseCare)
				log(GetType() + ".care(" + id1 + ", " + id2 + ") on " + desc() + " = " + ret);
			return ret;
		}

		private List<IStructureChangeListener> listeners()
		{
			List<IStructureChangeListener> ret = vessel.FindPartModulesImplementing<IStructureChangeListener>();
			if (verboseEvents)
				log(GetType() + ".listeners() on " + desc() + " finds " + ret.Count);
			return ret;
		}

		private List<IStructureChangeListener> listeners(Part p)
		{
			List<IStructureChangeListener> ret = p.FindModulesImplementing<IStructureChangeListener>();
			if (verboseEvents)
				log(GetType() + ".listeners(" + p.desc() + ") on " + desc() + " finds " + ret.Count);
			return ret;
		}

		private bool deadVessel()
		{
			string deadMsg = "";

			if (!vessel) {
				deadMsg = "no vessel";
			} else if (!vessel.rootPart) {
				deadMsg = "no vessel root";
			} else if (!vesselRoot) {
				deadMsg = "no original root";
			} else if (vessel.rootPart != vesselRoot) {
				deadMsg = "root changed " + vesselRoot.desc() + " -> " + vessel.rootPart.desc();
			}

			if (deadMsg.Length <= 0)
				return false;

			log(desc() + ".deadVessel(" + desc() + "): " + deadMsg);
			return true;
		}

		public void OnVesselCreate(Vessel v)
		{
			if (verboseEvents)
				log(GetType() + ".OnVesselCreate(" + desc(v) + ") on " + desc());
			get(v);
		}

		public void OnVesselGoOnRails(Vessel v)
		{
			if (verboseEvents)
				log(GetType() + ".OnVesselGoOnRails(" + desc(v) + ") on " + desc());
			if (deadVessel())
				return;
			if (!care(v, false))
				return;
			// resetRotCount(); // useless here, rotCount will go to 0 after freezing rotations
			structureChangeInfo.reset();
			listeners().map(l => l.OnVesselGoOnRails());
			onRails = true;
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (verboseEvents)
				log(GetType() + ".OnVesselGoOffRails(" + desc(v) + ") on " + desc());
			if (deadVessel())
				return;
			VesselMotionManager.get(v);
			if (!care(v, false))
				return;
			resetRotCount();
			structureChangeInfo.reset();
			onRails = false;
			listeners().map(l => l.OnVesselGoOffRails());
		}

		private void RightBeforeStructureChangeJointUpdate(Vessel v)
		{
			if (verboseEvents)
				log(GetType() + ".RightBeforeStructureChangeJointUpdate() on " + desc());
			if (!care(v, false))
				return;
			if (isRepeated("JointUpdate"))
				return;
			RightBeforeStructureChange();
		}

		public void RightBeforeStructureChangeIds(uint id1, uint id2)
		{
			if (verboseEvents)
				log(GetType() + ".RightBeforeStructureChangeIds("
					+ id1 + ", " + id2 + ") on " + desc());
			if (!care(id1, id2, false))
				return;
			if (isRepeated("Ids"))
				return;
			RightBeforeStructureChange();
		}

		public void RightBeforeStructureChangeAction(GameEvents.FromToAction<Part, Part> action)
		{
			if (verboseEvents)
				log(GetType() + ".RightBeforeStructureChangeAction("
					+ action.from.desc() + ", " + action.to.desc() + ") on " + desc());
			if (!care(action, false))
				return;
			if (isRepeated("Action"))
				return;
			RightBeforeStructureChange();
		}

		public void RightBeforeStructureChangePart(Part p)
		{
			if (verboseEvents)
				log(GetType() + ".RightBeforeStructureChangePart("
					+ desc(p.vessel) + ") on " + desc());
			if (!care(p, false))
				return;
			structureChangeInfo.part = p;
			if (isRepeated("Part"))
				return;
			RightBeforeStructureChange();
		}

		private void RightBeforeStructureChange()
		{
			if (deadVessel())
				return;
			if (isRepeated("Generic"))
				return;
			structureChangeInfo.reset();
			listeners().map(l => l.RightBeforeStructureChange());
		}

		public void RightAfterStructureChangeAction(GameEvents.FromToAction<Part, Part> action)
		{
			if (verboseEvents)
				log(GetType() + ".RightAfterStructureChangeAction("
					+ desc(action.from.vessel) + ", " + desc(action.to.vessel)
					+ ") on " + desc());
			if (!care(action, true))
				return;
			RightAfterStructureChange();
		}

		public void RightAfterStructureChangePart(Part p)
		{
			if (verboseEvents)
				log(GetType() + ".RightAfterStructureChangePart("
					+ desc(p.vessel) + ") on " + desc());
			if (!care(p, true))
				return;
			RightAfterStructureChange();
		}

		private void RightAfterStructureChange()
		{
			if (deadVessel())
				return;
			listeners().map(l => l.RightAfterStructureChange());
		}

		public void RightAfterSameVesselDock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (verboseEvents)
				log(GetType() + ".RightAfterSameVesselDock("
					+ action.from.part.desc() + "@" + desc(action.from.vessel)
					+ ", " + action.to.part.desc() + "@" + desc(action.to.vessel)
					+ ") on " + desc());
			if (deadVessel())
				return;
			if (!care(action, false))
				return;
			listeners(action.from.part).map(l => l.RightAfterStructureChange());
			listeners(action.to.part).map(l => l.RightAfterStructureChange());
		}

		public void RightAfterSameVesselUndock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (verboseEvents)
				log(GetType() + ".RightAfterSameVesselUndock("
					+ desc(action.from.vessel) + ", " + desc(action.to.vessel)
					+ ") on " + desc());
			if (deadVessel())
				return;
			if (!care(action, false))
				return;
			listeners(action.from.part).map(l => l.RightAfterStructureChange());
			listeners(action.to.part).map(l => l.RightAfterStructureChange());
		}

		public void OnCameraChange(CameraManager.CameraMode mode)
		{
			Camera camera = CameraManager.GetCurrentCamera();
			if (verboseEvents && camera) {
				log(GetType() + ".OnCameraChange(" + mode + ") on " + desc());
				Camera[] cameras = Camera.allCameras;
				for (int i = 0; i < cameras.Length; i++) {
					log("camera[" + i + "] = " + cameras[i].desc());
					log(cameras[i].transform.desc());
				}
			}
		}

		public void Awake()
		{
			log(GetType() + ".Awake() on " + desc());
			if (!vessel) {
				vessel = gameObject.GetComponent<Vessel>();
				if (verboseEvents && vessel)
					log(GetType() + ".Awake(): found vessel " + desc());
			}
			setEvents(true);
		}

		public void Start()
		{
			log(GetType() + ".Start() on " + desc());
		}

		public void OnDestroy()
		{
			log(GetType() + ".OnDestroy() on " + desc());
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

		private static bool log(string msg)
		{
			return ModuleBaseRotate.log(msg);
		}
	}
}

