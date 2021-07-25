using System.Collections;
using UnityEngine;
using KSP.Localization;

namespace DockRotate
{
	public class VesselMotionManager: MonoBehaviour
	{
		private Vessel vessel;

		private Part rootPart = null;

		private int rotCount = 0;

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
				log(nameof(VesselMotionManager), ".get(" + v.desc() + ") created " + mgr.desc());
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

			if (delta != 0)
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

		public void Awake()
		{
			if (!vessel) {
				vessel = gameObject.GetComponent<Vessel>();
				if (vessel)
					log(desc(), ".Awake(): found vessel");
			}
		}

		public void Start()
		{
			if (!vessel) {
				vessel = gameObject.GetComponent<Vessel>();
				if (vessel)
					log(desc(), ".Start(): found vessel");
			}
			setEvents(true);
			enabled = false;
		}

		public void OnDestroy()
		{
			setEvents(false);
			log(desc(), ".OnDestroy()");
		}

		private bool eventState = false;

		private void setEvents(bool cmd)
		{
			if (cmd == eventState)
				return;

			if (cmd) {
				GameEvents.onActiveJointNeedUpdate.Add(onActiveJointNeedUpdate);
			} else {
				GameEvents.onActiveJointNeedUpdate.Remove(onActiveJointNeedUpdate);
			}

			eventState = cmd;
		}

		private void onActiveJointNeedUpdate(Vessel v)
		{
			if (v != vessel)
				return;
			log(desc(), ".onActiveJointNeedUpdate(" + v.desc() + ")");
		}

		public void scheduleDockingStatesCheck(bool verbose)
		{
			StartCoroutine(checkDockingStates(verbose));
		}

		private int dockingCheckCounter = 0;

		private IEnumerator checkDockingStates(bool verbose)
		{
			if (!HighLogic.LoadedSceneIsFlight || vessel != FlightGlobals.ActiveVessel)
				yield break;
			DockingStateChecker checker = DockingStateChecker.load();
			if (checker == null || !checker.enabledCheck)
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
			if (force)
				log(desc() + ": --- " + msg + " " + new string('-', 60 - msg.Length));
		}

		private static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}

