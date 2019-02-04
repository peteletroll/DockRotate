using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{

	public static class Extensions
	{
		private static bool log(string msg)
		{
			return ModuleBaseRotate.log(msg);
		}

		/******** Camera utilities ********/

		public static string desc(this Camera c)
		{
			if (!c)
				return "null";
			return c.name + "(" + c.cameraType + ") @ " + c.gameObject;
		}

		/******** Vessel utilities ********/

		public static void releaseAllAutoStruts(this Vessel v)
		{
			List<Part> parts = v.parts;
			for (int i = 0; i < parts.Count; i++) {
				parts[i].ReleaseAutoStruts();
			}
		}

		public static void secureAllAutoStruts(this Vessel v)
		{
			if (!v)
				return;
			v.releaseAllAutoStruts();
			v.CycleAllAutoStrut();
		}

		/******** Part utilities ********/

		public static string desc(this Part part)
		{
			if (!part)
				return "<null>";
			ModuleBaseRotate mbr = part.FindModuleImplementing<ModuleBaseRotate>();
			return part.name + "_" + part.flightID
				+ (mbr ? "_" + mbr.nodeRole : "");
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

		public static ModuleDockingNode otherNode(this ModuleDockingNode node)
		{
			// this prevents a warning
			if (node.dockedPartUId <= 0)
				return null;
			return node.FindOtherNode();
		}

		public static PartJoint dockingJoint(this ModuleDockingNode node)
		{
			if (!node || !node.part) {
				log(node.part.desc() + ".dockingJoint(): no node");
				return null;
			}

			PartJoint ret = node.sameVesselDockJoint;
			if (ret) {
				log(node.part.desc() + ".dockingJoint(): to same vessel " + ret.desc());
				return ret;
			}

			ModuleDockingNode other = node.otherNode;
			if (!other || !other.part) {
				log(node.part.desc() + ".dockingJoint(): no otherNode");
				return null;
			}

			log(node.part.desc() + ".dockingJoint(): otherNode() returns " + other.part.desc());

			if (node.part.parent == other.part) {
				ret = node.part.attachJoint;
				log(node.part.desc() + ".dockingJoint(): to parent " + ret.desc());
				return ret;
			}

			for (int i = 0; i < node.part.children.Count; i++) {
				if (node.part.children[i].parent == node.part) {
					ret = node.part.children[i].attachJoint;
					log(node.part.desc() + ".dockingJoint(): to child " + ret.desc());
					return ret;
				}
			}

			return null;
		}

		public static string allTypes(this ModuleDockingNode node)
		{
			string lst = "";
			foreach (string t in node.nodeTypes) {
				if (lst.Length > 0)
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

		public static string desc(this PartJoint j)
		{
			if (j == null)
				return "null";
			string host = j.Host.desc() + "/" + (j.Child == j.Host ? "=" : j.Child.desc());
			string target = j.Target.desc() + "/" + (j.Parent == j.Target ? "=" : j.Parent.desc());
			return host + " -> " + target;
		}

		public static void dump(this PartJoint j)
		{
			log("PartJoint " + j.desc());
			log("jAxes: " + j.Axis.desc() + " " + j.SecAxis.desc());
			log("jAnchors: " + j.HostAnchor.desc() + " " + j.TgtAnchor.desc());

			for (int i = 0; i < j.joints.Count; i++) {
				log("ConfigurableJoint[" + i + "]:");
				j.joints[i].dump(j.Host);
			}
		}

		/******** ConfigurableJoint utilities ********/

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

		public static void dump(this ConfigurableJoint j, Part p = null)
		{
			// Quaternion localToJoint = j.localToJoint();

			if (p && p.vessel) {
				p = p.vessel.rootPart;
			} else {
				p = null;
			}

			log("  Link: " + j.gameObject + " to " + j.connectedBody);
			log("  Axes: " + j.axis.desc() + ", " + j.secondaryAxis.desc());
			if (p)
				log("  AxesV: " + j.axis.Td(j.T(), j.T()).desc()
					+ ", " + j.secondaryAxis.Td(j.T(), p.T()).desc());

			log("  Anchors: " + j.anchor.desc()
				+ " -> " + j.connectedAnchor.desc()
				+ " [" + j.connectedAnchor.Tp(j.connectedBody.T(), j.T()).desc() + "]");

			log("  Tgt: " + j.targetPosition.desc() + ", " + j.targetRotation.desc());

			log("  angX: " + _jdump(j.angularXMotion, j.angularXDrive, j.lowAngularXLimit, j.angularXLimitSpring));
			log("  angY: " + _jdump(j.angularYMotion, j.angularYZDrive, j.angularYLimit, j.angularYZLimitSpring));
			log("  angZ: " + _jdump(j.angularZMotion, j.angularYZDrive, j.angularZLimit, j.angularYZLimitSpring));
			log("  linX: " + _jdump(j.xMotion, j.xDrive, j.linearLimit, j.linearLimitSpring));
			log("  linY: " + _jdump(j.yMotion, j.yDrive, j.linearLimit, j.linearLimitSpring));
			log("  linZ: " + _jdump(j.zMotion, j.zDrive, j.linearLimit, j.linearLimitSpring));

			log("  proj: " + j.projectionMode + " ang=" + j.projectionAngle + " dst=" + j.projectionDistance);
		}

		private static string _jdump(ConfigurableJointMotion mot, JointDrive drv, SoftJointLimit lim, SoftJointLimitSpring spr)
		{
			return mot.ToString() + " " + drv.desc() + " " + lim.desc() + " " + spr.desc();
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
			return angle.ToString(angle == 0 ? "F0" : "F1") + "\u00b0" + axis.desc();
		}
	}
}

