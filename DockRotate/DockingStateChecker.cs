using System;
using System.Text;
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
		[Persistent] public bool enabledRedundantSameVesselUndock = false;
		[Persistent] public int checkDelay = 5;
		[Persistent] public float messageTimeout = 3f;
		[Persistent] public ScreenMessageStyle messageStyle = ScreenMessageStyle.UPPER_CENTER;
		[Persistent] public Color colorBad = Color.red;
		[Persistent] public Color colorFixable = 0.5f * (Color.red + Color.yellow);
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


		private static DockingStateChecker lastLoaded = null;
		private static int lastLoadedAt = 0;

		public static DockingStateChecker load()
		{
			if (lastLoaded != null && lastLoadedAt == Time.frameCount)
				return lastLoaded;

			lastLoaded = null;
			try {
				log("loading " + configFile());
				ConfigNode cn = ConfigNode.Load(configFile());
				if (cn == null)
					throw new Exception("null ConfigNode");
				cn = cn.GetNode(configName);
				if (cn == null)
					throw new Exception("can't find " + configName);
				lastLoaded = fromConfigNode(cn);
				lastLoadedAt = Time.frameCount;
				if (lastLoaded.enabledCheck)
					log("loaded\n" + lastLoaded.desc() + "\n");
				else
					log("loaded, check disabled");
			} catch (Exception e) {
				log("can't load: " + e.Message + "\n" + e.StackTrace);
				log("using builtin configuration");
				lastLoaded = builtin();
				if (!System.IO.File.Exists(configFile()))
					lastLoaded.save();
			}
			return lastLoaded;
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

		private static readonly NodeState[] allowedNodeStates = {
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

		private static readonly JointState[] allowedJointStates = {
			new JointState("PreAttached", "PreAttached", false),
			new JointState("Docked (docker)", "Docked (dockee)", false),
			new JointState("Docked (dockee)", "Docked (docker)", false),
			new JointState("Docked (same vessel)", "Docked (dockee)", true),

			new JointState("Docked (dockee)", "Docked (dockee)", false,
				"Docked (docker)", ""),
			new JointState("Docked (same vessel)", "Docked (dockee)", false,
				"Docked (docker)", ""),
			new JointState("Docked (docker)", "Docked (same vessel)", false,
				"", "Docked (dockee)"),
			new JointState("Docked (dockee)", "Docked (same vessel)", false,
				"", "Docked (docker)"),
			new JointState("Disengage", "Disengage", false,
				"Docked (docker)", "Docked (dockee)"),
			new JointState("Docked (docker)", "Ready", false,
				"", "Docked (dockee)"),
			new JointState("Ready", "Docked (docker)", false,
				"Docked (dockee)", ""),
			new JointState("Docked (same vessel)", "Ready", true,
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
			PartJoint joint = node.getDockingJoint(false);
			bool hasJoint = joint;
			bool isSameVessel = joint && joint.isOffTree();
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

		public Result checkVessel(Vessel vessel, bool verbose)
		{
			Result result = new Result();
			if (!enabledCheck)
				return result;
			List<ModuleDockingNode> dn = vessel.FindPartModulesImplementing<ModuleDockingNode>();
			dn = new List<ModuleDockingNode>(dn);
			dn.Sort((a, b) => (int) a.part.flightID - (int) b.part.flightID);
			result.msg("checking vessel " + vessel.vesselName);
			for (int i = 0; i < dn.Count; i++)
				dn[i].part.SetHighlightDefault();
			for (int i = 0; i < dn.Count; i++)
				checkNode(result, dn[i], verbose);
			result.logReport();
			return result;
		}

		public Result checkNode(ModuleDockingNode node, bool verbose)
		{
			Result result = new Result();
			checkNode(result, node, verbose);
			result.logReport();
			return result;
		}

		private void checkNode(Result result, ModuleDockingNode node, bool verbose)
		{
			if (!enabledCheck)
				return;
			if (!node)
				return;
			if (!result.addNode(node))
				return;

			result.indent(0);
			result.msg("checking node " + info(node));
			result.indent(1);

			ModuleDockRotate mdr = node.getDockRotate();
			if (mdr) {
				mdr.showCheckDockingState(false);
			} else {
				result.msg("has no ModuleDockRotate");
			}

			PartJoint joint = node.getDockingJoint(verbose);
			if (joint)
				checkDockingJoint(result, node, joint);

			string label = QS(node)
				+ (joint ?
					(joint.isOffTree() ? " with same vessel joint" : " with tree joint") :
					" without joint");

			if (node.sameVesselDockJoint && node.sameVesselDockJoint.getTreeEquiv(false)) {
				result.err("redundant same vessel joint " + info(node.sameVesselDockJoint));
				flash(result, node.part, colorBad);
				if (enabledRedundantSameVesselUndock) {
					result.msg("trying to undock " + info(node.sameVesselDockJoint));
					node.UndockSameVessel();
				} else {
					result.msg("enable " + nameof(enabledRedundantSameVesselUndock) + " to fix");
				}
			}

			checkVesselInfo(result, node, joint, verbose);

			if (!joint) {
				NodeState s = find(node);
				if (s)
					s = s.tryFix(result, node);
				if (!s) {
					result.err("unallowed node state " + label);
					flash(result, node.part, colorBad);
				}
			}

			if (result.foundError) {
				if (mdr)
					mdr.showCheckDockingState(true);
			}

			result.indent(0);
		}

		private void checkVesselInfo(Result result, ModuleDockingNode node, PartJoint joint, bool verbose)
		{
			// a null vesselInfo may cause NRE later
			if (joint && joint.Host == node.part && node.vesselInfo == null
				&& S(node) != "PreAttached" && S(node) != "Docked (same vessel)") {
				result.err("null vesselInfo");
				if (node.otherNode)
					result.msg("other vesselInfo is " + node.otherNode.vesselInfo.desc());
				if (enabledFix) {
					DockedVesselInfo info = node.vesselInfo = new DockedVesselInfo();
					if (node.vessel) {
						info.vesselType = node.vessel.vesselType;
						info.name = node.vessel.vesselName;
						if (node.vessel.rootPart)
							info.rootPartUId = node.vessel.rootPart.flightID;
					}
					result.msg("fixed vesselInfo to " + node.vesselInfo.desc());
					flash(result, node.part, colorFixed);
				} else {
					flash(result, node.part, colorFixable);
				}
			}
		}

		public class Result {
			private List<string> msgList = new List<string>();
			public bool foundError = false;
			private string indentString = "";

			public void indent(int l)
			{
				indentString = new string(' ', 6 * l);
			}

			public void msg(String s)
			{
				msgList.Add(indentString + s);
			}

			public void err(string e)
			{
				foundError = true;
				msg("*** " + e + " ***");
			}

			public void logReport()
			{
				StringBuilder report = new StringBuilder();
				report.AppendLine("Check report:");
				report.AppendLine(new string('#', 80));
				for (int i = 0; i < msgList.Count; i++)
					report.AppendLine(msgList[i]);
				report.Append(new string('#', 80));
				log(report.ToString());
			}

			private HashSet<string> chk = new HashSet<string>();
			private bool noisyAdd = false;

			public bool addFlash(Part part)
			{
				string key = part.flightID + "!";
				return add(key);
			}

			public bool addNode(ModuleDockingNode node)
			{
				string key = node.part.flightID + ".";
				return add(key);
			}

			public bool addPair(ModuleDockingNode node1, ModuleDockingNode node2)
			{
				uint id1 = node1.part.flightID;
				uint id2 = node2.part.flightID;
				string key = id1 < id2 ? id1 + "|" + id2 : id2 + "|" + id1;
				return add(key);
			}

			private bool add(string key) {
				if (chk.Contains(key)) {
					if (noisyAdd)
						msg("REPEATED " + key);
					return false;
				}
				if (noisyAdd)
					msg("CHECKING " + key);
				chk.Add(key);
				return true;
			}
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

			public NodeState() { } // needed for load/save

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
					+ (nodeFixTo == "" ? "" : ":" + nameof(nodeFixTo) + "=" + nodeFixTo);
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

			public bool good()
			{
				return nodeFixTo == "";
			}

			public bool fixable()
			{
				return nodeFixTo != "";
			}

			public NodeState tryFix(Result result, ModuleDockingNode node)
			{
				if (checker == null) {
					result.msg("WARNING: tryFix() without checker");
					return this;
				}

				if (good())
					return this;

				if (!checker.enabledFix) {
					result.err("fixable to \"" + nodeFixTo + "\"");
					checker.flash(result, node.part, checker.colorFixable);
					return this;
				}

				result.msg("fixing " + info(node));
				node.DebugFSMState = true;
				checker.setState(result, node, nodeFixTo, null);
				result.msg("fixed to " + info(node));

				NodeState ret = checker.find(node);
				Color hl = Color.clear;
				if (!ret) {
					result.err("fixed to bad state");
					hl = checker.colorBad;
				} else if (ret.fixable()) {
					result.err("fixed to fixable state");
					hl = checker.colorFixable;
					ret = this;
				} else {
					hl = checker.colorFixed;
				}
				if (hl != Color.clear)
					checker.flash(result, node.part, hl);
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

			public JointState() { } // needed for load/save

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
					+ (hostFixTo == "" ? "" : ":" + nameof(hostFixTo) + "=" + hostFixTo)
					+ (targetFixTo == "" ? "" : ":" + nameof(targetFixTo) + "=" + targetFixTo);
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

			public bool good()
			{
				return hostFixTo == "" && targetFixTo == "";
			}

			public bool fixable()
			{
				return !good();
			}

			public JointState tryFix(Result result, ModuleDockingNode host, ModuleDockingNode target)
			{
				if (checker == null) {
					result.msg("WARNING: tryFix() without checker");
					return this;
				}

				if (good())
					return this;

				ModuleDockRotate mdr = host.getDockRotate();
				if (!mdr)
					mdr = target.getDockRotate();
				bool rotating = mdr && mdr.isRotating();

				if (!checker.enabledFix || rotating) {
					result.err("fixable to "
						+ (hostFixTo == "" ? QS(host) : "\"" + hostFixTo + "\"")
						+ " -> "
						+ (targetFixTo == "" ? QS(target) : "\"" + targetFixTo + "\""));
					checker.flash(result, host.part, checker.colorFixable);
					checker.flash(result, target.part, checker.colorFixable);
					if (rotating)
						result.msg("rotating, not fixed");
					return this;
				}

				result.msg("fixing " + info(host) + " -> " + info(target));
				host.DebugFSMState = target.DebugFSMState = true;
				if (hostFixTo != "")
					checker.setState(result, host, hostFixTo, target);
				if (targetFixTo != "")
					checker.setState(result, target, targetFixTo, host);
				result.msg("fixed to " + info(host) + " -> " + info(target));

				JointState ret = checker.find(host, target, isSameVessel);
				Color hl = Color.clear;
				if (!ret) {
					result.err("fixed to bad state");
					hl = checker.colorBad;
				} else if (ret.fixable()) {
					result.err("fixed to fixable state");
					hl = checker.colorFixable;
					ret = this;
				} else {
					hl = checker.colorFixed;
				}
				if (hl != Color.clear) {
					checker.flash(result, host.part, hl);
					checker.flash(result, target.part, hl);
				}
				return ret;
			}
		}

		private void checkDockingJoint(Result result, ModuleDockingNode node, PartJoint joint)
		{
			if (!joint)
				return;

			result.msg("has docking joint " + info(joint));

			if (!joint.safetyCheck()) {
				result.err("joint fails safety check");
				flash(result, node.part, colorBad);
				return;
			}

			ModuleDockingNode other = node.getDockedNode(false);
			if (!other) {
				result.err("no other node");
				flash(result, node.part, colorBad);
				return;
			}
			result.msg("other is " + info(other));

			if (!result.addPair(node, other)) {
				result.msg("pair already checked");
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
				result.err("docking joint is unrelated");
				flash(result, node.part, colorBad);
				flash(result, other.part, colorBad);
				return;
			}

			JointState s = find(host, target, joint.isOffTree());
			if (s)
				s = s.tryFix(result, host, target);

			if (!s) {
				result.err("unallowed couple state "
					+ QS(host) + " > " + QS(target)
					+ " " + (joint.isOffTree() ? "same vessel" : "tree"));
				flash(result, host.part, colorBad);
				flash(result, target.part, colorBad);
			}
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

		private void setState(Result result, ModuleDockingNode node, string state, ModuleDockingNode other)
		{
			if (!exists(state)) {
				result.err("setState(\"" + state + "\") not allowed");
				return;
			}
			if (!node || node.fsm == null)
				return;
			if (node.otherNode != other) {
				result.msg("updating otherNode from " + info(node.otherNode)
					+ " to " + info(other));
				node.otherNode = other;
			}
			uint otherID = other ? other.part.flightID : 0;
			if (node.dockedPartUId != otherID) {
				result.msg("updating dockedPartUId from " + node.dockedPartUId + " to " + otherID);
				node.dockedPartUId = otherID;
			}
			node.DebugFSMState = true;
			if (node.fsm != null)
				node.fsm.StartFSM(state);
		}

		private static string info(Part part)
		{
			if (!part)
				return "null-part";
			return part.bareName()
				+ "-" + part.flightID;
		}

		static string info(ModuleDockingNode node)
		{
			if (!node)
				return "null-node";
			return info(node.part) + " " + QS(node);
		}

		private static string info(PartJoint j)
		{
			if (!j)
				return "null-joint";
			return j.GetInstanceID() + ","
				+ " " + info(j.Host)
				+ " " + new string('>', j.joints.Count)
				+ " " + info(j.Target)
				+ (j.isOffTree() ? ", same vessel" : "");
		}

		private void flash(Result result, Part part, Color color)
		{
			if (!result.addFlash(part))
				return;
			// log("FLASH " + part.flightID + " " + color);
			part.SetHighlightColor(color);
			part.SetHighlightType(Part.HighlightType.AlwaysOn);
			part.StartCoroutine(unHighlight(part, highlightTimeout));
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
