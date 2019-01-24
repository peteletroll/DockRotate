using UnityEngine;

namespace DockRotate
{
	public class VesselMotionManager: MonoBehaviour
	{
		public static bool trace = true;

		private Vessel vessel = null;
		private int rotCount = 0;

		public static VesselMotionManager get(Vessel v, bool create = true)
		{
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

		public void Awake()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionManager) + ".Awake(" + gameObject.name + ")");
		}

		public void Start()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionManager) + ".Start(" + gameObject.name + ")");
		}

		public void OnDestroy()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionManager) + ".OnDestroy(" + gameObject.name + ")");
		}
	}
}

