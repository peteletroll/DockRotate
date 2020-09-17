using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class DockingStateChecker
	{
		private const string configName = nameof(DockingStateChecker);

		[Persistent] public bool enabledCheck = true;
		[Persistent] public bool enabledFix = true;
		[Persistent] public int checkDelay = 5;
		[Persistent] public float messageTimeout = 3f;
		[Persistent] public ScreenMessageStyle messageStyle = ScreenMessageStyle.UPPER_CENTER;
		[Persistent] public Color colorBad = Color.red;
		[Persistent] public Color colorFixed = Color.yellow;
		[Persistent] public float highlightTimeout = 5f;

		private List<NodeState> nodeStates = new List<NodeState>();
		private List<JointState> jointStates = new List<JointState>();

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
				log("loading " + configFile());
				ConfigNode cn = ConfigNode.Load(configFile());
				if (cn == null)
					throw new Exception("null ConfigNode");
				cn = cn.GetNode(configName);
				if (cn == null)
					throw new Exception("can't find " + configName);
				ret = fromConfigNode(cn);
				log("loaded\n" + ret.desc() + "\n");
			} catch (Exception e) {
				log("can't load: " + e.Message + "\n" + e.StackTrace);
				log("using builtin configuration");
				ret = builtin();
				if (!System.IO.File.Exists(configFile()))
					ret.save();
			}
			return ret;
		}

		private bool save()
		{
			bool ret = false;
			try {
				string file = configFile();
				string directory = System.IO.Path.GetDirectoryName(file);
				if (!System.IO.Directory.Exists(directory)) {
					log("creating directory " + directory);
					System.IO.Directory.CreateDirectory(directory);
				}
				ConfigNode cn = new ConfigNode("root");
				cn.AddNode(toConfigNode());
				log("saving " + file);
				cn.Save(file);
				ret = true;
			} catch (Exception e) {
				log("can't save: " + e.Message + "\n" + e.StackTrace);
			}
			return ret;
		}

		private static DockingStateChecker builtin()
		{
			DockingStateChecker ret = new DockingStateChecker();
			ret.nodeStates.AddRange(allowedNodeStates);
			ret.jointStates.AddRange(allowedJointStates);
			log("builtin\n" + ret.desc());
			return ret;
		}

		private string desc()
		{
			string ret = nameof(DockingStateChecker) + ":";
			for (int i = 0; i < nodeStates.Count; i++)
				ret += "\n\t" + nodeStates[i].desc();
			for (int i = 0; i < jointStates.Count; i++)
				ret += "\n\t" + jointStates[i].desc();
			return ret;
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
			new NodeState("PreAttached", true, false),
			new NodeState("PreAttached", false, false)
		};

		private static readonly JointState[] allowedJointStates = new[] {
			new JointState("PreAttached", "PreAttached", false),
			new JointState("Docked (docker)", "Docked (dockee)", false),
			new JointState("Docked (dockee)", "Docked (docker)", false),
			new JointState("Docked (same vessel)", "Docked (dockee)", true),

			new JointState("Docked (dockee)", "Docked (dockee)", false,
				"Docked (docker)", ""),
			new JointState("Docked (same vessel)", "Docked (dockee)", false,
				"Docked (docker)", ""),
			new JointState("Docked (dockee)", "Docked (same vessel)", false,
				"", "Docked (docker)"),
			new JointState("Disengage", "Disengage", false,
				"Docked (docker)", "Docked (dockee)"),
			new JointState("Docked (docker)", "Ready", false,
				"", "Docked (dockee)"),
		};

		private ConfigNode toConfigNode()
		{
			ConfigNode ret = ConfigNode.CreateConfigFromObject(this);
			ret.name = configName;
			ret.comment = "reloaded at every check, edits effective without restarting KSP";

			for (int i = 0; i < nodeStates.Count; i++)
				ret.AddNode(nodeStates[i].toConfigNode());

			for (int i = 0; i < jointStates.Count; i++)
				ret.AddNode(jointStates[i].toConfigNode());

			return ret;
		}

		private static DockingStateChecker fromConfigNode(ConfigNode cn)
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

		private bool exists(string state)
		{
			for (int i = 0; i < nodeStates.Count; i++)
				if (nodeStates[i].state == state)
					return true;
			return false;
		}

		private NodeState find(ModuleDockingNode node)
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

		private JointState find(ModuleDockingNode host, ModuleDockingNode target, bool isSameVessel)
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

		public bool checkVessel(Vessel vessel, bool verbose)
		{
			List<ModuleDockingNode> dn = vessel.FindPartModulesImplementing<ModuleDockingNode>();
			dn = new List<ModuleDockingNode>(dn);
			dn.Sort((a, b) => (int) a.part.flightID - (int) b.part.flightID);
			bool foundError = false;
			for (int i = 0; i < dn.Count; i++) {
				ModuleDockingNode node = dn[i];
				if (checkNode(node, verbose))
					foundError = true;
			}
			return foundError;
		}

		public bool checkNode(ModuleDockingNode node, bool verbose)
		{
			if (!node)
				return false;
			if (!enabledCheck)
				return false;
			node.part.SetHighlightDefault();
			ModuleDockRotate mdr = node.getDockRotate();
			if (mdr)
				mdr.showCheckDockingState(false);

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

			bool foundError = false;
			if (msg.Count > 1) {
				foundError = true;
			} else if (verbose) {
				msg.Add("is ok");
			}

			if (msg.Count > 1)
				log(String.Join(",\n\t", msg.ToArray()));

			if (foundError) {
				flash(node.part, colorBad);
				if (mdr)
					mdr.showCheckDockingState(true);
			}
			return foundError;
		}

		public class NodeState
		{
			public DockingStateChecker checker = null;

			[Persistent] public string state = "";
			[Persistent] public bool hasJoint = false;
			[Persistent] public bool isSameVessel = false;
			[Persistent] public string nodeFixTo = "";

			public static implicit operator bool(NodeState s)
			{
				return s != null;
			}

			public NodeState() { }

			public NodeState(string state, bool hasJoint, bool isSameVessel,
				string nodeFixTo = "")
			{
				this.state = state;
				this.hasJoint = hasJoint;
				this.isSameVessel = isSameVessel;
				this.nodeFixTo = nodeFixTo;
			}

			public string desc()
			{
				return nameof(NodeState)
					+ ":" + nameof(state) + "=" + state
					+ ":" + nameof(hasJoint) + "=" + hasJoint
					+ ":" + nameof(isSameVessel) + "=" + isSameVessel
					+ ":" + nameof(nodeFixTo) + "=" + nodeFixTo;
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
				ret.comment = "this node state is allowed";
				if (!fixable())
					ret.RemoveValues(nameof(nodeFixTo));
				return ret;
			}

			public bool fixable()
			{
				return nodeFixTo != "";
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
					+ ":" + nameof(hostState) + "=" + hostState
					+ ":" + nameof(targetState) + "=" + targetState
					+ ":" + nameof(isSameVessel) + "=" + isSameVessel
					+ ":" + nameof(hostFixTo) + "=" + hostFixTo
					+ ":" + nameof(targetFixTo) + "=" + targetFixTo;
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
				if (fixable()) {
					ret.comment = "this connected pair state is fixable";
				} else {
					ret.comment = "this connected pair state is allowed";
					ret.RemoveValues(nameof(hostFixTo), nameof(targetFixTo));
				}
				return ret;
			}

			public bool fixable()
			{
				return hostFixTo != "" || targetFixTo != "";
			}

			public JointState fix(ModuleDockingNode host, ModuleDockingNode target)
			{
				if (checker == null || !fixable())
					return null;
				if (!checker.enabledFix) {
					log("FIXABLE TO "
						+ (hostFixTo == "" ? S(host) : hostFixTo)
						+ " -> "
						+ (targetFixTo == "" ? S(target) : targetFixTo));
					return this;
				}
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
				if (ret) {
					checker.flash(host.part, checker.colorFixed);
					checker.flash(target.part, checker.colorFixed);
				}
				return ret;
			}
		};

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
			if (node.fsm != null && node.fsm.currentStateName != "")
				return node.fsm.currentStateName;
			return node.state;
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

		public void flash(Part part, Color color)
		{
			flash(part, color, highlightTimeout);
		}

		public void flash(Part part, Color color, float timeOut)
		{
			part.SetHighlightColor(color);
			part.SetHighlightType(Part.HighlightType.AlwaysOn);
			part.StartCoroutine(unHighlight(part, timeOut));
		}

		private IEnumerator unHighlight(Part p, float waitSeconds)
		{
			yield return new WaitForSeconds(waitSeconds);
			p.SetHighlightDefault();
		}

		private static bool log(string msg)
		{
			return Extensions.log("[" + nameof(DockingStateChecker) + "] " + msg);
		}
	}
}
