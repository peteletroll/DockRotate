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
					Extensions.log(e.StackTrace);
				}
			}
		}
	}

	public class VesselMotionManager: MonoBehaviour
	{
		private Vessel _vessel = null;
		public Vessel vessel { get => _vessel; }

		private int rotCount = 0;
		public bool onRails = false;

		private bool verboseEvents = true;
		private bool verboseCare = false;

		public static VesselMotionManager get(Part p)
		{
			return p ? get(p.vessel) : null;
		}

		public static VesselMotionManager get(Vessel v)
		{
			if (!v)
				return null;

			VesselMotionManager mgr = v.gameObject.GetComponent<VesselMotionManager>();
			if (!mgr) {
				mgr = v.gameObject.AddComponent<VesselMotionManager>();
				mgr._vessel = v;
				log(nameof(VesselMotionManager), ".get(" + desc(v) + ") created " + mgr.desc());
			}

			return mgr;
		}

		public void resetRotCount()
		{
			int c = rotCount;
			if (verboseEvents && c != 0)
				log(desc(), ".resetRotCount(): " + c + " -> RESET");
			rotCount = 0;
		}

		public int changeCount(int delta)
		{
			int ret = rotCount + delta;
			if (ret < 0)
				ret = 0;

			if (rotCount == 0 && delta > 0)
				phase("START");

			if (verboseEvents && delta != 0)
				log(desc(), ".changeCount(" + delta + "): "
					+ rotCount + " -> " + ret);

			if (ret == 0 && rotCount > 0) {
				log(desc(), ": securing autostruts");
				vessel.CycleAllAutoStrut();
			}

			if (ret == 0 && delta < 0)
				phase("STOP");

			return rotCount = ret;
		}

		/******** Events ********/

		bool eventState = false;

		private void setEvents(bool cmd)
		{
			if (cmd == eventState) {
				if (verboseEvents)
					log(desc(), ".setEvents(" + cmd + ") repeated");
				return;
			}

			if (verboseEvents)
				log(desc(), ".setEvents(" + cmd + ")");

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

			public void reset(string label)
			{
				if (lastLabel == "")
					lastLabel = "Init";
				log("" + GetType(), ".reset() " + label + " after " + lastLabel);
				this = new StructureChangeInfo();
				this.lastResetFrame = Time.frameCount;
				this.lastLabel = "reset " + label;
			}

			public bool isRepeated(string label)
			{
				if (lastLabel == "")
					lastLabel = "Init";
				bool ret = lastResetFrame == Time.frameCount;
				if (ret) {
					log("" + GetType(), ".isRepeated(): repeated " + label
						+ " after " + lastLabel);
				} else {
					log("" + GetType(), ".isRepeated(): set " + label
						+ " after " + lastLabel);
					lastLabel = label;
				}
				return ret;
			}
		}

		StructureChangeInfo structureChangeInfo;

		private bool care(Vessel v, bool useStructureChangeInfo)
		{
			bool ret = v && v == vessel;
			if (verboseCare)
				log(desc(), ".care(" + desc(v) + ") = " + ret);
			return ret;
		}

		private bool care(Part p, bool useStructureChangeInfo)
		{
			if (useStructureChangeInfo && p && p == structureChangeInfo.part) {
				if (verboseCare)
					log(desc(), ".care(" + p.desc() + ") = " + true);
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
				log(desc(), ".care(" + id1 + ", " + id2 + ") = " + ret);
			return ret;
		}

		private List<IStructureChangeListener> listeners()
		{
			List<IStructureChangeListener> ret = vessel.FindPartModulesImplementing<IStructureChangeListener>();
			if (verboseEvents)
				log(desc(), ".listeners() finds " + ret.Count);
			return ret;
		}

		private List<IStructureChangeListener> listeners(Part p)
		{
			List<IStructureChangeListener> ret = p.FindModulesImplementing<IStructureChangeListener>();
			if (verboseEvents)
				log(desc(), ".listeners(" + p.desc() + ") finds " + ret.Count);
			return ret;
		}

		private bool deadVessel()
		{
			string deadMsg = "";

			if (!vessel) {
				deadMsg = "no vessel";
			} else if (!vessel.rootPart) {
				deadMsg = "no vessel root";
			}

			if (deadMsg == "")
				return false;

			log(desc(), ".deadVessel(): " + deadMsg);
			MonoBehaviour.Destroy(this);
			return true;
		}

		public void OnVesselCreate(Vessel v)
		{
			if (verboseEvents)
				log(desc(), ".OnVesselCreate(" + desc(v) + ")");
			get(v);
		}

		public void OnVesselGoOnRails(Vessel v)
		{
			if (verboseEvents)
				log(desc(), ".OnVesselGoOnRails(" + desc(v) + ")");
			if (deadVessel())
				return;
			if (!care(v, false))
				return;
			phase("BEGIN ON RAILS");
			structureChangeInfo.reset("OnRails");
			listeners().map(l => l.OnVesselGoOnRails());
			phase("END ON RAILS");
			onRails = true;
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (verboseEvents)
				log(desc(), ".OnVesselGoOffRails(" + desc(v) + ")");
			if (deadVessel())
				return;
			get(v);
			if (!care(v, false))
				return;
			phase("BEGIN OFF RAILS");
			resetRotCount();
			structureChangeInfo.reset("OffRails");
			onRails = false;
			listeners().map(l => l.OnVesselGoOffRails());
			phase("END OFF RAILS");
		}

		private void RightBeforeStructureChangeJointUpdate(Vessel v)
		{
			if (verboseEvents)
				log(desc(), ".RightBeforeStructureChangeJointUpdate()");
			if (!care(v, false))
				return;
			RightBeforeStructureChange("JointUpdate");
		}

		public void RightBeforeStructureChangeIds(uint id1, uint id2)
		{
			if (verboseEvents)
				log(desc(), ".RightBeforeStructureChangeIds("
					+ id1 + ", " + id2 + ")");
			if (!care(id1, id2, false))
				return;
			RightBeforeStructureChange("Ids");
		}

		public void RightBeforeStructureChangeAction(GameEvents.FromToAction<Part, Part> action)
		{
			if (verboseEvents)
				log(desc(), ".RightBeforeStructureChangeAction("
					+ action.from.desc() + ", " + action.to.desc() + ")");
			if (!care(action, false))
				return;
			RightBeforeStructureChange("Action");
		}

		public void RightBeforeStructureChangePart(Part p)
		{
			if (verboseEvents)
				log(desc(), ".RightBeforeStructureChangePart("
					+ desc(p.vessel) + ")");
			if (!care(p, false))
				return;
			structureChangeInfo.part = p;
			RightBeforeStructureChange("Part");
		}

		private void RightBeforeStructureChange(string label)
		{
			if (deadVessel())
				return;
			if (structureChangeInfo.isRepeated(label))
				return;
			phase("BEGIN BEFORE CHANGE");
			structureChangeInfo.reset("BeforeChange");
			listeners().map(l => l.RightBeforeStructureChange());
			phase("END BEFORE CHANGE");
		}

		public void RightAfterStructureChangeAction(GameEvents.FromToAction<Part, Part> action)
		{
			if (verboseEvents)
				log(desc(), ".RightAfterStructureChangeAction("
					+ desc(action.from.vessel) + ", " + desc(action.to.vessel) + ")");
			if (!care(action, true))
				return;
			RightAfterStructureChange();
		}

		public void RightAfterStructureChangePart(Part p)
		{
			if (verboseEvents)
				log(desc(), ".RightAfterStructureChangePart("
					+ desc(p.vessel) + ")");
			if (!care(p, true))
				return;
			RightAfterStructureChange();
		}

		private void RightAfterStructureChange()
		{
			if (deadVessel())
				return;
			phase("BEGIN AFTER CHANGE");
			listeners().map(l => l.RightAfterStructureChange());
			phase("END AFTER CHANGE");
		}

		public void RightAfterSameVesselDock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (verboseEvents)
				log(desc(), ".RightAfterSameVesselDock("
					+ action.from.part.desc() + "@" + desc(action.from.vessel)
					+ ", " + action.to.part.desc() + "@" + desc(action.to.vessel) + ")");
			if (deadVessel())
				return;
			if (!care(action, false))
				return;
			phase("BEGIN AFTER SV DOCK");
			listeners(action.from.part).map(l => l.RightAfterStructureChange());
			listeners(action.to.part).map(l => l.RightAfterStructureChange());
			phase("END AFTER SV DOCK");
		}

		public void RightAfterSameVesselUndock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (verboseEvents)
				log(desc(), ".RightAfterSameVesselUndock("
					+ desc(action.from.vessel) + ", " + desc(action.to.vessel)
					+ ")");
			if (deadVessel())
				return;
			if (!care(action, false))
				return;
			phase("BEGIN AFTER SV UNDOCK");
			listeners(action.from.part).map(l => l.RightAfterStructureChange());
			listeners(action.to.part).map(l => l.RightAfterStructureChange());
			phase("END AFTER SV UNDOCK");
		}

		public void OnCameraChange(CameraManager.CameraMode mode)
		{
			Camera camera = CameraManager.GetCurrentCamera();
			if (verboseEvents && camera) {
				log(desc(), ".OnCameraChange(" + mode + ")");
				Camera[] cameras = Camera.allCameras;
				for (int i = 0; i < cameras.Length; i++) {
					log("camera[" + i + "] = " + cameras[i].desc());
					log(cameras[i].transform.desc(10));
				}
			}
		}

		public void Awake()
		{
			log(desc(), ".Awake()");
			if (!vessel) {
				_vessel = gameObject.GetComponent<Vessel>();
				if (verboseEvents && vessel)
					log("" + GetType(), ".Awake(): found vessel " + desc());
			}
			setEvents(true);
		}

		public void Start()
		{
			log(desc(), ".Start()");
		}

		public void OnDestroy()
		{
			log(desc(), ".OnDestroy()");
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

		private void phase(string msg)
		{
			if (verboseEvents)
				log(new string('-', 10) + " " + msg + " " + new string('-', 60 - msg.Length));
		}

		protected static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}

