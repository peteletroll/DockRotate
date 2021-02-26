using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;

namespace DockRotate
{
	public interface IStructureChangeListener
	{
		void RightBeforeStructureChange();
		void RightAfterStructureChange();
		bool wantsVerboseEvents();
		Part getPart();
		int getRevision();
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
		private Vessel vessel;

		private int Revision = -1;

		private Part rootPart = null;

		private int rotCount = 0;

		private bool verboseEvents = false;

		public bool verbose()
		{
			return verboseEvents;
		}

		public static VesselMotionManager get(Vessel v)
		{
			if (!v)
				return null;
			if (!v.loaded)
				log(nameof(VesselMotionManager), ".get(" + v.desc() + ") called on unloaded vessel");
			if (!v.rootPart)
				log(nameof(VesselMotionManager), ".get(" + v.desc() + ") called on rootless vessel");

			VesselMotionManager mgr = null;
			VesselMotionManager[] mgrs = v.GetComponents<VesselMotionManager>();
			if (mgrs != null) {
				for (int i = 0; i < mgrs.Length; i++) {
					if (mgrs[i].vessel == v && mgrs[i].rootPart == v.rootPart && !mgr) {
						mgr = mgrs[i];
					} else {
						log(nameof(VesselMotionManager), ".get(" + v.desc() + ") found incoherency with " + mgrs[i].desc());
						Destroy(mgrs[i]);
					}
				}
			}

			if (!mgr) {
				mgr = v.gameObject.AddComponent<VesselMotionManager>();
				mgr.vessel = v;
				mgr.rootPart = v.rootPart;
				log(nameof(VesselMotionManager), ".get(" + v.desc() + ") created " + mgr.desc()
					+ " [" + mgr.listeners().Count + "]");
			}

			return mgr;
		}

		public void resetRotCount()
		{
			if (rotCount != 0)
				log(desc(), ".resetRotCount(): " + rotCount + " -> RESET");
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
				vessel.KJRNextCycleAllAutoStrut();
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

			} else {

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

		private bool care(Vessel v)
		{
			bool ret = v && v == vessel;
			if (verboseEvents)
				log(desc(), ".care(" + v.desc() + ") = " + ret + " on " + vessel.desc());
			return ret;
		}

		private bool care(Part p, bool useStructureChangeInfo)
		{
			if (useStructureChangeInfo && p && p == structureChangeInfo.part) {
				if (verboseEvents)
					log(desc(), ".care(" + p.desc() + ") = " + true);
				return true;
			}
			return p && care(p.vessel);
		}

		private bool care(GameEvents.FromToAction<Part, Part> action, bool useStructureChangeInfo)
		{
			return care(action.from, useStructureChangeInfo) || care(action.to, useStructureChangeInfo);
		}

		private bool care(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action, bool useStructureChangeInfo)
		{
			return care(action.from.part, useStructureChangeInfo) || care(action.to.part, useStructureChangeInfo);
		}

		private bool care(uint id1, uint id2)
		{
			bool ret = vessel && (vessel.persistentId == id1 || vessel.persistentId == id2);
			if (verboseEvents)
				log(desc(), ".care(" + id1 + ", " + id2 + ") = " + ret);
			return ret;
		}

		public List<IStructureChangeListener> listeners()
		{
			List<IStructureChangeListener> ret = vessel.FindPartModulesImplementing<IStructureChangeListener>();

			bool verboseEventsPrev = verboseEvents;
			verboseEvents = false;

			int l = ret.Count;
			for (int i = 0; i < l; i++) {
				if (ret[i] == null)
					continue;
				if (ret[i].getRevision() > Revision)
					Revision = ret[i].getRevision();
				if (ret[i].wantsVerboseEvents()) {
					log(desc(), ": " + ret[i].getPart().desc() + " wants verboseEvents");
					verboseEvents = true;
					break;
				}
			}
			if (verboseEvents || verboseEventsPrev)
				log(desc(), ".listeners() finds " + ret.Count);

			if (verboseEvents != verboseEventsPrev)
				log(desc(), ".listeners(): verboseEvents changed to " + verboseEvents);

			return ret;
		}

		public List<IStructureChangeListener> listeners(Part p)
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
			} else if (vessel.rootPart != rootPart) {
				deadMsg = "root part changed";
			} else if (vessel.rootPart.vessel != vessel) {
				deadMsg = "vessel incoherency";
			}

			if (deadMsg == "")
				return false;

			log(desc(), ".deadVessel(): " + deadMsg);
			Destroy(this);
			return true;
		}

		private void RightBeforeStructureChange_JointUpdate(Vessel v)
		{
			if (verboseEvents)
				log(desc(), ".RightBeforeStructureChange_JointUpdate()");
			if (!care(v))
				return;
			RightBeforeStructureChange("JointUpdate");
		}

		public void RightBeforeStructureChange_Ids(uint id1, uint id2)
		{
			if (verboseEvents)
				log(desc(), ".RightBeforeStructureChange_Ids("
					+ id1 + ", " + id2 + ")");
			if (!care(id1, id2))
				return;
			RightBeforeStructureChange("Ids");
		}

		public void RightBeforeStructureChange_Action(GameEvents.FromToAction<Part, Part> action)
		{
			if (verboseEvents)
				log(desc(), ".RightBeforeStructureChange_Action("
					+ action.from.desc() + ", " + action.to.desc() + ")");
			if (!care(action, false))
				return;
			RightBeforeStructureChange("Action");
		}

		public void RightBeforeStructureChange_Part(Part p)
		{
			if (verboseEvents)
				log(desc(), ".RightBeforeStructureChange_Part("
					+ p.vessel.desc() + ")");
			if (!care(p, false))
				return;
			structureChangeInfo.part = p;
			RightBeforeStructureChange("Part");
		}

		private void RightBeforeStructureChange(string label)
		{
			phase("BEGIN BEFORE CHANGE");
			if (!deadVessel() && !structureChangeInfo.isRepeated(label)) {
				structureChangeInfo.reset("BeforeChange");
				listeners().map(l => l.RightBeforeStructureChange());
			}
			phase("END BEFORE CHANGE");
		}

		public void RightAfterStructureChange_Action(GameEvents.FromToAction<Part, Part> action)
		{
			if (verboseEvents)
				log(desc(), ".RightAfterStructureChange_Action("
					+ action.from.vessel.desc() + ", " + action.to.vessel.desc() + ")");
			if (!care(action, true))
				return;
			RightAfterStructureChange();
		}

		public void RightAfterStructureChange_Part(Part p)
		{
			if (verboseEvents)
				log(desc(), ".RightAfterStructureChange_Part("
					+ p.vessel.desc() + ")");
			if (!care(p, true))
				return;
			RightAfterStructureChange();
		}

		private void RightAfterStructureChange()
		{
			phase("BEGIN AFTER CHANGE");
			if (!deadVessel())
				listeners().map(l => l.RightAfterStructureChange());
			phase("END AFTER CHANGE");
			scheduleDockingStatesCheck(false);
		}

		public void Awake()
		{
			if (!vessel) {
				vessel = gameObject.GetComponent<Vessel>();
				if (verboseEvents && vessel)
					log(desc(), ".Awake(): found vessel");
			}
			setEvents(true);
		}

		public void Start()
		{
			listeners(); // just to set verboseEvents
			enabled = false;
		}

		public void OnDestroy()
		{
			log(desc(), ".OnDestroy()");
			setEvents(false);
		}

		public void scheduleDockingStatesCheck(bool verbose)
		{
			StartCoroutine(checkDockingStates(verbose));
		}

		private int dockingCheckCounter = 0;

		public IEnumerator checkDockingStates(bool verbose)
		{
			if (!HighLogic.LoadedSceneIsFlight || vessel != FlightGlobals.ActiveVessel)
				yield break;
			DockingStateChecker checker = DockingStateChecker.load();
			if (checker == null)
				yield break;
			int thisCounter = ++dockingCheckCounter;

			int waitFrame = Time.frameCount + checker.checkDelay;
			while (Time.frameCount < waitFrame)
				yield return new WaitForFixedUpdate();

			if (thisCounter < dockingCheckCounter) {
				log("skipping analysis, another pending");
			} else {
				log((verbose ? "verbosely " : "")
					+ "analyzing incoherent states in " + vessel.GetName());
				DockingStateChecker.Result result = checker.checkVessel(vessel, verbose);
				if (result.foundError)
					ScreenMessages.PostScreenMessage(Localizer.Format("#DCKROT_bad_states"),
						checker.messageTimeout, checker.messageStyle, checker.colorBad);
			}
		}

		private string desc()
		{
			return "VMM:" + GetInstanceID() + ":" + vessel.desc(true);
		}

		private void phase(string msg, bool force = false)
		{
			if (verboseEvents || force)
				log(desc() + ": --- " + msg + " " + new string('-', 60 - msg.Length));
		}

		protected static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}

