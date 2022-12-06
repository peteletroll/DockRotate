using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class JointWelder
	{
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
			string sep = new string('-', 60);
			if (verbose)
				log(sep);
			this.valid = setup(joint, verbose);
			if (verbose)
				log("WELDABLE " + this.valid);
			if (verbose)
				log(sep);
		}

		private bool setup(PartJoint joint, bool verbose = false)
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

			AttachNode childNode = childPart.FindAttachNodeByPart(newChildPart);
			if (childNode == null)
				childNode = childPart.srfAttachNode;
			AttachNode parentNode = parentPart.FindAttachNodeByPart(newParentPart);
			if (parentNode == null)
				parentNode = parentPart.srfAttachNode;

			if (verbose) {
				log("CNODE " + childNode.desc());
				log("PNODE " + parentNode.desc());
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

		private void dumpNodes()
		{
			for (Part p = newChildPart; p && p != newParentPart.parent; p = p.parent)
				dumpNodes(p);
		}

		private void dumpNodes(Part p)
		{
			List<AttachNode> nodes = p.allAttachNodes();

			string desc = p.desc() + " nodes:";

			for (int i = 0; i < nodes.Count; i++) {
				AttachNode n = nodes[i];
				desc += "\n\t[" + i + "] \"" + n.id + "\" -> " + n.attachedPart.desc();
				AttachNode c = n.FindOpposingNode();
				if (c != null)
					desc += ", " + c.desc();
			}
			log(desc);
		}

		private void dumpJoints()
		{
			int i = 0;
			for (Part p = newChildPart; p && p != newParentPart.parent; p = p.parent)
				log("ATTACH[" + ++i + "] " + p.physicalSignificance + " " + p.PhysicsSignificance + " " + p.attachJoint.desc());
		}

		public IEnumerator doWeld(KerbalEVA EVA)
		{
			log("WELDING!");

			dumpNodes();
			dumpJoints();

			if (EVA) {
				EVA.DebugFSMState = true;
				EVA.Weld(childPart);
				yield return new WaitForSeconds(2f);
			}

			PartJoint joint = childPart.attachJoint;
			ConfigurableJointManager[] cjm = new ConfigurableJointManager[joint.joints.Count];
			for (int i = 0; i < cjm.Length; i++)
				cjm[i].setup(joint.joints[i]);

			childDR.forceUnlocked = parentDR.forceUnlocked = true;
			childPart.vessel.KJRNextCycleAllAutoStrut();
			childPart.releaseCrossAutoStruts(true);
			parentPart.releaseCrossAutoStruts(true);
			VesselMotionManager.get(childPart.vessel).changeCount(1);

			Vector3 ofs = -newParentOffset.STd(newChildPart, childPart);
			float T = 4f;
			float t = 0f;

			while (t < T) {
				t += Time.fixedDeltaTime;
				float p = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t / T);
				// log("t = " + t + ", p = " + p);
				for (int i = 0; i < cjm.Length; i++) {
					Vector3 pos = p * ofs.Td(childPart.T(), joint.joints[i].T());
					cjm[i].setPosition(pos);
				}
				yield return new WaitForFixedUpdate();
			}

			for (int i = 0; i < cjm.Length; i++) {
				Vector3 pos = ofs.Td(childPart.T(), joint.joints[i].T());
				cjm[i].setPosition(pos);
			}

			staticizeOrgInfo();
			staticizeTree();
			staticizeNodes();
			staticizeJoints();

			childDR.forceUnlocked = parentDR.forceUnlocked = false;

			VesselMotionManager.get(newChildPart.vessel).changeCount(-1);

			dumpNodes();
			dumpJoints();

			if (EVA)
				yield return new WaitForSeconds(2f);
			destroy(childPart);
			destroy(parentPart);

			log("WELDED!");
		}

		private void staticizeOrgInfo()
		{
			Vector3 offset = newParentOffset.STd(newChildPart, newChildPart.vessel.rootPart);
			propagateOffset(newChildPart, offset);
		}

		private void staticizeTree()
		{
			log("staticizeTree()");

			newChildPart.parent = newParentPart;

			newParentPart.children.Clear();
			newParentPart.children.Add(parentPart);
			newParentPart.children.AddRange(childPart.children);

			childPart.children.Clear();
		}

		private void staticizeNodes()
		{
			log("staticizeNodes()");
			List<AttachNode> nodes = new List<AttachNode>();
			nodes.AddRange(newChildPart.attachNodes);
			nodes.Add(newChildPart.srfAttachNode);
			nodes.AddRange(newParentPart.attachNodes);
			nodes.Add(newParentPart.srfAttachNode);
			for (int i = 0; i < nodes.Count; i++) {
				AttachNode n = nodes[i];
				if (n == null)
					continue;
				Part r = (n.attachedPart == childPart) ? newParentPart :
					(n.attachedPart == parentPart) ? newChildPart :
					null;
				if (r == null || r == n.owner)
					continue;
				string oldDesc = n.desc();
				n.attachedPart = r;
				n.attachedPartId = r.flightID;
				log("RENODE " + oldDesc + " -> " + n.desc());
			}
		}

		private void staticizeJoints()
		{
			log("staticizeJoints()");
			List<Part> q = new List<Part>();
			q.Add(newChildPart);
			for (int i = 0; i < q.Count; i++) {
				Part p = q[i];
				if (!p)
					continue;
				string ajd = p.attachJoint.desc();
				if (p.attachJoint.Target == childPart) {
					p.CreateAttachJoint(AttachModes.STACK);
					log("REATTACH " + ajd + " -> " + p.attachJoint.desc());
				}
				if (p.physicalSignificance != Part.PhysicalSignificance.FULL)
					q.AddRange(p.children);
			}
		}

		private static void propagateOffset(Part part, Vector3 offset)
		{
			if (!part)
				return;
			part.orgPos += offset;
			for (int i = 0; i < part.children.Count; i++)
				propagateOffset(part.children[i], offset);
		}

		private void destroy(Part part)
		{
			if (part)
				part.Die();
		}

		protected static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(nameof(JointWelder) + ": " + msg1, msg2);
		}
	}
}
