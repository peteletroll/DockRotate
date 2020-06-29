using System;
using System.Collections.Generic;

namespace DockRotate
{
	public class DockingStateChecker
	{
		private const string configName = nameof(DockingStateChecker);

		private static string configFile()
		{
			string assembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
			string directory = System.IO.Path.GetDirectoryName(assembly);
			return System.IO.Path.Combine(directory, "PluginData", configName + ".cfg");
		}

		public static DockingStateChecker load()
		{
			DockingStateChecker ret = null;
			try {
				log("LOADING " + configFile());
				ConfigNode cn = ConfigNode.Load(configFile());
				if (cn == null)
					throw new Exception("null ConfigNode");
				cn = cn.GetNode(configName);
				if (cn == null)
					throw new Exception("can't find " + configName);
				log("LOADED\n" + cn);
				ret = fromConfigNode(cn);
				log("GENERATED\n"
					+ ret.desc() + "\n"
					+ ret.toConfigNode());
			} catch (Exception e) {
				log("can't load: " + e.Message + "\n" + e.StackTrace);
				ret = builtin();
				ret.save();
			}
			log("LOADED " + ret.desc());
			return ret;
		}

		public bool save()
		{
			bool ret = false;
			try {
				string file = configFile();
				string directory = System.IO.Path.GetDirectoryName(file);
				if (!System.IO.Directory.Exists(directory)) {
					log("CREATING DIRECTORY " + directory);
					System.IO.Directory.CreateDirectory(directory);
				}
				ConfigNode cn = new ConfigNode("root");
				cn.AddNode(toConfigNode());
				log("SAVING " + file);
				cn.Save(file);
				ret = true;
			} catch (Exception e) {
				log("can't save: " + e.Message + "\n" + e.StackTrace);
			}
			return ret;
		}

		public static DockingStateChecker builtin()
		{
			DockingStateChecker ret = new DockingStateChecker();
			ret.nodeStates.AddRange(allowedNodeStates);
			ret.jointStates.AddRange(allowedJointStates);
			log("BUILTIN\n" + ret.toConfigNode());
			return ret;
		}

		public string desc()
		{
			string ret = nameof(DockingStateChecker) + ":";
			for (int i = 0; i < nodeStates.Count; i++)
				ret += "\n\t" + nodeStates[i].desc();
			for (int i = 0; i < jointStates.Count; i++)
				ret += "\n\t" + jointStates[i].desc();
			return ret;
		}

		private List<NodeState> nodeStates = new List<NodeState>();
		private List<JointState> jointStates = new List<JointState>();

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
			new NodeState("PreAttached", true, false),
			new NodeState("PreAttached", false, false)
		};

		private static readonly JointState[] allowedJointStates = new[] {
			new JointState("PreAttached", "PreAttached", false),
			new JointState("Docked (docker)", "Docked (dockee)", false),
			new JointState("Docked (dockee)", "Docked (docker)", false),
			new JointState("Docked (same vessel)", "Docked (dockee)", true),

			new JointState("Docked (dockee)", "Docked (dockee)", false,
				"Docked (docker)")
		};

		public ConfigNode toConfigNode()
		{
			ConfigNode ret = ConfigNode.CreateConfigFromObject(this);
			ret.name = configName;

			for (int i = 0; i < nodeStates.Count; i++)
				ret.AddNode(nodeStates[i].toConfigNode());

			for (int i = 0; i < jointStates.Count; i++)
				ret.AddNode(jointStates[i].toConfigNode());

			return ret;
		}

		public static DockingStateChecker fromConfigNode(ConfigNode cn)
		{
			DockingStateChecker ret = ConfigNode.CreateObjectFromConfig<DockingStateChecker>(cn);

			ConfigNode[] ns = cn.GetNodes(nameof(NodeState));
			if (ns != null)
				for (int i = 0; i < ns.Length; i++)
					ret.nodeStates.Add(NodeState.fromConfigNode(ns[i]));

			ConfigNode[] js = cn.GetNodes(nameof(JointState));
			if (js != null)
				for (int i = 0; i < js.Length; i++)
					ret.jointStates.Add(JointState.fromConfigNode(js[i]));

			return ret;
		}

		public bool exists(string state)
		{
			for (int i = 0; i < nodeStates.Count; i++)
				if (nodeStates[i].state == state)
					return true;
			return false;
		}

		public NodeState find(ModuleDockingNode node)
		{
			if (!node)
				return null;
			NodeState ret = null;
			string nodeState = S(node);
			bool hasJoint = node.getDockingJoint(out bool isSameVessel, false);
			for (int i = 0; i < nodeStates.Count; i++) {
				NodeState s = nodeStates[i];
				if (s.state == nodeState && s.hasJoint == hasJoint && s.isSameVessel == isSameVessel) {
					ret = s;
					break;
				}
			}
			if (ret)
				ret.checker = this;
			return ret;
		}

		public JointState find(ModuleDockingNode host, ModuleDockingNode target, bool isSameVessel)
		{
			JointState ret = null;
			string hostState = S(host);
			string targetState = S(target);
			for (int i = 0; i < jointStates.Count; i++) {
				JointState s = jointStates[i];
				if (s.hostState == hostState && s.targetState == targetState && s.isSameVessel == isSameVessel) {
					ret = s;
					break;
				}
			}
			if (ret)
				ret.checker = this;
			return ret;
		}

		public class NodeState
		{
			public DockingStateChecker checker = null;

			[Persistent] public string state = "";
			[Persistent] public bool hasJoint = false;
			[Persistent] public bool isSameVessel = false;

			public static implicit operator bool(NodeState s)
			{
				return s != null;
			}

			public NodeState() { }

			public NodeState(string state, bool hasJoint, bool isSameVessel)
			{
				this.state = state;
				this.hasJoint = hasJoint;
				this.isSameVessel = isSameVessel;
			}

			public string desc()
			{
				return nameof(NodeState)
					+ ":" + state
					+ ":" + hasJoint
					+ ":" + isSameVessel;
			}

			public static NodeState fromConfigNode(ConfigNode cn)
			{
				NodeState ret = ConfigNode.CreateObjectFromConfig<NodeState>(cn);
				return ret;
			}

			public ConfigNode toConfigNode()
			{
				ConfigNode ret = ConfigNode.CreateConfigFromObject(this);
				ret.name = this.GetType().Name;
				return ret;
			}
		}

		public class JointState
		{
			public DockingStateChecker checker = null;

			[Persistent] public string hostState = "";
			[Persistent] public string targetState = "";
			[Persistent] public bool isSameVessel = false;
			[Persistent] public string hostFixTo = "";
			[Persistent] public string targetFixTo = "";

			public static implicit operator bool(JointState s)
			{
				return s != null;
			}

			public JointState() { }

			public JointState(string hoststate, string targetstate, bool isSameVessel,
				string hostFixTo = "", string targetFixTo = "")
			{
				this.hostState = hoststate;
				this.targetState = targetstate;
				this.isSameVessel = isSameVessel;
				this.hostFixTo = hostFixTo;
				this.targetFixTo = targetFixTo;
			}

			public string desc()
			{
				return nameof(JointState)
					+ ":" + hostState
					+ ":" + targetState
					+ ":" + isSameVessel
					+ ":" + hostFixTo
					+ ":" + targetFixTo;
			}

			public static JointState fromConfigNode(ConfigNode cn)
			{
				JointState ret = ConfigNode.CreateObjectFromConfig<JointState>(cn);
				return ret;
			}

			public ConfigNode toConfigNode()
			{
				ConfigNode ret = ConfigNode.CreateConfigFromObject(this);
				ret.name = this.GetType().Name;
				if (!fixable())
					ret.RemoveValues("hostFixTo", "targetFixTo");
				return ret;
			}

			public bool fixable()
			{
				return hostFixTo != "" || targetFixTo != "";
			}

			public JointState fix(ModuleDockingNode host, ModuleDockingNode target)
			{
				if (!fixable())
					return null;
				log("FIXING\n\t" + info(host) + " ->\n\t" + info(target));
				host.DebugFSMState = target.DebugFSMState = true;
				if (hostFixTo != "")
					checker.setState(host, hostFixTo);
				if (targetFixTo != "")
					checker.setState(target, targetFixTo);
				log("AFTER FIX\n\t" + info(host) + " ->\n\t" + info(target));
				JointState ret = checker.find(host, target, isSameVessel);
				if (ret.fixable())
					ret = null;
				return ret;
			}
		};

		public bool isBadNode(ModuleDockingNode node, bool verbose)
		{
			if (!node)
				return false;

			bool foundError = false;
			List<string> msg = new List<string>();
			msg.Add(info(node));

			PartJoint j = node.getDockingJoint(out bool dsv, verbose);

			string label = QS(node)
				+ (j ? ".hasJoint" : "")
				+ (dsv ? ".isSameVessel" : ".isTree");

			if (!find(node))
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

		private void checkDockingJoint(List<string> msg, ModuleDockingNode node, PartJoint joint, bool isSameVessel)
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
				msg.Add("unrelated joint " + info(joint));
				return;
			}

			ModuleDockingNode treeChild =
				host.part.parent == target.part ? host :
				target.part.parent == host.part ? target :
				null;
			if (treeChild && isSameVessel)
				msg.Add("should use tree joint " + info(treeChild.part.attachJoint));

			string label = QS(host) + ">" + QS(target)
				+ (isSameVessel ? ".isSameVessel" : ".isTree");

			JointState s = find(host, target, isSameVessel);
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

		private void setState(ModuleDockingNode node, string state)
		{
			if (!exists(state)) {
				log("setState(\"" + state + "\") not allowed");
				return;
			}
			if (!node || node.fsm == null)
				return;
			node.DebugFSMState = true;
			if (node.fsm != null)
				node.fsm.StartFSM(state);
		}

		private static string info(ModuleDockingNode node)
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
			ret += ":dj=" + info(dj) + (dsv ? ":dsv" : "");
			return ret;
		}

		private static string info(PartJoint j)
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
