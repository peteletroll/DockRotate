using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;

namespace DockRotate
{
	public static class DockingStateChecker
	{
		private struct NodeState
		{
			public string state;
			public bool hasJoint, isSameVessel;

			public NodeState(string state, bool hasJoint, bool isSameVessel)
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

			public static bool allowed(ModuleDockingNode node, bool verbose = false)
			{
				if (!node)
					return false;
				string name = node.state;
				bool hasJoint = node.getDockingJoint(out bool isSameVessel, verbose);
				for (int i = 0; i < allowedNodeStates.Length; i++) {
					ref NodeState s = ref allowedNodeStates[i];
					if (s.state == name && s.hasJoint == hasJoint && s.isSameVessel == isSameVessel)
						return true;
				}
				return false;
			}
		}

		public static void checkDockingStates(this Vessel v, bool verbose)
		{
			log("analyzing incoherent states in " + v.GetName());
			List<ModuleDockingNode> dn = v.FindPartModulesImplementing<ModuleDockingNode>();
			dn = new List<ModuleDockingNode>(dn);
			dn.Sort((a, b) => (int)a.part.flightID - (int)b.part.flightID);
			bool foundError = false;
			for (int i = 0; i < dn.Count; i++)
				if (dn[i].isBadNode(verbose))
					foundError = true;
			if (foundError)
				ScreenMessages.PostScreenMessage(
					Localizer.Format("#DCKROT_bad_states"),
					5f, ScreenMessageStyle.LOWER_CENTER, Color.red);
		}

		public static bool isBadNode(this ModuleDockingNode node, bool verbose)
		{
			if (!node)
				return false;

			bool foundError = false;
			List<string> msg = new List<string>();
			msg.Add(node.stateInfo());

			PartJoint j = node.getDockingJoint(out bool dsv, false);

			string label = "\"" + node.state + "\""
				+ (j ? ".hasJoint" : "")
				+ (dsv ? ".isSameVessel" : ".isTree");

			if (!NodeState.allowed(node, verbose))
				msg.Add("unallowed node state " + label);

			if (j)
				checkDockingJoint(msg, node, j, dsv);

			if (j && j.Host == node.part && node.vesselInfo == null
					&& node.state != "PreAttached" && node.state != "Docked (same vessel)")
				msg.Add("null vesselInfo");

			if (msg.Count > 1) {
				foundError = true;
				node.part.SetHighlightColor(Color.red);
				node.part.SetHighlightType(Part.HighlightType.AlwaysOn);
				log(String.Join(",\n\t", msg.ToArray()));
			} else {
				node.part.SetHighlightDefault();
			}

			if (verbose && msg.Count <= 1) {
				if (verbose)
					msg.Add("is ok");
			}
			return foundError;
		}

		private struct JointState
		{
			public string hoststate, targetstate;
			public bool isSameVessel;

			public JointState(string hoststate, string targetstate, bool isSameVessel)
			{
				this.hoststate = hoststate;
				this.targetstate = targetstate;
				this.isSameVessel = isSameVessel;
			}

			public static readonly JointState[] allowedJointStates = new[] {
				new JointState("PreAttached", "PreAttached", false),
				new JointState("Docked (docker)", "Docked (dockee)", false),
				new JointState("Docked (dockee)", "Docked (docker)", false),
				new JointState("Docked (same vessel)", "Docked (dockee)", true)
			};
		};

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
			ModuleDockingNode other = node.getDockedNode();
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
			int l = JointState.allowedJointStates.GetLength(0);
			bool found = false;
			for (int i = 0; i < l; i++) {
				ref JointState s = ref JointState.allowedJointStates[i];
				if (s.hoststate == host.state && s.targetstate == target.state && s.isSameVessel == isSameVessel) {
					found = true;
					break;
				}
			}
			if (!found) {
				msg.Add("unallowed couple state \"" + host.state + "\">\"" + target.state + "\""
					+ (isSameVessel ? ".isSameVessel" : ".isTree"));
				return;
			}

			if (isSameVessel) {
				ModuleDockingNode child =
					host.part.parent == target.part ? host :
					target.part.parent == host.part ? target :
					null;
				if (child)
					msg.Add("should use tree joint " + child.part.attachJoint.info());
			}
		}

		public static string stateInfo(this ModuleDockingNode node)
		{
			if (!node)
				return "null-node";
			if (!node.part)
				return "null-part";
			string ret = "MDN@" + node.part.flightID
				+ "_" + node.part.bareName()
				+ "<" + (node.part.parent ? node.part.parent.flightID : 0)
				+ ":\"" + node.state + "\"";
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

		public static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}
