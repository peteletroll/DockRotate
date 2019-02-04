using System.Collections.Generic;
using UnityEngine;
using CompoundParts;

namespace DockRotate
{
	public static class SmartAutostruts
	{
		private static bool log(string msg)
		{
			Debug.Log("[SmartAutostruts:" + Time.frameCount + "]: " + msg);
			return true;
		}

		public class PartJointSet: Dictionary<int, PartJoint>
		{
			public void add(PartJoint j)
			{
				if (!j)
					return;
				Add(j.GetInstanceID(), j);
			}

			public bool contains(PartJoint j)
			{
				return ContainsKey(j.GetInstanceID());
			}
		}

		public class PartSet: Dictionary<uint, Part>
		{
			public void add(Part part)
			{
				Add(part.flightID, part);
			}

			public bool contains(Part part)
			{
				return ContainsKey(part.flightID);
			}

			public static PartSet allPartsFromHere(Part p)
			{
				PartSet ret = new PartSet();
				_collect(ret, p);
				return ret;
			}

			private static void _collect(PartSet s, Part p)
			{
				s.add(p);
				int c = p.children.Count;
				for (int i = 0; i < c; i++)
					_collect(s, p.children[i]);
			}
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

			PartJointSet jointsToKeep = new PartJointSet();

			// keep same vessel docking joints
			List<ModuleDockingNode> allDockingNodes = vessel.FindPartModulesImplementing<ModuleDockingNode>();
			for (int i = 0; i < allDockingNodes.Count; i++)
				jointsToKeep.add(allDockingNodes[i].sameVesselDockJoint);

			// keep strut joints
			List<CModuleStrut> allStruts = vessel.FindPartModulesImplementing<CModuleStrut>();
			for (int i = 0; i < allStruts.Count; i++)
				jointsToKeep.add(allStruts[i].strutJoint);

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

				if (j == j.Host.attachJoint || j == j.Target.attachJoint)
					continue;

				if (jointsToKeep.contains(j))
					continue;

				allAutostrutJoints.Add(j);
				log("Autostrut [" + allAutostrutJoints.Count + "] " + j.desc());
			}

			cached_allAutostrutJoints = allAutostrutJoints.ToArray();
			cached_allAutostrutJoints_vessel = vessel;
			cached_allAutostrutJoints_frame = Time.frameCount;
			return cached_allAutostrutJoints;
		}

		/******** public interface ********/

		public static void releaseCrossAutoStruts(this Part part)
		{
			PartSet rotParts = PartSet.allPartsFromHere(part);

			PartJoint[] allAutostrutJoints = getAllAutostrutJoints(part.vessel);

			int count = 0;
			for (int ii = 0; ii < allAutostrutJoints.Length; ii++) {
				PartJoint j = allAutostrutJoints[ii];
				if (!j)
					continue;
				if (rotParts.contains(j.Host) == rotParts.contains(j.Target))
					continue;

				log(part.desc() + ": releasing [" + ++count + "] " + j.desc());
				j.DestroyJoint();
			}
		}
	}
}

