using System;
using System.Collections.Generic;

namespace DockRotate
{
	public static class DockingStateChecker
	{
		private static DockingStateTable dockingStateTable = new DockingStateTable();

		public class DockingStateTable
		{
			private const string configName = nameof(DockingStateChecker);

			private static string configFile() {
				string assembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
				string directory = System.IO.Path.GetDirectoryName(assembly);
				return System.IO.Path.Combine(directory, "PluginData", configName + ".cfg");
			}

			private List<NodeState> NodeStates = new List<NodeState>();
			private List<JointState> JointStates = new List<JointState>();

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

			private static readonly JointState[] allowedJointStates = new[] {
				new JointState("PreAttached", "PreAttached", false),
				new JointState("Docked (docker)", "Docked (dockee)", false),
				new JointState("Docked (dockee)", "Docked (docker)", false),
				new JointState("Docked (same vessel)", "Docked (dockee)", true),

				new JointState("Docked (dockee)", "Docked (dockee)", false,
					"Docked (docker)")
			};

			public DockingStateTable()
			{
				NodeStates = new List<NodeState>();
				NodeStates.AddRange(allowedNodeStates);

				JointStates = new List<JointState>();
				JointStates.AddRange(allowedJointStates);

				log("CONFIG\n" + configNode());
				log("FILE " + configFile());
				ConfigNode cn = ConfigNode.Load(configFile());
				log("LOADED\n" + cn);
			}

			public ConfigNode configNode()
			{
				ConfigNode ret = ConfigNode.CreateConfigFromObject(this);
				ret.name = configName;

				ConfigNode ns = new ConfigNode(nameof(NodeStates));
				for (int i = 0; i < NodeStates.Count; i++)
					ns.AddNode(NodeStates[i].configNode());
				ret.AddNode(ns);

				ConfigNode js = new ConfigNode(nameof(JointStates));
				for (int i = 0; i < JointStates.Count; i++)
					js.AddNode(JointStates[i].configNode());
				ret.AddNode(js);

				return ret;
			}

			public bool exists(string state)
			{
				for (int i = 0; i < NodeStates.Count; i++)
					if (NodeStates[i].state == state)
						return true;
				return false;
			}

			public NodeState find(ModuleDockingNode node)
			{
				if (!node)
					return null;
				string nodestate = S(node);
				bool hasJoint = node.getDockingJoint(out bool isSameVessel, false);
				for (int i = 0; i < NodeStates.Count; i++) {
					NodeState s = NodeStates[i];
					if (s.state == nodestate && s.hasJoint == hasJoint && s.isSameVessel == isSameVessel)
						return s;
				}
				return null;
			}

			public JointState find(ModuleDockingNode host, ModuleDockingNode target, bool isSameVessel)
			{
				string hoststate = S(host);
				string targetstate = S(target);
				for (int i = 0; i < JointStates.Count; i++) {
					JointState s = JointStates[i];
					if (s.hostState == hoststate && s.targetState == targetstate && s.isSameVessel == isSameVessel)
						return s;
				}
				return null;
			}
		}

		public abstract class State
		{
			public static implicit operator bool(State s)
			{
				return s != null;
			}

			public ConfigNode configNode()
			{
				ConfigNode ret = ConfigNode.CreateConfigFromObject(this);
				ret.name = this.GetType().Name;
				return ret;
			}
		}

		public class NodeState: State
		{
			[Persistent] public string state;
			[Persistent] public bool hasJoint;
			[Persistent] public bool isSameVessel;

			public NodeState(string state, bool hasJoint, bool isSameVessel)
			{
				this.state = state;
				this.hasJoint = hasJoint;
				this.isSameVessel = isSameVessel;
			}
		}

		public class JointState: State
		{
			[Persistent] public string hostState;
			[Persistent] public string targetState;
			[Persistent] public bool isSameVessel;
			[Persistent] public string hostFixTo;
			[Persistent] public string targetFixTo;

			public JointState(string hoststate, string targetstate, bool isSameVessel,
				string hostFixTo = "", string targetFixTo = "")
			{
				this.hostState = hoststate;
				this.targetState = targetstate;
				this.isSameVessel = isSameVessel;
				this.hostFixTo = hostFixTo;
				this.targetFixTo = targetFixTo;
			}

			public bool fixable()
			{
				return hostFixTo != "" || targetFixTo != "";
			}

			public JointState fix(ModuleDockingNode host, ModuleDockingNode target)
			{
				if (!fixable())
					return null;
				log("FIXING\n\t" + host.info() + " ->\n\t" + target.info());
				host.DebugFSMState = target.DebugFSMState = true;
				if (hostFixTo != "")
					host.setState(hostFixTo);
				if (targetFixTo != "")
					target.setState(targetFixTo);
				log("AFTER FIX\n\t" + host.info() + " ->\n\t" + target.info());
				JointState ret = dockingStateTable.find(host, target, isSameVessel);
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

			if (!dockingStateTable.find(node))
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

			JointState s = dockingStateTable.find(host, target, isSameVessel);
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
			if (!dockingStateTable.exists(state)) {
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
