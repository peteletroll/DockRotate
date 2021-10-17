using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class JointWelder
	{
		private PartJoint joint;
		private Part newParentPart, newChildPart;
		private bool valid = false;

		public static JointWelder get(PartJoint joint)
		{
			JointWelder ret = new JointWelder(joint);
			return ret.valid ? ret : null;
		}

		private JointWelder(PartJoint joint)
		{
			this.joint = joint;
			string sep = new string('-', 60);
			log(sep);
			this.valid = setup(true);
			log("WELDABLE " + this.valid);
			log(sep);
		}

		private bool setup(bool verbose = false)
		{
			if (!joint) {
				if (verbose)
					log("setup(): no joint");
				return false;
			}
			if (joint != joint.Host.attachJoint) {
				if (verbose)
					log(joint.desc() + "setup(): not attachJoint");
				return false;
			}

			if (!weldable(joint.Host, verbose) || !weldable(joint.Target, verbose))
				return false;

			newChildPart = joint.Host.children[0];
			newParentPart = joint.Target.parent;

			for (Part p = newChildPart; p && p != newParentPart.parent; p = p.parent)
				dumpNodes(p);

			AttachNode childNode = joint.Host.FindAttachNodeByPart(newChildPart);
			AttachNode parentNode = joint.Target.FindAttachNodeByPart(newParentPart);
			log("CNODE " + childNode.position.ToString("F2")+ " " + childNode.desc());
			log("PNODE " + parentNode.position.ToString("F2") + " " + parentNode.desc());
			if (childNode == null || parentNode == null) {
				if (verbose)
					log("setup(): missing node");
				return false;
			}

			Vector3 childOffset = parentNode.position.STp(parentNode.owner, newChildPart)
				- childNode.position.STp(childNode.owner, newChildPart);
			log("DIFF " + childOffset.magnitude.ToString("F2") + " " + childOffset.ToString("F2"));

			ModuleDockingNode mdn = joint.Host.FindModuleImplementing<ModuleDockingNode>();
			if (!mdn) {
				if (verbose)
					log("setup(): no ModuleDockingNode");
				return false;
			}
			Vector3 childAxis = Vector3.forward.Td(mdn.T(), joint.Target.T()).STd(joint.Target, newChildPart);
			log("AXIS " + childAxis.magnitude.ToString("F2") + " " + childAxis.ToString("F2"));
			childOffset = Vector3.Project(childOffset, childAxis);
			log("OFFS " + childOffset.magnitude.ToString("F2") + " " + childOffset.ToString("F2"));

			return true;
		}

		private static bool weldable(Part p, bool verbose)
		{
			if (!p.parent) {
				if (verbose)
					log(p.desc() + ".weldable(): no parent");
				return false;
			}
			if (p.children == null) {
				if (verbose)
					log(p.desc() + ".weldable(): no children");
				return false;
			}
			if (p.children.Count != 1) {
				if (verbose)
					log(p.desc() + ".weldable(): has " + p.children.Count + " children");
				return false;
			}
			int nn = p.namedAttachNodes(false).Count;
			if (nn != 2) {
				if (verbose)
					log(p.desc() + ".weldable(): has " + nn + " nodes");
				return false;
			}
			return true;
		}

		private void dumpNodes(Part p)
		{
			List<AttachNode> nodes = p.namedAttachNodes();

			string desc = p.desc() + " nodes:";

			for (int i = 0; i < nodes.Count; i++) {
				AttachNode n = nodes[i];
				desc += "\n\t[" + i + "] "+ n.id + " -> " + n.attachedPart.desc();
				AttachNode c = n.getConnectedNode(false);
				if (c != null)
					desc += ", " + c.desc();
			}
			log(desc);
		}

		protected static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}
