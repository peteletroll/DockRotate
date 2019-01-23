using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class VesselMotionInfo: MonoBehaviour
	{
		public static bool trace = true;

		private Vessel vessel = null;
		private int rotCount = 0;

		public static VesselMotionInfo get(Vessel v)
		{
			Guid id = v.id;
			VesselMotionInfo info = v.gameObject.GetComponent<VesselMotionInfo>();
			if (!info) {
				info = v.gameObject.AddComponent<VesselMotionInfo>();
				info.vessel = v;
				ModuleBaseRotate.lprint("created VesselMotionInfo for " + v.name);
			}
			return info;
		}

		public static void resetInfo(Vessel v)
		{
			VesselMotionInfo info = get(v);
			int c = info.rotCount;
			if (trace && c != 0)
				ModuleBaseRotate.lprint("resetInfo(" + v.name + "): " + c + " -> RESET");
			info.rotCount = 0;
		}

		public int changeCount(int delta)
		{
			int ret = rotCount + delta;
			if (ret < 0)
				ret = 0;
			if (trace && delta != 0)
				ModuleBaseRotate.lprint("changeCount(" + vessel.name + ", " + delta + "): "
					+ rotCount + " -> " + ret);
			return rotCount = ret;
		}

		/******** Events ********/

		public void Awake()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionInfo) + ".Awake(" + gameObject.name + ")");
		}

		public void Start()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionInfo) + ".Start(" + gameObject.name + ")");
		}

		public void OnDestroy()
		{
			ModuleBaseRotate.lprint(nameof(VesselMotionInfo) + ".OnDestroy(" + gameObject.name + ")");
		}
	}
}

