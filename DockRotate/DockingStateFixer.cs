using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public static class DockingStateFixer
	{
		public static void checkDockingStates(this Vessel v)
		{
			List<ModuleDockingNode> dn = v.FindPartModulesImplementing<ModuleDockingNode>();
			for (int i = 0; i < dn.Count; i++) {
				string s = dn[i].shouldChangeStateTo();
				log(dn[i].stateInfo()
					+ ", "
					+ (s != null ? "should be \"" + s + "\"" : "is ok"));
			}
		}

		public static string stateInfo(this ModuleDockingNode node)
		{
			if (!node)
				return "null-node";
			if (!node.part)
				return "null-part";
			string ret = "MDN@" + node.part.bareName() + "_" + node.part.flightID
				+ ":\"" + node.state + "\"";
			PartJoint dj = node.getDockingJoint(out bool dsv);
			if (!dj) {
				ret += ":null-joint";
			} else if (!dj.Host ){
				ret += ":null-host";
			} else if (!dj.Target) {
				ret += ":null-target";
			} else {
				ret += ":" + dj.Host.flightID + ">" + dj.Target.flightID;
				if (dsv)
					ret += ":sv";
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
			PartJoint j = node.getDockingJoint(out bool sameVesselDock);
			if (!j) {
				return null;
			}
			if (sameVesselDock) {
				if (node.part == j.Host) {
				} else if (node.part == j.Target) {
				}
			} else {
				if (node.part == j.Host) {
				} else if (node.part == j.Target) {
				}
			}
			if (ret != null && ret == node.state)
				ret = null;
			return ret;
		}

		public static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}
