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

		public static VesselMotionInfo getInfo(Vessel v)
		{
			Guid id = v.id;
			VesselMotionInfo info = v.gameObject.GetComponent<VesselMotionInfo>();
			if (!info) {
				info = v.gameObject.GetComponent<VesselMotionInfo>();
				info.vessel = v;
				ModuleBaseRotate.lprint("created VesselMotionInfo for " + v.name);
			}
			return info;
		}

		public static void resetInfo(Vessel v)
		{
			VesselMotionInfo info = getInfo(v);
			int c = info.rotCount;
			if (trace && c != 0)
				ModuleBaseRotate.lprint("changeCount(" + v.name + "): " + c + " -> RESET");
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
	}
}

