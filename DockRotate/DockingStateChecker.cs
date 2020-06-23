using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public static class DockingStateChecker
	{
		private struct NodeState
		{
			public string name;
			public bool hasJoint, isSameVessel;

			public NodeState(string name, bool hasJoint = false, bool isSameVessel = false)
			{
				this.name = name;
				this.hasJoint = hasJoint;
				this.isSameVessel = isSameVessel;
			}

			private static readonly NodeState[] allowedNodeStates = new[] {
					new NodeState("Ready"),
					new NodeState("Acquire"),
					new NodeState("Acquire (dockee)"),
					new NodeState("Disengage"),
					new NodeState("Disabled"),
					new NodeState("Docked (docker)", true),
					new NodeState("Docked (dockee)", true),
					new NodeState("Docked (same vessel)", true),
					new NodeState("PreAttached", true),
				};

			public static bool allowed(ModuleDockingNode node, bool verbose = false)
			{
				if (!node)
					return true;
				string name = node.state;
				bool hasJoint = node.getDockingJoint(out bool isSameVessel, verbose);
				for (int i = 0; i < allowedNodeStates.Length; i++) {
					ref NodeState s = ref allowedNodeStates[i];
					if (s.name == name && s.hasJoint == hasJoint && s.isSameVessel == isSameVessel)
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
			dn.Sort((a, b) => (int) a.part.flightID - (int) b.part.flightID);
			for (int i = 0; i < dn.Count; i++)
				dn[i].checkDockingNode(verbose);
		}

		public static void checkDockingNode(this ModuleDockingNode node, bool verbose)
		{
			if (!node)
				return;
			List<string> msg = new List<string>();
			msg.Add(node.stateInfo());

			PartJoint j = node.getDockingJoint(out bool dsv, verbose);

			string label = "\"" + node.state + "\""
				+ (j ? ".hasJoint" : "")
				+ (dsv ? ".isSameVessel" : "");

			if (!NodeState.allowed(node, verbose))
				msg.Add("unallowed state " + label);

			if (j)
				checkDockingJoint(msg, node, j, dsv);

			if (j && j.Host == node.part) {
				if (node.vesselInfo == null)
					msg.Add("null vessel info");
			}

			if (msg.Count <= 1) {
				if (verbose)
					msg.Add("is ok");
				node.part.SetHighlightDefault();
			} else {
				node.part.SetHighlightColor(Color.red);
				node.part.SetHighlightType(Part.HighlightType.AlwaysOn);
			}
			if (msg.Count > 1)
				log(String.Join(",\n    *** ", msg.ToArray()));
		}

		private static readonly String[,] jointState = {
			{ "PreAttached", "PreAttached", "" },
			{ "Docked (docker)", "Docked (dockee)", "" },
			{ "Docked (dockee)", "Docked (docker)", "" },
			{ "Docked (same vessel)", "Docked (dockee)", "S" }
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
				msg.Add("joint incoherency");
				return;
			}
			int l = jointState.GetLength(0);
			bool found = false;
			for (int i = 0; i < l; i++) {
				if (host.state == jointState[i, 0] && target.state == jointState[i, 1]) {
					found = true;
					string flags = jointState[i, 2];
					bool dsv = flags.IndexOf('S') >= 0;
					if (dsv) {
						if (!isSameVessel)
							msg.Add("should be sv");
					} else {
						if (isSameVessel)
							msg.Add("shouldn't be sv");
					}
					if (isSameVessel) {
						ModuleDockingNode treeChild =
							host.part.parent == target.part ? host :
							target.part.parent == host.part ? target :
							null;
						if (treeChild)
							msg.Add("should use tree joint " + treeChild.part.attachJoint.info());
					}
					break;
				}
			}
			if (!found)
				msg.Add("unknown couple state \"" + host.state + "\">\"" + target.state + "\"");
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
