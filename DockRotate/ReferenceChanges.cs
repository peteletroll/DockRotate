using UnityEngine;

namespace DockRotate
{
	public static class DynamicReferenceChanges
	{
		public static string desc(this Transform t, int parents = 0)
		{
			if (!t)
				return "world";
			string ret = t.name + ":" + t.GetInstanceID() + ":"
				+ t.localRotation.desc() + "@" + t.localPosition.desc()
				+ "/"
				+ t.rotation.desc() + "@" + t.position.desc();
			if (parents > 0)
				ret += "\n\t< " + t.parent.desc(parents - 1);
			return ret;
		}

		public static Vector3 Td(this Vector3 v, Transform from, Transform to)
		{
			if (from)
				v = from.TransformDirection(v);
			if (to)
				v = to.InverseTransformDirection(v);
			return v;
		}

		public static Vector3 Tp(this Vector3 v, Transform from, Transform to)
		{
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

	public struct StaticTransform
	{
		Quaternion rotation;
		Vector3 position;

		public static implicit operator StaticTransform(Part p)
		{
			StaticTransform ret;
			ret.rotation = p.orgRot;
			ret.position = p.orgPos;
			return ret;
		}

		public static implicit operator StaticTransform(Transform t)
		{
			StaticTransform ret;
			ret.rotation = t.rotation;
			ret.position = t.position;
			return ret;
		}

		public Quaternion inverse()
		{
			StaticTransform ret;
			ret.rotation = rotation.inverse();
			ret.position = -(ret.rotation * position);
			return rotation;
		}

		public string desc()
		{
			return rotation.desc() + "@" + position.desc();
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
			return to.orgRot.inverse() * (from.orgRot * v);
		}

		public static Vector3 STp(this Vector3 v, Part from, Part to)
		{
			Vector3 vv = from.orgPos + from.orgRot * v;
			return to.orgRot.inverse() * (vv - to.orgPos);
		}
	}
}

