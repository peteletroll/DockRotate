using System;
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

	public class VesselMotionManager: MonoBehaviour
	{
		public static bool trace = true;

		private Vessel vessel = null;
		private int rotCount = 0;
		private bool verboseEvents = false;
		public bool onRails = false;

		public static VesselMotionManager get(Vessel v, bool create = true)
		{
			if (!v) {
				lprint("*** WARNING *** " + nameof(VesselMotionManager) + ".get() with null vessel");
				return null;
			}
			VesselMotionManager info = v.gameObject.GetComponent<VesselMotionManager>();
			if (!info && create) {
				info = v.gameObject.AddComponent<VesselMotionManager>();
				info.vessel = v;
				ModuleBaseRotate.lprint("created VesselMotionInfo " + info.GetInstanceID() + " for " + v.name);
			}
			return info;
		}

		public static void resetInfo(Vessel v)
		{
			VesselMotionManager info = get(v, false);
			if (!info)
				return;
			int c = info.rotCount;
			if (trace && c != 0)
				ModuleBaseRotate.lprint("resetInfo(" + info.GetInstanceID() + "): " + c + " -> RESET");
			info.rotCount = 0;
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

		private void setEvents(bool cmd)
		{
			if (cmd) {

				GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
				GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);

				GameEvents.OnCameraChange.Add(OnCameraChange);

				/*

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

				*/

			} else {

				GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
				GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);

				GameEvents.OnCameraChange.Remove(OnCameraChange);

				/*

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

				*/
			}
		}

		private bool dontCare(Vessel v)
		{
			return !v || !vessel || v != vessel;
		}

		private IStructureChangeListener[] allListeners()
		{
			return vessel.FindPartModulesImplementing<IStructureChangeListener>().ToArray();
		}

		private void onAllListeners(Action<IStructureChangeListener> a)
		{
			IStructureChangeListener[] l = allListeners();
			for (int i = 0; i < l.Length; i++) {
				if (l[i] == null)
					continue;
				try {
					a(l[i]);
				} catch (Exception e) {
					lprint(e.StackTrace);
				}
			}
		}

		public void OnVesselGoOnRails(Vessel v)
		{
			if (dontCare(v))
				return;
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".OnVesselGoOnRails(" + v.persistentId + ") [" + vessel.persistentId + "]");
			resetInfo(vessel);
			onRails = true;
			onAllListeners(l => l.OnVesselGoOnRails());
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (dontCare(v))
				return;
			if (verboseEvents)
				lprint(nameof(VesselMotionManager) + ".OnVesselGoOffRails(" + v.persistentId + ") [" + vessel.persistentId + "]");
			resetInfo(vessel);
			onRails = false;
			onAllListeners(l => l.OnVesselGoOffRails());
		}

		public void OnCameraChange(CameraManager.CameraMode mode)
		{
			Camera camera = CameraManager.GetCurrentCamera();
			if (verboseEvents && camera) {
				lprint(nameof(VesselMotionManager) + ".OnCameraChange(" + mode + "): " + camera.desc());
				Camera[] cameras = Camera.allCameras;
				for (int i = 0; i < cameras.Length; i++)
					lprint("camera[" + i + "] = " + cameras[i].desc());
			}
		}

		public void Awake()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionManager) + ".Awake(" + gameObject.name + ")");
			setEvents(true);
		}

		public void Start()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionManager) + ".Start(" + gameObject.name + ")");
		}

		public void OnDestroy()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionManager) + ".OnDestroy(" + gameObject.name + ")");
			setEvents(false);
		}

		private static void lprint(string msg)
		{
			ModuleBaseRotate.lprint(msg);
		}
	}
}

