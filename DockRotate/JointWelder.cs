using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class JointWelder
	{
		private PartJoint joint;
		private ModuleDockRotate parentDR, childDR;
		private Part parentPart, childPart;
		private Part newParentPart, newChildPart;
		private Vector3 newParentOffset;
		private bool valid = false;

		public static JointWelder get(PartJoint joint, bool verbose)
		{
			JointWelder ret = new JointWelder(joint, verbose);
			return ret.valid ? ret : null;
		}

		private JointWelder(PartJoint joint, bool verbose)
		{
			this.joint = joint;
			string sep = new string('-', 60);
			if (verbose)
				log(sep);
			this.valid = setup(verbose);
			if (verbose)
				log("WELDABLE " + this.valid);
			if (verbose)
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

			childPart = joint.Host;
			childDR = childPart.FindModuleImplementing<ModuleDockRotate>();
			parentPart = joint.Target;
			parentDR = parentPart.FindModuleImplementing<ModuleDockRotate>();

			if (!weldable(childPart, verbose) || !weldable(parentPart, verbose))
				return false;

			newChildPart = childPart.children[0];
			newParentPart = parentPart.parent;

			if (verbose)
				for (Part p = newChildPart; p && p != newParentPart.parent; p = p.parent)
					dumpNodes(p);

			AttachNode childNode = childPart.FindAttachNodeByPart(newChildPart);
			AttachNode parentNode = parentPart.FindAttachNodeByPart(newParentPart);
			if (verbose) {
				log("CNODE " + childNode.position.ToString("F2") + " " + childNode.desc());
				log("PNODE " + parentNode.position.ToString("F2") + " " + parentNode.desc());
			}

			if (childNode == null || parentNode == null) {
				if (verbose)
					log("setup(): missing node");
				return false;
			}

			newParentOffset = parentNode.position.STp(parentNode.owner, newChildPart)
				- childNode.position.STp(childNode.owner, newChildPart);
			if (verbose)
				log("DIFF " + newParentOffset.magnitude.ToString("F2") + " " + newParentOffset.ToString("F2"));

			ModuleDockingNode mdn = childPart.FindModuleImplementing<ModuleDockingNode>();
			if (!mdn) {
				if (verbose)
					log("setup(): no ModuleDockingNode");
				return false;
			}
			Vector3 newChildAxis = Vector3.forward.Td(mdn.T(), mdn.part.T()).STd(mdn.part, newChildPart);
			if (verbose)
				log("AXIS " + newChildAxis.magnitude.ToString("F2") + " " + newChildAxis.ToString("F2"));
			newParentOffset = Vector3.Project(newParentOffset, newChildAxis);
			if (verbose)
				log("OFFS " + newParentOffset.magnitude.ToString("F2") + " " + newParentOffset.ToString("F2"));

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

		public IEnumerator doWeld()
		{
			log("WELDING!");
			ConfigurableJointManager[] cjm = new ConfigurableJointManager[joint.joints.Count];
			for (int i = 0; i < cjm.Length; i++)
				cjm[i].setup(joint.joints[i]);
			float T = 4f;
			float t = 0f;

			if (newChildPart.forcePhysics()) {
				log("forcePhysics " + newChildPart);
				for (int i = 0; i < 10; i++)
					yield return new WaitForFixedUpdate();
			}

			if (newParentPart.forcePhysics()) {
				log("forcePhysics " + newParentPart);
				for (int i = 0; i < 10; i++)
					yield return new WaitForFixedUpdate();
			}

			Vector3 ofs = -newParentOffset.STd(newChildPart, childPart);
			while (t < T) {
				t += Time.fixedDeltaTime;
				float p = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * t / T);
				log("t = " + t + ", p = " + p);
				for (int i = 0; i < cjm.Length; i++) {
					Vector3 pos = p * ofs.Td(childPart.T(), joint.joints[i].T());
					cjm[i].setPosition(pos);
				}
				yield return new WaitForFixedUpdate();
			}
			for (int i = 0; i < cjm.Length; i++)
				cjm[i].setPosition(Vector3.zero);
			log("WELDED!");
		}

		protected static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log("Welder: " + msg1, msg2);
		}
	}
}
