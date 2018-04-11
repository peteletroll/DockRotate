using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class RotationAnimation
	{
		private Part part, otherPart;
		private Vector3 node, axis;
		private PartJoint joint;
		public bool smartAutoStruts = false;

		public float pos, tgt, vel;
		private float maxvel, maxacc;

		private Guid vesselId;
		private Part startParent;

		public static string soundFile = "DockRotate/DockRotateMotor";
		public AudioSource sound;
		public float pitchAlteration;

		private struct RotJointInfo
		{
			public Quaternion localToJoint, jointToLocal;
			public Vector3 jointAxis;
			public Quaternion startTgtRotation;
			public Vector3 startTgtPosition;
		}
		private RotJointInfo[] rji;

		private bool started = false, finished = false;

		private const float accelTime = 2.0f;
		private const float stopMargin = 1.5f;

		private static Dictionary<Guid, int> vesselRotCount = new Dictionary<Guid, int>();

		private static bool lprint(string msg)
		{
			return ModuleBaseRotate.lprint(msg);
		}

		/*

		Notes for generalizing for NodeRotate:
		A RotationAnimation must contain:
		- a rotating part (current DockRotate active part)
		- a rotating point (current ModuleDockRotate.partNodePos)
		- a rotating axis (current ModuleDockRotate.partNodeAxis)
		- a rotating joint (not necessarily Part.attachJoint, there's same vessel docking too)

		*/

		public RotationAnimation(Part part, Vector3 node, Vector3 axis, PartJoint joint, float pos, float tgt, float maxvel)
		{
			this.part = part;
			this.node = node;
			this.axis = axis;
			this.joint = joint;

			this.otherPart = joint.Host == part ? joint.Target : joint.Host;
			this.vesselId = part.vessel.id;
			this.startParent = part.parent;

			this.pos = pos;
			this.tgt = tgt;
			this.maxvel = maxvel;

			this.vel = 0;
			this.maxacc = maxvel / accelTime;
		}

		private int incCount()
		{
			if (!vesselRotCount.ContainsKey(vesselId))
				vesselRotCount[vesselId] = 0;
			int ret = vesselRotCount[vesselId];
			if (ret < 0) {
				lprint("WARNING: vesselRotCount[" + vesselId + "] = " + ret + " in incCount()");
				ret = vesselRotCount[vesselId] = 0;
			}
			return vesselRotCount[vesselId] = ++ret;
		}

		private int decCount()
		{
			if (!vesselRotCount.ContainsKey(vesselId))
				vesselRotCount[vesselId] = 0;
			int ret = --vesselRotCount[vesselId];
			if (ret < 0) {
				lprint("WARNING: vesselRotCount[" + vesselId + "] = " + ret + " in decCount()");
				ret = vesselRotCount[vesselId] = 0;
			}
			return ret;
		}

		public static void resetCount(Vessel v)
		{
			vesselRotCount.Remove(v.id);
		}

		public void advance(float deltat)
		{
			if (part.parent != startParent)
				abort(true, "changed parent");
			if (finished)
				return;

			bool goingRightWay = (tgt - pos) * vel >= 0;
			float brakingTime = Mathf.Abs(vel) / maxacc + 2 * stopMargin * deltat;
			float brakingSpace = Mathf.Abs(vel) / 2 * brakingTime;

			float newvel = vel;

			if (goingRightWay && Mathf.Abs(vel) <= maxvel && Math.Abs(tgt - pos) > brakingSpace) {
				// driving
				newvel += deltat * Mathf.Sign(tgt - pos) * maxacc;
				newvel = Mathf.Clamp(newvel, -maxvel, maxvel);
			} else {
				// braking
				newvel -= deltat * Mathf.Sign(vel) * maxacc;
			}

			if (!started) {
				onStart();
				started = true;
			}

			vel = newvel;
			pos += deltat * vel;

			onStep(deltat);

			if (!finished && done(deltat)) {
				onStop();
			}
		}

		private void onStart()
		{
			incCount();
			if (smartAutoStruts) {
				releaseCrossAutoStruts();
			} else {
				part.vessel.releaseAllAutoStruts();
			}
			int c = joint.joints.Count;
			rji = new RotJointInfo[c];
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = joint.joints[i];

				rji[i].localToJoint = j.localToJoint();
				rji[i].jointToLocal = rji[i].localToJoint.inverse();
				rji[i].jointAxis = axis.Td(
					part.T(),
					joint.joints[i].T());
				rji[i].startTgtRotation = j.targetRotation;
				rji[i].startTgtPosition = j.targetPosition;

				j.reconfigureForRotation();
			}

			setupSound();
			if (sound != null)
				sound.Play();

			/*
			lprint(String.Format("{0}: started {1:F4}\u00b0 at {2}\u00b0/s",
				part.desc(), tgt, maxvel));
			*/
		}

		private void onStep(float deltat)
		{
			for (int i = 0; i < joint.joints.Count; i++) {
				ConfigurableJoint j = joint.joints[i];
				Quaternion jRot = currentRotation(i);
				Quaternion pRot = Quaternion.AngleAxis(pos, rji[i].jointAxis);
				if (j) {
					j.targetRotation = jRot;
					j.targetPosition = rji[i].startTgtPosition + rji[i].jointToLocal * (pRot * j.anchor - j.anchor);

					// energy += j.currentTorque.magnitude * Mathf.Abs(vel) * deltat;
				}
			}

			if (sound != null) {
				float p = Mathf.Sqrt(Mathf.Abs(vel / maxvel));
				sound.volume = p * GameSettings.SHIP_VOLUME;
				sound.pitch = p * pitchAlteration;
			}

			// first rough attempt for electricity consumption
			if (deltat > 0) {
				double el = part.RequestResource("ElectricCharge", 1.0 * deltat);
				if (el <= 0.0)
					abort(false, "no electric charge");
			}
		}

		private void onStop()
		{
			// lprint("stop rot axis " + currentRotation(0).desc());
			if (sound != null) {
				sound.Stop();
				AudioSource.Destroy(sound);
			}

			pos = tgt;
			onStep(0);

			for (int i = 0; i < joint.joints.Count; i++) {
				Quaternion jointRot = Quaternion.AngleAxis(tgt, rji[i].jointAxis);
				ConfigurableJoint j = joint.joints[i];
				if (j) {
					// staticize joint rotation
					j.axis = jointRot * j.axis;
					j.secondaryAxis = jointRot * j.secondaryAxis;
					j.targetRotation = rji[i].startTgtRotation;

					// staticize joint target anchors
					// ModuleDockRotate m = rotationModule.proxyRotationModule;
					// Vector3 tgtAxis = m.partNodeAxis.Td(m.part.T(), m.part.rb.T());
					Vector3 tgtAxis = -axis.STd(part, otherPart).Td(otherPart.T(), otherPart.rb.T());
					Quaternion tgtRot = Quaternion.AngleAxis(pos, tgtAxis);
					j.connectedAnchor = tgtRot * j.connectedAnchor;
					j.targetPosition = rji[i].startTgtPosition;
				}
			}
			if (decCount() <= 0) {
				lprint("securing autostruts on vessel " + vesselId);
				joint.Host.vessel.secureAllAutoStruts();
			}
			lprint(part.desc() + ": rotation stopped");
			staticizeRotation();
		}

		public void setupSound()
		{
			if (sound)
				return;

			try {
				AudioClip clip = GameDatabase.Instance.GetAudioClip(soundFile);
				if (!clip) {
					lprint("clip " + soundFile + "not found");
					return;
				}

				sound = part.gameObject.AddComponent<AudioSource>();
				sound.clip = clip;
				sound.volume = 0;
				sound.pitch = 0;
				sound.loop = true;
				sound.rolloffMode = AudioRolloffMode.Logarithmic;
				sound.dopplerLevel = 0f;
				sound.maxDistance = 10;
				sound.playOnAwake = false;

				pitchAlteration = UnityEngine.Random.Range(0.9f, 1.1f);

				lprint(part.desc() + ": added sound");
			} catch (Exception e) {
				sound = null;
				lprint("sound: " + e.Message);
			}
		}

		private void staticizeRotation()
		{
			if (joint != part.attachJoint) {
				lprint(part.desc() + ": skip staticize, same vessel joint");
				return;
			}
			float angle = tgt;
			Vector3 nodeAxis = -axis.STd(part, part.vessel.rootPart);
			Quaternion nodeRot = Quaternion.AngleAxis(angle, nodeAxis);
			_propagate(part, nodeRot);
		}

		private void _propagate(Part p, Quaternion rot)
		{
			Vector3 vNode = node.STp(part, part.vessel.rootPart);
			p.orgPos = rot * (p.orgPos - vNode) + vNode;
			p.orgRot = rot * p.orgRot;

			for (int i = 0; i < p.children.Count; i++)
				_propagate(p.children[i], rot);
		}

		public void releaseCrossAutoStruts()
		{
			PartSet rotParts = part.allPartsFromHere();
			List<ModuleDockingNode> dockingNodes = part.vessel.FindPartModulesImplementing<ModuleDockingNode>();

			int count = 0;
			PartJoint[] allJoints = UnityEngine.Object.FindObjectsOfType<PartJoint>();
			for (int ii = 0; ii < allJoints.Length; ii++) {
				PartJoint j = allJoints[ii];
				if (!j.Host || j.Host.vessel != part.vessel)
					continue;
				if (!j.Target || j.Target.vessel != part.vessel)
					continue;
				if (j == j.Host.attachJoint)
					continue;
				if (j == j.Target.attachJoint)
					continue;
				if (rotParts.contains(j.Host) == rotParts.contains(j.Target))
					continue;

				bool isSameVesselJoint = false;
				for (int i = 0; !isSameVesselJoint && i < dockingNodes.Count; i++)
					if (j == dockingNodes[i].sameVesselDockJoint)
						isSameVesselJoint = true;
				if (isSameVesselJoint)
					continue;

				lprint("releasing [" + ++count + "] " + j.desc());
				j.DestroyJoint();
			}
		}

		private Quaternion currentRotation(int i)
		{
			Quaternion newJointRotation = Quaternion.AngleAxis(pos, rji[i].jointAxis);

			Quaternion rot = rji[i].jointToLocal
				* newJointRotation * rji[i].startTgtRotation
				* rji[i].localToJoint;

			return rji[i].startTgtRotation * rot;
		}

		public bool done()
		{
			return finished;
		}

		private bool done(float deltat)
		{
			if (finished)
				return true;
			if (Mathf.Abs(vel) < stopMargin * deltat * maxacc
				&& Mathf.Abs(tgt - pos) < deltat * deltat * maxacc)
				finished = true;
			return finished;
		}

		public void abort(bool hard, string msg)
		{
			lprint((hard ? "HARD " : "") + "ABORTING: " + msg);

			if (sound != null) {
				sound.Stop();
				AudioSource.Destroy(sound);
			}

			tgt = pos;
			vel = 0;
			if (hard)
				finished = true;
		}
	}

	public class PartSet: Dictionary<uint, Part>
	{
		private Part[] partArray = null;

		public void add(Part part)
		{
			partArray = null;
			Add(part.flightID, part);
		}

		public bool contains(Part part)
		{
			return ContainsKey(part.flightID);
		}

		public Part[] parts()
		{
			if (partArray != null)
				return partArray;
			List<Part> ret = new List<Part>();
			foreach (KeyValuePair<uint, Part> i in this) {
				ret.Add(i.Value);
			}
			return partArray = ret.ToArray();
		}

		public void dump()
		{
			Part[] p = parts();
			for (int i = 0; i < p.Length; i++)
				ModuleBaseRotate.lprint("rotPart " + p[i].desc());
		}
	}

	public abstract class ModuleBaseRotate: PartModule
	{
		[UI_Toggle()]
		[KSPField(
			guiName = "#DCKROT_rotation",
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true
		)]
		public bool rotationEnabled = false;

		[UI_FloatRange(
			minValue = 0,
			maxValue = 180,
			stepIncrement = 5
		)]
		[KSPField(
			guiActive = true,
			guiActiveEditor = false,
			isPersistant = true,
			guiName = "#DCKROT_rotation_step",
			guiUnits = "\u00b0"
		)]
		public float rotationStep = 15;

		[UI_FloatRange(
			minValue = 1,
			maxValue = 90,
			stepIncrement = 1
		)]
		[KSPField(
			guiActive = true,
			guiActiveEditor = false,
			isPersistant = true,
			guiName = "#DCKROT_rotation_speed",
			guiUnits = "\u00b0/s"
		)]
		public float rotationSpeed = 5;

		[UI_Toggle(affectSymCounterparts = UI_Scene.None)]
		[KSPField(
			guiActive = true,
			isPersistant = true,
			guiName = "#DCKROT_reverse_rotation"
		)]
		public bool reverseRotation = false;

		[UI_Toggle()]
		[KSPField(
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			guiName = "#DCKROT_smart_autostruts"
		)]
		public bool smartAutoStruts = false;

		[KSPField(
			guiName = "#DCKROT_angle",
			guiActive = true,
			guiActiveEditor = false
		)]
		public string angleInfo;

		[KSPField(
			guiName = "#DCKROT_status",
			guiActive = false,
			guiActiveEditor = false
		)]
		public String nodeStatus = "";

		[KSPAction(
			guiName = "#DCKROT_rotate_clockwise",
			requireFullControl = true
		)]
		public void RotateClockwise(KSPActionParam param)
		{
			ModuleBaseRotate tgt = actionTarget();
			if (tgt)
				tgt.doRotateClockwise();
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_clockwise",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateClockwise()
		{
			if (canStartRotation())
				doRotateClockwise();
		}

		[KSPAction(
			guiName = "#DCKROT_rotate_counterclockwise",
			requireFullControl = true
		)]
		public void RotateCounterclockwise(KSPActionParam param)
		{
			ModuleBaseRotate tgt = actionTarget();
			if (tgt)
				tgt.doRotateCounterclockwise();
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_counterclockwise",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateCounterclockwise()
		{
			if (canStartRotation())
				doRotateCounterclockwise();
		}

		[KSPAction(
			guiName = "#DCKROT_rotate_to_snap",
			requireFullControl = true
		)]
		public void RotateToSnap(KSPActionParam param)
		{
			ModuleBaseRotate tgt = actionTarget();
			if (tgt)
				tgt.doRotateToSnap();
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_to_snap",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateToSnap()
		{
			if (canStartRotation())
				doRotateToSnap();
		}

#if DEBUG
		[KSPEvent(
			guiName = "Dump",
			guiActive = true,
			guiActiveEditor = false
		)]
		public void Dump()
		{
			dumpPart();
		}

		[KSPEvent(
			guiName = "Toggle Autostrut Display",
			guiActive = true,
			guiActiveEditor = false
		)]
		public void ToggleAutoStrutDisplay()
		{
			PhysicsGlobals.AutoStrutDisplay = !PhysicsGlobals.AutoStrutDisplay;
		}
#endif

		public abstract void doRotateClockwise();

		public abstract void doRotateCounterclockwise();

		public abstract void doRotateToSnap();

		public abstract bool useSmartAutoStruts();

		protected abstract float rotationAngle(bool dynamic);

		protected abstract float dynamicDelta();

		protected abstract void dumpPart();

		protected abstract int countJoints();

		protected int vesselPartCount;

		protected RotationAnimation rotCur = null;

		protected bool onRails;

		public PartJoint rotatingJoint;
		public Part rotatingPart;
		public string nodeRole = "Init";
		protected Vector3 partNodePos; // node position, relative to part
		public Vector3 partNodeAxis; // node rotation axis, relative to part, reference Vector3.forward
		protected Vector3 partNodeUp; // node vector for measuring angle, relative to part

		public void OnVesselGoOnRails(Vessel v)
		{
			if (v != vessel)
				return;
			onRails = true;
			resetVessel("go on rails");
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (v != vessel)
				return;
			onRails = false;
			resetVessel("go off rails");
		}

		public override void OnAwake()
		{
			// lprint((part ? part.desc() : "<no part>") + ".OnAwake()");
			base.OnAwake();
			GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
		}

		public void OnDestroy()
		{
			// lprint((part ? part.desc() : "<no part>") + ".OnDestroy()");
			GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
		}

		protected static string[,] guiList = {
			// F: is a KSPField;
			// E: is a KSPEvent;
			// e: show in editor;
			// R: hide when rotating;
			// D: show only with debugMode activated
			// A: show with advanced tweakables
			{ "angleInfo", "F" },
			{ "nodeRole", "F" },
			{ "smartAutoStruts", "FA" },
			{ "rotationStep", "Fe" },
			{ "rotationSpeed", "Fe" },
			{ "reverseRotation", "Fe" },
			{ "RotateClockwise", "E" },
			{ "RotateCounterclockwise", "E" },
			{ "RotateToSnap", "E" },
			{ "ToggleAutoStrutDisplay", "E" }
		};

		protected void checkGuiActive()
		{
			int i;

			if (currentRotation() != null) {
				angleInfo = String.Format("{0:+0.00;-0.00;0.00}\u00b0",
					rotationAngle(true));
			} else {
				angleInfo = String.Format("{0:+0.00;-0.00;0.00}\u00b0 ({1:+0.0000;-0.0000;0.0000}\u00b0)",
					rotationAngle(false),
					dynamicDelta());
			}

			bool newGuiActive = canStartRotation();

			nodeStatus = "";

			int nJoints = countJoints();
			nodeStatus = nodeRole + " [" + nJoints + "]";

			Fields["nodeStatus"].guiActive = nodeStatus.Length > 0;

			int l = guiList.GetLength(0);
			for (i = 0; i < l; i++) {
				string name = guiList[i, 0];
				string flags = guiList[i, 1];

				bool editorGui = flags.IndexOf('e') >= 0;

				bool thisGuiActive = newGuiActive;
				if (flags.IndexOf('A') >= 0)
					thisGuiActive = thisGuiActive && GameSettings.ADVANCED_TWEAKABLES;

				if (flags.IndexOf('F') >= 0) {
					BaseField fld = Fields[name];
					if (fld != null) {
						fld.guiActive = thisGuiActive;
						fld.guiActiveEditor = thisGuiActive && editorGui;
						UI_Control uc = fld.uiControlEditor;
						if (uc != null) {
							uc.scene = (fld.guiActive ? UI_Scene.Flight : 0)
								| (fld.guiActiveEditor ? UI_Scene.Editor : 0);
						}
					}
				} else if (flags.IndexOf('E') >= 0) {
					BaseEvent ev = Events[name];
					if (ev != null) {
						if (flags.IndexOf('R') >= 0 && currentRotation() != null)
							thisGuiActive = false;
						ev.guiActive = thisGuiActive;
						ev.guiActiveEditor = thisGuiActive && editorGui;
						if (name == "ToggleAutoStrutDisplay") {
							ev.guiName = PhysicsGlobals.AutoStrutDisplay ? "Hide Autostruts" : "Show Autostruts";
						}
					}
				} else {
					lprint("bad guiList flags " + flags);
					continue;
				}
			}
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);
			if ((state & StartState.Editor) != 0)
				return;
			checkGuiActive();
		}

		public override void OnUpdate()
		{
			base.OnUpdate();
			checkGuiActive();
		}

		protected int setupStageCounter = 0;

		protected abstract void stagedSetup();

		protected void resetVessel(string msg)
		{
			bool reset = false;
			List<ModuleBaseRotate> rotationModules = vessel.FindPartModulesImplementing<ModuleBaseRotate>();
			for (int i = 0; i < rotationModules.Count; i++) {
				ModuleBaseRotate m = rotationModules[i];
				if (m.setupStageCounter != 0) {
					reset = true;
					m.setupStageCounter = 0;
				}
			}
			if (reset && msg.Length > 0)
				lprint(part.desc() + " resets vessel: " + msg);
			RotationAnimation.resetCount(vessel);
		}

		protected virtual ModuleBaseRotate actionTarget()
		{
			return canStartRotation() ? this : null;
		}

		protected virtual bool canStartRotation()
		{
			return !onRails
				&& rotationEnabled
				&& vessel
				&& vessel.CurrentControlLevel == Vessel.ControlLevel.FULL;
		}

		protected virtual void enqueueRotation(float angle, float speed)
		{
			if (!rotatingJoint)
				return;

			if (speed < 0.5)
				return;

			string action = "none";
			if (rotCur != null) {
				rotCur.tgt += angle;
				action = "updated";
			} else {
				rotCur = new RotationAnimation(rotatingPart, partNodePos, partNodeAxis, rotatingJoint, 0, angle, speed);
				rotCur.smartAutoStruts = useSmartAutoStruts();
				action = "added";
			}
			lprint(String.Format("{0}: enqueueRotation({1}, {2:F4}\u00b0, {3}\u00b0/s), {4}",
				rotatingPart.desc(), partNodeAxis.desc(), angle, speed, action));
		}

		protected virtual void advanceRotation(float deltat)
		{
			if (rotCur == null)
				return;
			if (rotCur.done()) {
				rotCur = null;
				return;
			}

			rotCur.advance(deltat);
		}

		protected abstract RotationAnimation currentRotation();

		public void FixedUpdate()
		{
			if (HighLogic.LoadedScene != GameScenes.FLIGHT)
				return;

			string resetMsg = neededResetMsg();
			if (resetMsg.Length > 0)
				resetVessel(resetMsg);

			if (rotCur != null)
				advanceRotation(Time.fixedDeltaTime);

			stagedSetup();
		}

		public virtual string neededResetMsg()
		{
			if (vessel && vessel.parts.Count != vesselPartCount)
				return "part count changed";
			return "";
		}

		/******** Debugging stuff ********/

		public static bool lprint(string msg)
		{
			print("[DockRotate]: " + msg);
			return true;
		}
	}

	public class ModuleNodeRotate: ModuleBaseRotate
	{
		[KSPField(
			isPersistant = true
		)]
		public string rotatingNodeName = "";

		public AttachNode rotatingNode;
		public Part otherPart;
		public Vector3 otherPartUp;

		protected override int countJoints()
		{
			return rotatingJoint ? rotatingJoint.joints.Count : 0;
		}

		protected override float rotationAngle(bool dynamic)
		{
			if (!otherPart)
				return float.NaN;

			Vector3 a = partNodeAxis;
			Vector3 v1 = partNodeUp;
			Vector3 v2 = dynamic ?
				otherPartUp.Td(otherPart.T(), part.T()) :
				otherPartUp.STd(otherPart, part);
			v2 = Vector3.ProjectOnPlane(v2, a).normalized;

			float angle = Vector3.Angle(v1, v2);
			float axisAngle = Vector3.Angle(a, Vector3.Cross(v2, v1));

			return (axisAngle > 90) ? angle : -angle;
		}

		protected override float dynamicDelta()
		// = dynamic - static
		{
			if (!otherPart)
				return float.NaN;

			Vector3 a = partNodeAxis;
			Vector3 vd = otherPartUp.Td(otherPart.T(), part.T());
			vd = Vector3.ProjectOnPlane(vd, a).normalized;
			Vector3 vs = otherPartUp.STd(otherPart, part);
			vs = Vector3.ProjectOnPlane(vs, a).normalized;

			float angle = Vector3.Angle(vs, vd);
			float axisAngle = Vector3.Angle(a, Vector3.Cross(vs, vd));

			return (axisAngle > 90) ? -angle : angle;
		}

		protected override void stagedSetup()
		{
			if (onRails || !part || !vessel)
				return;

			if (rotCur != null)
				return;

			switch (setupStageCounter) {

				case 0:
					rotationStep = Mathf.Abs(rotationStep);
					rotationSpeed = Mathf.Abs(rotationSpeed);

					otherPart = null;
					rotatingPart = null;
					rotatingJoint = null;
					partNodePos = partNodeAxis = partNodeUp = otherPartUp = new Vector3(9.9f, 9.9f, 9.9f);

					nodeRole = "None";

					vesselPartCount = vessel ? vessel.parts.Count : -1;
					break;

				case 1:
					if (part.FindModuleImplementing<ModuleDockRotate>())
						break;
					rotatingNode = part.physicalSignificance == Part.PhysicalSignificance.FULL ?
						part.FindAttachNode(rotatingNodeName) : null;
					if (rotatingNode == null) {
						lprint(part.desc() + " has no node named \"" + rotatingNodeName + "\"");
						AttachNode[] nodes = part.FindAttachNodes("");
						string nodeList = part.desc() + " available nodes:";
						for (int i = 0; i < nodes.Length; i++)
							nodeList += " " + nodes[i].id;
						lprint(nodeList);
					}
					AttachNode otherNode = rotatingNode != null ? rotatingNode.FindOpposingNode() : null;
					if (rotatingNode != null && otherNode != null) {
						partNodePos = rotatingNode.position;
						partNodeAxis = rotatingNode.orientation;

						partNodeUp = part.up(partNodeAxis);

						Part other = rotatingNode.attachedPart;
						if (part.parent == other) {
							otherPart = other;
							rotatingPart = part;
							nodeRole = "Active";
						} else if (other.parent == part) {
							otherPart = other;
							rotatingPart = other;
							partNodePos = partNodePos.STp(part, rotatingPart);
							partNodeAxis = -partNodeAxis.STd(part, rotatingPart);
							nodeRole = "Proxy";
						}
					}
					if (rotatingPart)
						rotatingJoint = rotatingPart.attachJoint;
					if (otherPart)
						otherPartUp = otherPart.up(partNodeAxis.STd(part, otherPart));
					break;

				case 2:
					if (rotatingJoint) {
						lprint(part.desc()
							+ ": on "
							+ (rotatingPart == part ? "itself" : rotatingPart.desc()));
					}
					break;

			}

			setupStageCounter++;
		}

		public override void doRotateClockwise()
		{
			float s = rotationStep;
			if (reverseRotation)
				s = -s;
			enqueueRotation(s, rotationSpeed);
		}

		public override void doRotateCounterclockwise()
		{
			float s = -rotationStep;
			if (reverseRotation)
				s = -s;
			enqueueRotation(s, rotationSpeed);
		}

		public override void doRotateToSnap()
		{
			// FIXME: do something here
		}

		public override bool useSmartAutoStruts()
		{
			return smartAutoStruts;
		}

		protected override RotationAnimation currentRotation()
		{
			return rotCur;
		}

		protected override bool canStartRotation()
		{
			return base.canStartRotation() && rotatingJoint;
		}

		protected override void dumpPart()
		{
			lprint("--- DUMP " + part.desc() + " ---");
			lprint("rotPart: " + rotatingPart.desc());
			lprint("rotAxis: " + partNodeAxis.desc());
			lprint("rotAxisV: " + partNodeAxis.STd(rotatingPart, vessel.rootPart).desc());
			lprint("other: " + otherPart.desc());
			AttachNode[] nodes = part.FindAttachNodes("");
			for (int i = 0; i < nodes.Length; i++) {
				AttachNode n = nodes[i];
				lprint("  node [" + i + "] \"" + n.id + "\""
					+ ", size " + n.size
					+ ", type " + n.nodeType
					+ ", method " + n.attachMethod);
				lprint("    dirV: " + n.orientation.STd(part, vessel.rootPart).desc());
				_dumpv("dir", n.orientation, n.originalOrientation);
				_dumpv("sec", n.secondaryAxis, n.originalSecondaryAxis);
				_dumpv("pos", n.position, n.originalPosition);
			}
			if (rotatingJoint) {
				lprint(rotatingJoint == part.attachJoint ? "parent joint:" : "not parent joint:");
				rotatingJoint.dump();
			}

			lprint("--------------------");
		}

		private void _dumpv(string label, Vector3 v, Vector3 orgv)
		{
			lprint("    "
				+ label + ": "
				+ v.desc()
				+ ", org " + (orgv == v ? "=" : orgv.desc()));
		}
	}

	public class ModuleDockRotate: ModuleBaseRotate
	{
		// things to be set up by stagedSetup()
		// the active module of the couple is the farthest from the root part
		// the proxy module of the couple is the closest to the root part

		private ModuleDockingNode dockingNode;
		private string lastNodeState = "Init";
		private Part lastSameVesselDockPart;
		public ModuleDockRotate activeRotationModule;
		public ModuleDockRotate proxyRotationModule;

		protected override void stagedSetup()
		{
			if (onRails || !part || !vessel)
				return;

			if (rotCur != null)
				return;

			switch (setupStageCounter) {

				case 0:
					rotationStep = Mathf.Abs(rotationStep);
					rotationSpeed = Mathf.Abs(rotationSpeed);

					dockingNode = null;
					rotatingPart = null;
					rotatingJoint = null;
					activeRotationModule = proxyRotationModule = null;
					nodeStatus = "";
					nodeRole = "None";
					partNodePos = partNodeAxis = partNodeUp = new Vector3(9.9f, 9.9f, 9.9f);

					vesselPartCount = vessel ? vessel.parts.Count : -1;
					lastNodeState = "-";
					lastSameVesselDockPart = null;

					dockingNode = part.FindModuleImplementing<ModuleDockingNode>();
					if (dockingNode) {
						partNodePos = Vector3.zero.Tp(dockingNode.T(), part.T());
						partNodeAxis = Vector3.forward.Td(dockingNode.T(), part.T());
						partNodeUp = Vector3.up.Td(dockingNode.T(), part.T());
						lastNodeState = dockingNode.state;
						if (dockingNode.sameVesselDockJoint)
							lastSameVesselDockPart = dockingNode.sameVesselDockJoint.Target;
					}
					break;

				case 1:
					if (!dockingNode)
						break;

					PartJoint svj = dockingNode.sameVesselDockJoint;
					if (svj) {
						ModuleDockRotate otherModule = svj.Target.FindModuleImplementing<ModuleDockRotate>();
						if (otherModule) {
							activeRotationModule = this;
							proxyRotationModule = otherModule;
							rotatingJoint = svj;
							nodeRole = "ActiveSame";
						}
					} else if (isActive()) {
						proxyRotationModule = part.parent.FindModuleImplementing<ModuleDockRotate>();
						if (proxyRotationModule) {
							activeRotationModule = this;
							rotatingJoint = part.attachJoint;
							nodeRole = "Active";
						}
					}
					if (activeRotationModule)
						rotatingPart = activeRotationModule.part;
					break;


				case 2:
					if (activeRotationModule == this) {
						proxyRotationModule.activeRotationModule = this;
						proxyRotationModule.proxyRotationModule = proxyRotationModule;
						proxyRotationModule.nodeRole = "Proxy";
					}
					break;

				case 3:
					if (activeRotationModule == this) {
						lprint(activeRotationModule.part.desc()
							+ ": on " + proxyRotationModule.part.desc());
					}
					break;

				case 4:
					if (dockingNode.snapRotation && dockingNode.snapOffset > 0
						&& activeRotationModule == this
						&& (rotationEnabled || proxyRotationModule.rotationEnabled)) {
						enqueueRotationToSnap(dockingNode.snapOffset, rotationSpeed);
					}
					break;
			}

			setupStageCounter++;
		}

		private bool isActive() // must be used only after setup stage 0
		{
			if (!part || !part.parent)
				return false;

			ModuleDockingNode parentNode = part.parent.FindModuleImplementing<ModuleDockingNode>();
			ModuleDockRotate parentRotate = part.parent.FindModuleImplementing<ModuleDockRotate>();

			bool ret = dockingNode && parentNode && parentRotate
				&& dockingNode.nodeType == parentNode.nodeType
				&& hasGoodState(dockingNode) && hasGoodState(parentNode)
				&& (partNodePos - parentRotate.partNodePos.Tp(parentRotate.part.T(), part.T())).magnitude < 1.0f
				&& Vector3.Angle(partNodeAxis, Vector3.back.Td(parentNode.T(), part.T())) < 3;

			// lprint("isActive(" + descPart(part) + ") = " + ret);

			return ret;
		}

		private bool hasGoodState(ModuleDockingNode node)
		{
			if (!node || node.state == null)
				return false;
			string s = node.state;
			bool ret = s.StartsWith("Docked") || s == "PreAttached";
			return ret;
		}

		protected override bool canStartRotation()
		{
			return base.canStartRotation() && activeRotationModule;
		}

		protected override int countJoints()
		{
			if (!activeRotationModule)
				return 0;
			if (!activeRotationModule.rotatingJoint)
				return 0;
			return activeRotationModule.rotatingJoint.joints.Count;
		}

		public override bool useSmartAutoStruts()
		{
			return (activeRotationModule && activeRotationModule.smartAutoStruts)
				|| (proxyRotationModule && proxyRotationModule.smartAutoStruts);
		}

		protected override float rotationAngle(bool dynamic)
		{
			if (!activeRotationModule || !proxyRotationModule)
				return float.NaN;

			Vector3 a = activeRotationModule.partNodeAxis;
			Vector3 v1 = activeRotationModule.partNodeUp;
			Vector3 v2 = dynamic ?
				proxyRotationModule.partNodeUp.Td(proxyRotationModule.part.T(), activeRotationModule.part.T()) :
				proxyRotationModule.partNodeUp.STd(proxyRotationModule.part, activeRotationModule.part);
			v2 = Vector3.ProjectOnPlane(v2, a).normalized;

			float angle = Vector3.Angle(v1, v2);
			float axisAngle = Vector3.Angle(a, Vector3.Cross(v2, v1));

			return (axisAngle > 90) ? angle : -angle;
		}

		protected override float dynamicDelta()
		// = dynamic - static
		{
			if (!activeRotationModule || !proxyRotationModule)
				return float.NaN;

			Vector3 a = activeRotationModule.partNodeAxis;
			Vector3 vd = proxyRotationModule.partNodeUp.Td(proxyRotationModule.part.T(), activeRotationModule.part.T());
			vd = Vector3.ProjectOnPlane(vd, a).normalized;
			Vector3 vs = proxyRotationModule.partNodeUp.STd(proxyRotationModule.part, activeRotationModule.part);
			vs = Vector3.ProjectOnPlane(vs, a).normalized;

			float angle = Vector3.Angle(vs, vd);
			float axisAngle = Vector3.Angle(a, Vector3.Cross(vs, vd));

			return (axisAngle > 90) ? -angle : angle;
		}

		public override string neededResetMsg()
		{
			string msg = base.neededResetMsg();
			if (msg.Length > 0)
				return msg;

			/*

				docking node states:

				* PreAttached
				* Docked (docker/same vessel/dockee) - (docker) and (same vessel) are coupled with (dockee)
				* Ready
				* Disengage
				* Acquire
				* Acquire (dockee)

			*/

			if (dockingNode && dockingNode.state != lastNodeState) {
				string newNodeState = dockingNode.state;
				ModuleDockingNode other = dockingNode.otherNode();

				lprint(part.desc() + ": from " + lastNodeState
					+ " to " + newNodeState
					+ " with " + (other ? other.part.desc() : "none"));

				lastNodeState = newNodeState;

				if (other && other.vessel == vessel) {
					if (rotCur != null) {
						lprint(part.desc() + ": same vessel, not stopping");
					} else {
						return "docking port state changed on same vessel";
					}
				} else {
					string ret = "docking port state changed";
					if (rotCur != null)
						rotCur.abort(false, ret);
					return ret;
				}
			}

			Part svdp = (dockingNode && dockingNode.sameVesselDockJoint) ?
				dockingNode.sameVesselDockJoint.Target : null;
			if (dockingNode && rotCur == null && svdp != lastSameVesselDockPart) {
				lastSameVesselDockPart = svdp;
				return "changed same vessel joint";
			}

			return "";
		}

		protected override void enqueueRotation(float angle, float speed)
		{
			if (activeRotationModule != this) {
				lprint("enqueueRotation() called on wrong module, ignoring");
				return;
			}
			base.enqueueRotation(angle, speed);
		}

		private void enqueueRotationToSnap(float snap, float speed)
		{
			snap = Mathf.Abs(snap);
			if (snap < 0.5)
				return;

			float a = rotCur == null ? rotationAngle(false) : rotCur.tgt;
			if (float.IsNaN(a))
				return;
			float f = snap * Mathf.Floor(a / snap + 0.5f);
			lprint(String.Format("{0}: snap {1:F4} to {2:F4} ({3:F4})",
				part.desc(), a, f, f - a));
			enqueueRotation(f - a, rotationSpeed);
		}

		protected override void advanceRotation(float deltat)
		{
			base.advanceRotation(deltat);

			if (activeRotationModule && activeRotationModule != this) {
				lprint("advanceRotation() called on wrong module, aborting");
				if (rotCur != null)
					rotCur.abort(false, "wrong module");
				return;
			}
		}

		public override void doRotateClockwise()
		{
			float s = rotationStep;
			if (reverseRotation)
				s = -s;
			activeRotationModule.enqueueRotation(s, rotationSpeed);
		}

		public override void doRotateCounterclockwise()
		{
			float s = -rotationStep;
			if (reverseRotation)
				s = -s;
			activeRotationModule.enqueueRotation(s, rotationSpeed);
		}

		public override void doRotateToSnap()
		{
			activeRotationModule.enqueueRotationToSnap(rotationStep, rotationSpeed);
		}

		protected override RotationAnimation currentRotation()
		{
			return activeRotationModule ? activeRotationModule.rotCur : null;
		}

		protected override ModuleBaseRotate actionTarget()
		{
			if (rotationEnabled)
				return this;
			ModuleDockRotate ret = null;
			if (activeRotationModule && activeRotationModule.rotationEnabled) {
				ret = activeRotationModule;
			} else if (proxyRotationModule && proxyRotationModule.rotationEnabled) {
				ret = proxyRotationModule;
			}
			if (ret)
				lprint(part.desc() + ": forwards to " + ret.part.desc());
			return ret;
		}

		/******** Debugging stuff ********/

		protected override void dumpPart() {
			lprint("--- DUMP " + part.desc() + " ---");
			lprint("rotPart: " + rotatingPart.desc());
			/*
			lprint("mass: " + part.mass);
			lprint("parent: " + descPart(part.parent));
			*/
			lprint("role: " + nodeRole + ", status: " + nodeStatus);
			lprint("org: " + part.orgPos.desc() + ", " + part.orgRot.desc());

			if (dockingNode) {
				// lprint("size: " + dockingNode.nodeType);
				lprint("state: " + dockingNode.state);

				ModuleDockingNode other = dockingNode.otherNode();
				lprint("other: " + (other ? other.part.desc() : "none"));

				/*
				lprint("partNodePos: " + partNodePos);
				lprint("partNodeAxis: " + partNodeAxis);
				lprint("partNodeUp: " + partNodeUp);
				*/

				lprint("partNodeAxisV: " + partNodeAxis.STd(part, vessel.rootPart).desc());
			}

			if (rotatingJoint) {
				lprint(rotatingJoint == part.attachJoint ? "parent joint:" : "same vessel joint:");
				rotatingJoint.dump();
			}

			lprint("--------------------");
		}
	}

	public static class Extensions
	{
		private static bool lprint(string msg)
		{
			return ModuleBaseRotate.lprint(msg);
		}

		/******** Vessel utilities ********/

		public static void releaseAllAutoStruts(this Vessel v)
		{
			List<Part> parts = v.parts;
			for (int i = 0; i < parts.Count; i++) {
				parts[i].ReleaseAutoStruts();
			}
		}

		public static void secureAllAutoStruts(this Vessel v)
		{
			v.releaseAllAutoStruts();
			v.CycleAllAutoStrut();
		}

		/******** Part utilities ********/

		public static PartSet allPartsFromHere(this Part p)
		{
			PartSet ret = new PartSet();
			_collect(ret, p);
			return ret;
		}

		private static void _collect(PartSet s, Part p)
		{
			s.add(p);
			for (int i = 0; i < p.children.Count; i++)
				_collect(s, p.children[i]);
		}

		public static string desc(this Part part)
		{
			if (!part)
				return "<null>";
			ModuleDockRotate mdr = part.FindModuleImplementing<ModuleDockRotate>();
			return part.name + "_" + part.flightID
				+ (mdr ? "_" + mdr.nodeRole : "");
		}

		public static Vector3 up(this Part part, Vector3 axis)
		{
			Vector3 up1 = Vector3.ProjectOnPlane(Vector3.up, axis);
			Vector3 up2 = Vector3.ProjectOnPlane(Vector3.forward, axis);
			return (up1.magnitude > up2.magnitude ? up1 : up2).normalized;
		}

		/******** ModuleDockingMode utilities ********/

		public static ModuleDockingNode otherNode(this ModuleDockingNode node)
		{
			// this prevents a warning
			if (node.dockedPartUId <= 0)
				return null;
			return node.FindOtherNode();
		}

		/******** PartJoint utilities ********/

		public static string desc(this PartJoint j)
		{
			string from = (j.Host == j.Child ? j.Host.desc() : j.Host.desc() + "/" + j.Child.desc());
			string to = (j.Target == j.Parent ? j.Target.desc() : j.Target.desc() + "/" + j.Parent.desc());
			return from + " -> " + to;
		}

		public static void dump(this PartJoint j)
		{
			// lprint("Joint Parent: " + descPart(joint.Parent));

			/*
			lprint("jChild: " + j.Child.desc());
			lprint("jHost: " + j.Host.desc());
			lprint("jTarget: " + j.Target.desc());
			*/

			/*
			lprint("jAxis: " + j.Axis);
			lprint("jSecAxis: " + j.SecAxis);
			*/

			for (int i = 0; i < j.joints.Count; i++) {
				lprint("ConfigurableJoint[" + i + "]:");
				j.joints[i].dump();
			}
		}

		/******** ConfigurableJoint utilities ********/

		public static Quaternion localToJoint(this ConfigurableJoint j)
		{
			// the returned rotation turns Vector3.right (1, 0, 0) to axis
			// and Vector3.up (0, 1, 0) to secondaryAxis

			// localToJoint() * v means:
			// vector v expressed in local coordinates defined by (axis, secondaryAxis)
			// result is same vector in joint transform space

			Vector3 right = j.axis.normalized;
			Vector3 forward = Vector3.Cross(j.axis, j.secondaryAxis).normalized;
			Vector3 up = Vector3.Cross(forward, right).normalized;
			return Quaternion.LookRotation(forward, up);
		}

		public static string desc(this JointDrive drive)
		{
			return "drv(maxFrc=" + drive.maximumForce
				+ " posSpring=" + drive.positionSpring
				+ " posDamp=" + drive.positionDamper
				+ ")";
		}

		public static string desc(this SoftJointLimit limit)
		{
			return "lim(lim=" + limit.limit
				+ " bounce=" + limit.bounciness
				+ " cDist=" + limit.contactDistance
				+ ")";
		}

		public static void reconfigureForRotation(this ConfigurableJoint joint)
		{
			ConfigurableJointMotion f = ConfigurableJointMotion.Free;
			joint.angularXMotion = f;
			joint.angularYMotion = f;
			joint.angularZMotion = f;
			joint.xMotion = f;
			joint.yMotion = f;
			joint.zMotion = f;
		}

		public static void disable(this ConfigurableJoint joint)
		{
			joint.reconfigureForRotation();
			JointDrive d = joint.angularXDrive;
			d.positionSpring = 0;
			d.positionDamper = 0;
			d.maximumForce = 1e20f;
			joint.angularXDrive = d;
			joint.angularYZDrive = d;
		}

		public static void dump(this ConfigurableJoint j)
		{
			// Quaternion localToJoint = j.localToJoint();

			lprint("  Link: " + j.gameObject + " to " + j.connectedBody);
			// lprint("  autoConf: " + j.autoConfigureConnectedAnchor);
			// lprint("  swap: " + j.swapBodies);
			lprint("  Axes: " + j.axis.desc() + ", " + j.secondaryAxis.desc());
			// lprint("  localToJoint: " + localToJoint.desc());
			lprint("  Anchors: " + j.anchor.desc()
				+ " -> " + j.connectedAnchor.desc()
				+ " [" + j.connectedAnchor.Tp(j.connectedBody.T(), j.T()).desc() + "]");

			/*
			lprint("  thdAxis: " + Vector3.Cross(joint.axis, joint.secondaryAxis));
			lprint("  axisV: " + Td(joint.axis, T(joint), T(vessel.rootPart)));
			lprint("  secAxisV: " + Td(joint.secondaryAxis, T(joint), T(vessel.rootPart)));
			lprint("  jSpacePartAxis: " + Td(partNodeAxis, T(part), T(joint)));
			*/

			/*
			Quaternion axr = joint.axisRotation();
			lprint("  axisRotation: " + axr.desc());
			lprint("  axr*right: " + (axr * Vector3.right));
			lprint("  axr*up: " + (axr * Vector3.up));
			lprint("  axr*forward: " + (axr * Vector3.forward));
			*/

			/*
			lprint("  AXMot: " + j.angularXMotion);
			lprint("  LAXLim: " + j.lowAngularXLimit.desc());
			lprint("  HAXLim: " + j.highAngularXLimit.desc());
			lprint("  AXDrv: " + j.angularXDrive.desc());

			lprint("  YMot: " + j.yMotion);
			lprint("  YDrv: " + j.yDrive);
			lprint("  ZMot: " + j.zMotion);
			lprint("  ZDrv: " + j.zDrive.desc());
			*/

			lprint("  Tgt: " + j.targetPosition.desc() + ", " + j.targetRotation.desc());
			// lprint("  TgtPosP: " + Tp(j.targetPosition, T(j), T(part)));

			/*
			lprint("  AnchorsP: " + Tp(j.anchor, T(j), T(part))
				+ " -> " + Tp(j.connectedAnchor, T(j.connectedBody), T(part)));
			*/

			/*
			lprint("Joint YMot: " + joint.Joint.angularYMotion);
			lprint("Joint YLim: " + descLim(joint.Joint.angularYLimit));
			lprint("Joint aYZDrv: " + descDrv(joint.Joint.angularYZDrive));
			lprint("Joint RMode: " + joint.Joint.rotationDriveMode);
			*/
		}

		/******** Vector3 utilities ********/

		public static string desc(this Vector3 v)
		{
			return "(" + v.x.ToString("F2")
				+ ", " + v.y.ToString("F2")
				+ ", " + v.z.ToString("F2")
				+ ")";
		}

		/******** Quaternion utilities ********/

		public static Quaternion inverse(this Quaternion q)
		{
			return Quaternion.Inverse(q);
		}

		public static float angle(this Quaternion q)
		{
			float angle;
			Vector3 axis;
			q.ToAngleAxis(out angle, out axis);
			return angle;
		}

		public static Vector3 axis(this Quaternion q)
		{
			float angle;
			Vector3 axis;
			q.ToAngleAxis(out angle, out axis);
			return axis;
		}

		public static string desc(this Quaternion q)
		{
			float angle;
			Vector3 axis;
			q.ToAngleAxis(out angle, out axis);
			return angle.ToString("F1") + "\u00b0" + axis.desc();
		}

		/******** Reference change utilities - dynamic ********/

		public static Vector3 Td(this Vector3 v, Transform from, Transform to)
		{
			return to.InverseTransformDirection(from.TransformDirection(v));
		}

		public static Vector3 Tp(this Vector3 v, Transform from, Transform to)
		{
			return to.InverseTransformPoint(from.TransformPoint(v));
		}

		public static Transform T(this Vessel v)
		{
			return v.rootPart.transform;
		}

		public static Transform T(this Part p)
		{
			return p.transform;
		}

		public static Transform T(this ConfigurableJoint j)
		{
			return j.transform;
		}

		public static Transform T(this Rigidbody b)
		{
			return b.transform;
		}

		public static Transform T(this ModuleDockingNode m)
		{
			return m.nodeTransform;
		}

		/******** Reference change utilities - static ********/

		public static Vector3 STd(this Vector3 v, Part from, Part to)
		{
			return to.orgRot.inverse() * (from.orgRot * v);
		}

		public static Vector3 STp(this Vector3 v, Part from, Part to)
		{
			Vector3 vv = from.orgPos + from.orgRot * v;
			return to.orgRot.inverse() * (vv - to.orgPos);
		}
	}
}

