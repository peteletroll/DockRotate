using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public static class Extensions
	{
		/******** logging ********/

		private static int framelast = 0;
		private static string msg1last = "";

		public static bool log(string msg1, string msg2 = "")
		{
			int now = Time.frameCount;
			if (msg2 == "") {
				msg1last = "";
			} else {
				if (msg1 == msg1last && framelast == now) {
					msg1 = "    ... ";
				} else {
					framelast = now;
					msg1last = msg1;
				}
			}
			Debug.Log("[DR:" + now + "] " + msg1 + msg2);
			return true;
		}

		/******** Camera utilities ********/

		public static string desc(this Camera c)
		{
			if (!c)
				return "null";
			return c.name + "(" + c.cameraType + ") @ " + c.gameObject;
		}

		/******** Part utilities ********/

		public static string desc(this Part part, bool bare = false)
		{
			if (!part)
				return "null";
			ModuleBaseRotate mbr = part.FindModuleImplementing<ModuleBaseRotate>();
			return (bare ? "" : "P:") + part.bareName() + ":" + part.flightID
				+ (mbr ? ":" + mbr.nodeRole : "");
		}

		public static string bareName(this Part part)
		{
			if (!part)
				return "null";
			int s = part.name.IndexOf(' ');
			return s > 1 ? part.name.Remove(s) : part.name;
		}

		public static Vector3 up(this Part part, Vector3 axis)
		{
			Vector3 up1 = Vector3.ProjectOnPlane(Vector3.up, axis);
			Vector3 up2 = Vector3.ProjectOnPlane(Vector3.forward, axis);
			return (up1.magnitude >= up2.magnitude ? up1 : up2).normalized;
		}

		/******** Physics Activation utilities ********/

		public static bool hasPhysics(this Part part)
		{
			bool ret = (part.physicalSignificance == Part.PhysicalSignificance.FULL);
			if (ret != part.rb) {
				log(part.desc() + ": hasPhysics() Rigidbody incoherency: "
					+ part.physicalSignificance + ", " + (part.rb ? "rb ok" : "rb null"));
				ret = part.rb;
			}
			return ret;
		}

		public static bool forcePhysics(this Part part)
		{
			if (!part || part.hasPhysics())
				return false;

			log(part.desc() + ": calling PromoteToPhysicalPart(), "
				+ part.physicalSignificance + ", " + part.PhysicsSignificance);
			part.PromoteToPhysicalPart();
			log(part.desc() + ": called PromoteToPhysicalPart(), "
				+ part.physicalSignificance + ", " + part.PhysicsSignificance);
			if (part.parent) {
				if (part.attachJoint) {
					log(part.desc() + ": parent joint exists already: " + part.attachJoint.desc());
				} else {
					AttachNode nodeHere = part.FindAttachNodeByPart(part.parent);
					AttachNode nodeParent = part.parent.FindAttachNodeByPart(part);
					AttachModes m = (nodeHere != null && nodeParent != null) ?
						AttachModes.STACK : AttachModes.SRF_ATTACH;
					part.CreateAttachJoint(m);
					log(part.desc() + ": created joint " + m + " " + part.attachJoint.desc());
				}
			}

			return true;
		}

		/******** ModuleDockingMode utilities ********/

		public static string allTypes(this ModuleDockingNode node)
		{
			string lst = "";
			foreach (string t in node.nodeTypes) {
				if (lst != "")
					lst += ",";
				lst += t;
			}
			return lst;
		}

		/******** AttachNode utilities ********/

		public static string desc(this AttachNode n)
		{
			if (n == null)
				return "null";
			return "[\"" + n.id + "\": "
				+ n.owner.desc() + " -> " + n.attachedPart.desc()
				+ ", size " + n.size + "]";
		}

		/******** PartJoint utilities ********/

		public static string desc(this PartJoint j, bool bare = false)
		{
			if (j == null)
				return "null";
			string host = j.Host.desc(true) + (j.Child == j.Host ? "" : "/" + j.Child.desc(true));
			string target = j.Target.desc(true) + (j.Parent == j.Target ? "" : "/" + j.Parent.desc(true));
			int n = j.joints.Count;
			return (bare ? "" : "PJ:") + host + new string('>', n) + target;
		}

		public static void dump(this PartJoint j)
		{
			log("PartJoint " + j.desc());
			log("jAxes: " + j.Axis.desc() + " " + j.SecAxis.desc());
			log("jAnchors: " + j.HostAnchor.desc() + " " + j.TgtAnchor.desc());

			for (int i = 0; i < j.joints.Count; i++) {
				log("ConfigurableJoint[" + i + "]:");
				j.joints[i].dump();
			}
		}

		/******** ConfigurableJoint utilities ********/

		public static void reconfigureForRotation(this ConfigurableJoint joint)
		{
			ConfigurableJointMotion f = ConfigurableJointMotion.Free;
			joint.angularXMotion = f;
			joint.angularYMotion = f;
			joint.angularZMotion = f;
			joint.xMotion = f;
			joint.yMotion = f;
			joint.zMotion = f;
		}

		public static void dump(this ConfigurableJoint j)
		{
			log("  Link: " + j.gameObject + " to " + j.connectedBody);
			log("  Axes: " + j.axis.desc() + ", " + j.secondaryAxis.desc());

			log("  Anchors: " + j.anchor.desc()
				+ " -> " + j.connectedAnchor.desc()
				+ " [" + j.connectedAnchor.Tp(j.connectedBody.T(), j.T()).desc() + "]");

			log("  Tgt: " + j.targetPosition.desc() + ", " + j.targetRotation.desc());

			log("  angX: " + desc(j.angularXMotion, j.angularXDrive, j.lowAngularXLimit, j.angularXLimitSpring));
			log("  angY: " + desc(j.angularYMotion, j.angularYZDrive, j.angularYLimit, j.angularYZLimitSpring));
			log("  angZ: " + desc(j.angularZMotion, j.angularYZDrive, j.angularZLimit, j.angularYZLimitSpring));
			log("  linX: " + desc(j.xMotion, j.xDrive, j.linearLimit, j.linearLimitSpring));
			log("  linY: " + desc(j.yMotion, j.yDrive, j.linearLimit, j.linearLimitSpring));
			log("  linZ: " + desc(j.zMotion, j.zDrive, j.linearLimit, j.linearLimitSpring));

			log("  proj: " + j.projectionMode + " ang=" + j.projectionAngle + " dst=" + j.projectionDistance);
		}

		public static string desc(ConfigurableJointMotion mot, JointDrive drv, SoftJointLimit lim, SoftJointLimitSpring spr)
		{
			return mot.ToString() + " " + drv.desc() + " " + lim.desc() + " " + spr.desc();
		}

		public static string desc(this JointDrive drive)
		{
			return "drv(frc=" + drive.maximumForce
				+ " spr=" + drive.positionSpring
				+ " dmp=" + drive.positionDamper
				+ ")";
		}

		public static string desc(this SoftJointLimit limit)
		{
			return "lim(lim=" + limit.limit
				+ " bnc=" + limit.bounciness
				+ " dst=" + limit.contactDistance
				+ ")";
		}

		public static string desc(this SoftJointLimitSpring spring)
		{
			return "spr(spr=" + spring.spring
				+ " dmp=" + spring.damper
				+ ")";
		}

		/******** Vector3 utilities ********/

		public static Quaternion rotation(this Vector3 axis, float angle)
		{
			return Quaternion.AngleAxis(angle, axis);
		}

		public static float axisSignedAngle(this Vector3 axis, Vector3 v1, Vector3 v2)
		{
			v1 = Vector3.ProjectOnPlane(v1, axis).normalized;
			v2 = Vector3.ProjectOnPlane(v2, axis).normalized;
			float angle = Vector3.Angle(v1, v2);
			float s = Vector3.Dot(axis, Vector3.Cross(v1, v2));
			return (s < 0) ? -angle : angle;
		}

		public static string desc(this Vector3 v)
		{
			return v.ToString(v == Vector3.zero ? "F0" : "F2");
		}

		public static string ddesc(this Vector3 v, Part p)
		{
			string ret = v.desc();
			if (p && p.vessel.rootPart) {
				ret += " VSL" + v.Td(p.T(), p.vessel.rootPart.T()).desc();
			} else {
				ret += " (no vessel)";
			}
			return ret;
		}

		public static string pdesc(this Vector3 v, Part p)
		{
			string ret = v.desc();
			if (p && p.vessel.rootPart) {
				ret += " VSL" + v.Tp(p.T(), p.vessel.rootPart.T()).desc();
			} else {
				ret += " (no vessel)";
			}
			return ret;
		}

		/******** Quaternion utilities ********/

		public static Quaternion inverse(this Quaternion q)
		{
			return Quaternion.Inverse(q);
		}

		public static string desc(this Quaternion q)
		{
			float angle;
			Vector3 axis;
			q.ToAngleAxis(out angle, out axis);
			if (angle == 0f)
				axis = Vector3.zero;
			return angle.ToString(angle == 0f ? "F0" : "F1") + "\u00b0" + axis.desc();
		}
	}
}

