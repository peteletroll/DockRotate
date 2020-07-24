using UnityEngine;

namespace DockRotate
{
	public static class DynamicReferenceChanges
	{
		public static string desc(this Transform t, int parents = 0)
		{
			if (!t)
				return "world";
			string ret = t.name + ":" + t.GetInstanceID()
				+ (t.gameObject.GetComponent<Vessel>() ? ":V" : "")
				+ (t.gameObject.GetComponent<Part>() ? ":P" : "")
				+ (t.gameObject.GetComponent<InternalModel>() ? ":I" : "")
				+ ":" + t.localRotation.desc() + "@" + t.localPosition.desc()
				+ "/" + t.rotation.desc() + "@" + t.position.desc();
			if (parents > 0)
				ret += "\n\t< " + t.parent.desc(parents - 1);
			return ret;
		}

		public static Vector3 Td(this Vector3 v, Transform from, Transform to)
		{
			if (to == from)
				return v;
			if (from)
				v = from.TransformDirection(v);
			if (to)
				v = to.InverseTransformDirection(v);
			return v;
		}

		public static Vector3 Tp(this Vector3 v, Transform from, Transform to)
		{
			if (to == from)
				return v;
			if (from)
				v = from.TransformPoint(v);
			if (to)
				v = to.InverseTransformPoint(v);
			return v;
		}

		public static Transform T(this Part p)
		{
			return p.transform;
		}

		public static Transform T(this ConfigurableJoint j)
		{
			return j.transform;
		}

		public static Transform T(this Rigidbody b)
		{
			return b.transform;
		}

		public static Transform T(this ModuleDockingNode m)
		{
			return m.nodeTransform;
		}
	}

	public static class StaticReferenceChanges
	{
		public static string descOrg(this Part p)
		{
			return p ? p.orgRot.desc() + "@" + p.orgPos.desc() : "null-part";
		}

		public static Vector3 STd(this Vector3 v, Part from, Part to)
		{
			if (to == from)
				return v;
			// return to.orgRot.inverse() * (from.orgRot * v);
			Vessel refVessel = to.vessel;
			return Part.VesselToPartSpaceDir(
				Part.PartToVesselSpaceDir(v, from, refVessel, PartSpaceMode.Pristine),
				to, refVessel, PartSpaceMode.Pristine);
		}

		public static Vector3 STp(this Vector3 v, Part from, Part to)
		{
			if (to == from)
				return v;
			// Vector3 vv = from.orgPos + from.orgRot * v;
			// return to.orgRot.inverse() * (vv - to.orgPos);
			Vessel refVessel = to.vessel;
			return Part.VesselToPartSpacePos(
				Part.PartToVesselSpacePos(v, from, refVessel, PartSpaceMode.Pristine),
				to, refVessel, PartSpaceMode.Pristine);
		}
	}
}

