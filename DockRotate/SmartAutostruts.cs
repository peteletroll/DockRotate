using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public static class SmartAutostruts
	{
		private static bool lprint(string msg)
		{
			Debug.Log("[SmartAutostruts:" + Time.frameCount + "]: " + msg);
			return true;
		}

		/******** PartSet utilities ********/

		public class PartSet : Dictionary<uint, Part>
		{
			private Part[] partArray = null;

			public void add(Part part)
			{
				partArray = null;
				Add(part.flightID, part);
			}

			public bool contains(Part part)
			{
				return ContainsKey(part.flightID);
			}

			public Part[] parts()
			{
				if (partArray != null)
					return partArray;
				List<Part> ret = new List<Part>();
				foreach (KeyValuePair<uint, Part> i in this)
					ret.Add(i.Value);
				return partArray = ret.ToArray();
			}

			public void dump()
			{
				Part[] p = parts();
				for (int i = 0; i < p.Length; i++)
					ModuleBaseRotate.lprint("rotPart " + p[i].desc());
			}
		}

		private static PartSet allPartsFromHere(this Part p)
		{
			PartSet ret = new PartSet();
			_collect(ret, p);
			return ret;
		}

		private static void _collect(PartSet s, Part p)
		{
			s.add(p);
			for (int i = 0; i < p.children.Count; i++)
				_collect(s, p.children[i]);
		}

		/******** Object.FindObjectsOfType<PartJoint>() cache ********/

		private static PartJoint[] cached_allJoints = null;
		private static int cached_allJoints_frame = 0;

		private static PartJoint[] getAllJoints()
		{
			if (cached_allJoints != null && cached_allJoints_frame == Time.frameCount)
				return cached_allJoints;
			cached_allJoints = UnityEngine.Object.FindObjectsOfType<PartJoint>();
			cached_allJoints_frame = Time.frameCount;
			return cached_allJoints;
		}

		/******** Vessel Autostruts cache ********/

		private static PartJoint[] cached_allAutostrutJoints = null;
		private static Vessel cached_allAutostrutJoints_vessel = null;
		private static int cached_allAutostrutJoints_frame = 0;

		private static PartJoint[] getAllAutostrutJoints(Vessel vessel)
		{
			if (cached_allAutostrutJoints != null
				&& cached_allAutostrutJoints_vessel == vessel
				&& cached_allAutostrutJoints_frame == Time.frameCount)
				return cached_allAutostrutJoints;

			List<ModuleDockingNode> allDockingNodes = vessel.FindPartModulesImplementing<ModuleDockingNode>();
			List<ModuleDockingNode> sameVesselDockingNodes = new List<ModuleDockingNode>();
			for (int i = 0; i < allDockingNodes.Count; i++)
				if (allDockingNodes[i].sameVesselDockJoint)
					sameVesselDockingNodes.Add(allDockingNodes[i]);

			PartJoint[] allJoints = getAllJoints();
			List<PartJoint> allAutostrutJoints = new List<PartJoint>();
			for (int ii = 0; ii < allJoints.Length; ii++) {
				PartJoint j = allJoints[ii];
				if (!j)
					continue;
				if (!j.Host || j.Host.vessel != vessel)
					continue;
				if (!j.Target || j.Target.vessel != vessel)
					continue;
				if (j == j.Host.attachJoint)
					continue;
				if (j == j.Target.attachJoint)
					continue;

				bool isSameVesselDockingJoint = false;
				for (int i = 0; !isSameVesselDockingJoint && i < sameVesselDockingNodes.Count; i++)
					if (j == sameVesselDockingNodes[i].sameVesselDockJoint)
						isSameVesselDockingJoint = true;
				if (isSameVesselDockingJoint)
					continue;

				allAutostrutJoints.Add(j);
				lprint("Autostrut [" + allAutostrutJoints.Count + "] " + j.desc());
			}

			cached_allAutostrutJoints = allAutostrutJoints.ToArray();
			cached_allAutostrutJoints_vessel = vessel;
			cached_allAutostrutJoints_frame = Time.frameCount;
			return cached_allAutostrutJoints;
		}

		/******** public interface ********/

		public static void releaseCrossAutoStruts(this Part part)
		{
			PartSet rotParts = part.allPartsFromHere();

			PartJoint[] allAutostrutJoints = getAllAutostrutJoints(part.vessel);

			int count = 0;
			for (int ii = 0; ii < allAutostrutJoints.Length; ii++) {
				PartJoint j = allAutostrutJoints[ii];
				if (!j)
					continue;
				if (rotParts.contains(j.Host) == rotParts.contains(j.Target))
					continue;

				lprint(part.desc() + ": releasing [" + ++count + "] " + j.desc());
				j.DestroyJoint();
			}
		}
	}


}

