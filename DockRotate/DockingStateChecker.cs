using System;
using System.Collections.Generic;

namespace DockRotate
{
	public static class DockingStateChecker
	{
		private class State
		{
			public static implicit operator bool(State s)
			{
				return s != null;
			}
		}

		private class NodeState: State
		{
			private string state;
			private bool hasJoint, isSameVessel;

			private NodeState(string state, bool hasJoint, bool isSameVessel)
			{
				this.state = state;
				this.hasJoint = hasJoint;
				this.isSameVessel = isSameVessel;
			}

			private static readonly NodeState[] allowedNodeStates = new[] {
				new NodeState("Ready", false, false),
				new NodeState("Acquire", false, false),
				new NodeState("Acquire (dockee)", false, false),
				new NodeState("Disengage", false, false),
				new NodeState("Disabled", false, false),
				new NodeState("Docked (docker)", true, false),
				new NodeState("Docked (dockee)", true, false),
				new NodeState("Docked (dockee)", true, true),
				new NodeState("Docked (same vessel)", true, true),
				new NodeState("PreAttached", true, false)
			};

			public static bool exists(string state) {
				for (int i = 0; i < allowedNodeStates.Length; i++)
					if (allowedNodeStates[i].state == state)
						return true;
				return false;
			}

			public static NodeState find(ModuleDockingNode node)
			{
				if (!node)
					return null;
				string nodestate = S(node);
				bool hasJoint = node.getDockingJoint(out bool isSameVessel, false);
				for (int i = 0; i < allowedNodeStates.Length; i++) {
					ref NodeState s = ref allowedNodeStates[i];
					if (s.state == nodestate && s.hasJoint == hasJoint && s.isSameVessel == isSameVessel)
						return s;
				}
				return null;
			}
		}

		private class JointState: State
		{
			private string hoststate, targetstate;
			private bool isSameVessel;
			private Action<ModuleDockingNode, ModuleDockingNode> fixer;

			private JointState(string hoststate, string targetstate, bool isSameVessel,
				Action<ModuleDockingNode, ModuleDockingNode> fixer = null)
			{
				this.hoststate = hoststate;
				this.targetstate = targetstate;
				this.isSameVessel = isSameVessel;
				this.fixer = fixer;
			}

			private static readonly JointState[] allowedJointStates = new[] {
				new JointState("PreAttached", "PreAttached", false),
				new JointState("Docked (docker)", "Docked (dockee)", false),
				new JointState("Docked (dockee)", "Docked (docker)", false),
				new JointState("Docked (same vessel)", "Docked (dockee)", true),

				new JointState("Docked (dockee)", "Docked (dockee)", false,
					(host, target) => host.setState("Docked (docker)"))
			};

			public static JointState find(ModuleDockingNode host, ModuleDockingNode target, bool isSameVessel)
			{
				string hoststate = S(host);
				string targetstate = S(target);
				int l = JointState.allowedJointStates.GetLength(0);
				for (int i = 0; i < l; i++) {
					JointState s = JointState.allowedJointStates[i];
					if (s.hoststate == hoststate && s.targetstate == targetstate && s.isSameVessel == isSameVessel)
						return s;
				}
				return null;
			}

			public bool fixable()
			{
				return fixer != null;
			}

			public JointState fix(ModuleDockingNode host, ModuleDockingNode target)
			{
				if (fixer == null)
					return null;
				log("FIXING\n\t" + host.info() + " ->\n\t" + target.info());
				host.DebugFSMState = target.DebugFSMState = true;
				fixer(host, target);
				JointState ret = find(host, target, isSameVessel);
				if (ret.fixable())
					ret = null;
				return ret;
			}
		};

		public static bool isBadNode(this ModuleDockingNode node, bool verbose)
		{
			if (!node)
				return false;

			bool foundError = false;
			List<string> msg = new List<string>();
			msg.Add(node.info());

			PartJoint j = node.getDockingJoint(out bool dsv, verbose);

			string label = QS(node)
				+ (j ? ".hasJoint" : "")
				+ (dsv ? ".isSameVessel" : ".isTree");

			if (!NodeState.find(node))
				msg.Add("unallowed node state " + label);

			// a null vesselInfo may cause NRE later
			if (j && j.Host == node.part && node.vesselInfo == null
				&& S(node) != "PreAttached" && S(node) != "Docked (same vessel)")
				msg.Add("null vesselInfo");

			if (j)
				checkDockingJoint(msg, node, j, dsv);

			if (msg.Count > 1) {
				foundError = true;
			} else if (verbose) {
				msg.Add("is ok");
			}

			if (msg.Count > 1)
				log(String.Join(",\n\t", msg.ToArray()));

			return foundError;
		}

		private static void checkDockingJoint(List<string> msg, ModuleDockingNode node, PartJoint joint, bool isSameVessel)
		{
			if (!joint)
				return;

			bool valid = true;
			if (!joint.Host) {
				msg.Add("null host");
				valid = false;
			}
			if (!joint.Target) {
				msg.Add("null target");
				valid = false;
			}
			if (!valid)
				return;

			ModuleDockingNode other = node.getDockedNode(false);
			if (!other) {
				msg.Add("no other");
				return;
			}

			ModuleDockingNode host, target;
			if (node.part == joint.Host && other.part == joint.Target) {
				host = node;
				target = other;
			} else if (node.part == joint.Target && other.part == joint.Host) {
				host = other;
				target = node;
			} else {
				msg.Add("unrelated joint " + joint.info());
				return;
			}

			ModuleDockingNode treeChild =
				host.part.parent == target.part ? host :
				target.part.parent == host.part ? target :
				null;
			if (treeChild && isSameVessel)
				msg.Add("should use tree joint " + treeChild.part.attachJoint.info());

			string label = QS(host) + ">" + QS(target)
				+ (isSameVessel ? ".isSameVessel" : ".isTree");

			JointState s = JointState.find(host, target, isSameVessel);
			if (s && s.fixable())
				s = s.fix(host, target);

			if (!s)
				msg.Add("unallowed couple state " + label);
		}

		private static string QS(ModuleDockingNode node)
		{
			return "\"" + S(node) + "\"";
		}

		private static string S(ModuleDockingNode node)
		{
			return node.fsm != null ? node.fsm.currentStateName : node.state;
		}

		private static void setState(this ModuleDockingNode node, string state)
		{
			if (!NodeState.exists(state)) {
				log("setState(\"" + state + "\") not allowed");
				return;
			}
			if (!node || node.fsm == null)
				return;
			node.DebugFSMState = true;
			if (node.fsm != null)
				node.fsm.StartFSM(state);
		}

		private static string info(this ModuleDockingNode node)
		{
			if (!node)
				return "MDN:null-node";
			if (!node.part)
				return "MDN:null-part";
			string ret = "MDN@" + node.part.flightID
				+ "_" + node.part.bareName()
				+ "<" + (node.part.parent ? node.part.parent.flightID : 0)
				+ ">" + node.dockedPartUId
				+ ":" + QS(node);
			if (node.sameVesselDockJoint)
				ret += ":svdj=" + node.sameVesselDockJoint.GetInstanceID();
			PartJoint dj = node.getDockingJoint(out bool dsv, false);
			ret += ":dj=" + dj.info() + (dsv ? ":dsv" : "");
			return ret;
		}

		private static string info(this PartJoint j)
		{
			string ret = "PJ" + "[";
			if (j) {
				ret += j.GetInstanceID();
				ret += ":";
				ret += (j.Host ? j.Host.flightID : 0);
				ret += new string('>', j.joints.Count);
				ret += (j.Target ? j.Target.flightID : 0);
			} else {
				ret += "0";
			}
			ret += "]";
			return ret;
		}

		public static bool log(string msg)
		{
			return Extensions.log("[" + nameof(DockingStateChecker) + "] " + msg);
		}
	}
}
