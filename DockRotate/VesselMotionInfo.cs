using System;
using System.Collections.Generic;

namespace DockRotate
{
	public class VesselMotionInfo
	{
		public static bool trace = true;

		private Guid id;
		private int rotCount = 0;

		private static Dictionary<Guid, VesselMotionInfo> vesselInfo = new Dictionary<Guid, VesselMotionInfo>();

		private VesselMotionInfo(Vessel v)
		{
			this.id = v.id;
		}

		public static VesselMotionInfo getInfo(Vessel v)
		{
			Guid id = v.id;
			if (vesselInfo.ContainsKey(id))
				return vesselInfo[id];
			return vesselInfo[id] = new VesselMotionInfo(v);
		}

		public static void resetInfo(Vessel v)
		{
			Guid id = v.id;
			int c = vesselInfo.ContainsKey(id) ? getInfo(v).rotCount : 0;
			if (trace && c != 0)
				ModuleBaseRotate.lprint("changeCount(" + id + "): " + c + " -> RESET");
			vesselInfo.Remove(id);
		}

		public int changeCount(int delta)
		{
			int ret = rotCount + delta;
			if (ret < 0)
				ret = 0;
			if (trace && delta != 0)
				ModuleBaseRotate.lprint("changeCount(" + id + ", " + delta + "): " + rotCount + " -> " + ret);
			return rotCount = ret;
		}
	}
}

