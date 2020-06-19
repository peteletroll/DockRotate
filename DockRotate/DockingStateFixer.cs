using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public static class DockingStateFixer
	{
		private class State
		{
			public string name;
			public bool isDocked;
			public bool isSameVessel;

			private readonly static State[] dockingStates = new State[] {
				new State("Ready", false),
				new State("Acquire", false),
				new State("Acquire (dockee)", false),
				new State("Disengage", false),
				new State("Disabled", false),
				new State("Docked (docker)", true),
				new State("Docked (dockee)", true),
				new State("Docked (same vessel)", true, true),
				new State("PreAttached", true)
				/*
				* Disabled
				* PreAttached
				* Docked (docker/same vessel/dockee) - (docker) and (same vessel) are coupled with (dockee)
				* Ready
				* Disengage
				* Acquire
				* Acquire (dockee)
				*/
			};

			public State(string name, bool isDocked, bool isSameVessel = false)
			{
				this.name = name;
				this.isDocked = isDocked;
				this.isSameVessel = isSameVessel;
			}

			public static State get(ModuleDockingNode node)
			{
				return get(node.state);
			}

			public static State get(string name) {
				for (int i = 0; i < dockingStates.Length; i++)
					if (dockingStates[i].name == name)
						return dockingStates[i];
				log("*** WARNING *** unknown state \"" + name + "\"");
				return null;
			}

			public static implicit operator bool(State s)
			{
				return s != null;
			}
		}

		public static void checkDockingStates(this Vessel v)
		{
			List<ModuleDockingNode> dn = v.FindPartModulesImplementing<ModuleDockingNode>();
			dn = new List<ModuleDockingNode>(dn);
			dn.Sort((a, b) => (int)a.part.flightID - (int)b.part.flightID);
			for (int i = 0; i < dn.Count; i++)
				checkDockingNode(dn[i]);
		}

		public static void checkDockingNode(ModuleDockingNode node)
		{
			if (!node)
				return;
			List<string> msg = new List<string>();
			msg.Add(node.stateInfo());
			State s = State.get(node.state);
			if (s) {
				PartJoint j = node.getDockingJoint(out bool dsv);
				if (j) {
					if (!s.isDocked)
						msg.Add("should not be docked");
					checkDockingJoint(msg, node, j, dsv);
				} else {
					if (s.isDocked)
						msg.Add("should be docked");
				}
			} else {
				msg.Add("unknown state");
			}
			if (msg.Count <= 1) {
				msg.Add("is ok");
			} else {
				node.part.Highlight(Color.red);
			}
			log(String.Join(", ", msg.ToArray()));
		}

		private static readonly String[,] jointState = {
			{ "PreAttached", "PreAttached", "" },
			{ "Docked (docker)", "Docked (dockee)", "" },
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
			ModuleDockingNode other = node.otherNode;
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

		public static void fixDockingState(this Vessel vessel, uint flightID)
		{
			if (!vessel || vessel.parts == null) {
				log("no vessel");
				return;
			}
			bool found = false;
			List<Part> parts = vessel.Parts;
			for (int i = 0; i < parts.Count; i++) {
				if (parts[i] && parts[i].flightID == flightID) {
					found = true;
					parts[i].fixDockingState();
				}
			}
			if (!found)
				log("part not found");
		}

		public static void fixDockingState(this Part part) {
			if (!part)
				return;
			List<ModuleDockingNode> dn = part.FindModulesImplementing<ModuleDockingNode>();
			if (dn == null || dn.Count <= 0) {
				log("part " + part.flightID + " has no docking nodes");
				return;
			}
			for (int i = 0; i < dn.Count; i++)
				dn[i].fixDockingState();
		}

		public static void fixDockingState(this ModuleDockingNode node)
		{
			if (!node || !node.part)
				return;
			string newState = node.shouldChangeStateTo();
			if (newState != null) {
				log(node.stateInfo() + ", fixing to \"" + newState + "\"");
				node.state = newState;
			} else {
				log(node.stateInfo() + ", needs no fixing");
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
			State s = State.get(node.state);
			if (!s)
				ret += ":unknown-state";
			if (node.sameVesselDockJoint)
				ret += ":svdj=" + node.sameVesselDockJoint.GetInstanceID();
			PartJoint dj = node.getDockingJoint(out bool dsv);
			if (!dj) {
				ret += ":null-joint";
				if (s && s.isDocked)
					ret += ":should-have-joint";
			} else {
				if (dsv)
					ret += ":sv";
				ret += ":" + dj.info();
			}
			return ret;
		}

		public static PartJoint getDockingJoint(this ModuleDockingNode node, out bool sameVesselDock)
		{
			sameVesselDock = false;
			if (!node)
				return null;
			ModuleDockingNode other = node.otherNode;
			if (!other)
				return null;
			if (node.sameVesselDockJoint) {
				sameVesselDock = true;
				return node.sameVesselDockJoint;
			}
			if (node.otherNode.sameVesselDockJoint) {
				sameVesselDock = true;
				return node.otherNode.sameVesselDockJoint;
			}
			if (node.otherNode.part == node.part.parent)
				return node.part.attachJoint;
			for (int i = 0; i < node.part.children.Count; i++) {
				Part child = node.part.children[i];
				if (child == other.part)
					return child.attachJoint;
			}
			return null;
		}

		public static string shouldChangeStateTo(this ModuleDockingNode node)
		{
			if (!node)
				return null;
			string ret = null;
			PartJoint dockingJoint = node.getDockingJoint(out bool sameVesselDock);
			if (!dockingJoint || !dockingJoint.Host || !dockingJoint.Target)
				return null;
			if (sameVesselDock) {
				if (node.part == dockingJoint.Host) {
					ModuleDockingNode other = node.otherNode;
					if (other && other.part == dockingJoint.Target) {
						PartJoint targetJoint = other.getDockingJoint(out bool targetSameVesselDock);
						State targetState = State.get(other.state);
						if (targetSameVesselDock && targetState && !targetState.isSameVessel)
							ret = "Docked (docker)";
					}
				} else if (node.part == dockingJoint.Target) {
				}
			} else {
				if (node.part == dockingJoint.Host) {
					ret = "Docked (docker)";
				} else if (node.part == dockingJoint.Target) {
					ret = "Docked (dockee)";
				}
			}
			if (ret != null && !State.get(ret)) {
				log("*** WARNING *** state \"" + ret + "\" doesn't exist");
				ret = null;
			}
			if (ret != null && ret == node.state)
				ret = null;
			return ret;
		}

		private static string info(this PartJoint j)
		{
			string ret = "J" + "[";
			if (j) {
				ret += j.GetInstanceID();
				ret += ":";
				ret += (j.Host ? j.Host.flightID : 0);
				ret += new string('>', j.joints.Count);
				ret += (j.Target ? j.Target.flightID : 0);
			} else {
				ret += 0;
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
