using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;

namespace DockRotate
{
	public abstract class SmoothMotion
	{
		public float pos;
		public float vel;
		public float tgt;

		public float maxvel = 1.0f;
		private float maxacc = 1.0f;

		public bool started = false, finished = false;

		private const float accelTime = 2.0f;
		private const float stopMargin = 1.5f;

		protected abstract void onStart();
		protected abstract void onStep(float deltat);
		protected abstract void onStop();

		public virtual void advance(float deltat)
		{
			if (finished)
				return;

			maxacc = maxvel / accelTime;

			bool goingRightWay = (tgt - pos) * vel >= 0;
			float brakingTime = Mathf.Abs(vel) / maxacc + 2 * stopMargin * deltat;
			float brakingSpace = Mathf.Abs(vel) / 2 * brakingTime;

			float newvel = vel;

			if (goingRightWay && Mathf.Abs(vel) <= maxvel && Mathf.Abs(tgt - pos) > brakingSpace) {
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

			if (!finished && checkFinished(deltat))
				onStop();
		}

		private bool checkFinished(float deltat)
		{
			if (finished)
				return true;
			if (Mathf.Abs(vel) < stopMargin * deltat * maxacc
				&& Mathf.Abs(tgt - pos) < deltat * deltat * maxacc) {
				finished = true;
				pos = tgt;
			}

			return finished;
		}

		public bool done()
		{
			return finished;
		}
	}

	public class VesselRotInfo
	{
		public Guid id;
		public int rotCount = 0;

		private static Dictionary<Guid, VesselRotInfo> vesselInfo = new Dictionary<Guid, VesselRotInfo>();

		private VesselRotInfo(Guid id)
		{
			this.id = id;
		}

		public static VesselRotInfo getInfo(Guid id)
		{
			if (vesselInfo.ContainsKey(id))
				return vesselInfo[id];
			return vesselInfo[id] = new VesselRotInfo(id);
		}

		public static void resetInfo(Guid id)
		{
			vesselInfo.Remove(id);
		}
	}

	public class RotationAnimation: SmoothMotion
	{
		private Part activePart, proxyPart;
		private Vector3 node, axis;
		private PartJoint joint;
		public bool smartAutoStruts = false;

		private Guid vesselId;
		private Part startParent;

		public static string soundFile = "DockRotate/DockRotateMotor";
		public AudioSource sound;
		public float pitchAlteration;

		private struct RotJointInfo
		{
			public ConfigurableJoint joint;
			public JointManager jm;
			public Vector3 localAxis, localNode;
			public Vector3 jointAxis, jointNode;
			public Vector3 connectedBodyAxis, connectedBodyNode;
		}
		private RotJointInfo[] rji;

		private static bool lprint(string msg)
		{
			return ModuleBaseRotate.lprint(msg);
		}

		public RotationAnimation(Part part, Vector3 node, Vector3 axis, PartJoint joint, float pos, float tgt, float maxvel)
		{
			this.activePart = part;
			this.node = node;
			this.axis = axis;
			this.joint = joint;

			this.proxyPart = joint.Host == part ? joint.Target : joint.Host;
			this.vesselId = part.vessel.id;
			this.startParent = part.parent;

			this.pos = pos;
			this.tgt = tgt;
			this.maxvel = maxvel;

			this.vel = 0;
		}

		private int changeCount(int delta)
		{
			VesselRotInfo vi = VesselRotInfo.getInfo(vesselId);
			int ret = vi.rotCount + delta;
			if (ret < 0) {
				lprint("WARNING: vesselRotCount[" + vesselId + "] = " + ret + " in changeCount(" + delta + ")");
				ret = 0;
			}
			return vi.rotCount = ret;

		}

		public override void advance(float deltat)
		{
			if (activePart.parent != startParent)
				abort(true, "changed parent");
			if (finished)
				return;

			base.advance(deltat);
		}

		protected override void onStart()
		{
			changeCount(+1);
			if (smartAutoStruts) {
				activePart.releaseCrossAutoStruts();
			} else {
				// not needed with new IsJointUnlocked() logic
				// but IsJointUnlocked() logic is bugged now :-(
				activePart.vessel.releaseAllAutoStruts();
			}
			int c = joint.joints.Count;
			rji = new RotJointInfo[c];
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = joint.joints[i];

				RotJointInfo ji;

				ji.joint = j;
				ji.jm = new JointManager();
				ji.jm.setup(j);

				ji.localAxis = axis.Td(activePart.T(), j.T());
				ji.localNode = node.Tp(activePart.T(), j.T());

				ji.jointAxis = ji.jm.L2Jd(ji.localAxis);
				ji.jointNode = ji.jm.L2Jp(ji.localNode);

				ji.connectedBodyAxis = axis.STd(activePart, proxyPart)
					.Td(proxyPart.T(), proxyPart.rb.T());
				ji.connectedBodyNode = node.STp(activePart, proxyPart)
					.Tp(proxyPart.T(), proxyPart.rb.T());

				rji[i] = ji;

				j.reconfigureForRotation();
			}

			startSound();

			/*
			lprint(String.Format("{0}: started {1:F4}\u00b0 at {2}\u00b0/s",
				part.desc(), tgt, maxvel));
			*/
		}

		protected override void onStep(float deltat)
		{
			for (int i = 0; i < joint.joints.Count; i++) {
				ConfigurableJoint j = joint.joints[i];
				if (j) {
					RotJointInfo ji = rji[i];

					Quaternion jointRotation = ji.jointAxis.rotation(pos);

					j.targetRotation = ji.jm.tgtRot0 * jointRotation;
					j.targetPosition = jointRotation * (ji.jm.tgtPos0 - ji.jointNode) + ji.jointNode;

					// energy += j.currentTorque.magnitude * Mathf.Abs(vel) * deltat;
				}
			}

			stepSound();

			// first rough attempt for electricity consumption
			if (deltat > 0) {
				double el = activePart.RequestResource("ElectricCharge", 1.0 * deltat);
				if (el <= 0.0)
					abort(false, "no electric charge");
			}
		}

		protected override void onStop()
		{
			// lprint("stop rot axis " + currentRotation(0).desc());

			stopSound();

			onStep(0);

			staticizeOrgInfo();
			staticizeJoints();

			if (changeCount(-1) <= 0) {
				if (smartAutoStruts) {
					lprint("securing autostruts on vessel " + vesselId);
					joint.Host.vessel.secureAllAutoStruts();
				} else {
					// no action needed with IsJountUnlocked() logic
					// but IsJountUnlocked() logic is bugged now
					joint.Host.vessel.secureAllAutoStruts();
				}
			}
			lprint(activePart.desc() + ": rotation stopped");
		}

		public void startSound()
		{
			if (sound)
				return;

			try {
				AudioClip clip = GameDatabase.Instance.GetAudioClip(soundFile);
				if (!clip) {
					lprint("clip " + soundFile + "not found");
					return;
				}

				sound = activePart.gameObject.AddComponent<AudioSource>();
				sound.clip = clip;
				sound.volume = 0;
				sound.pitch = 0;
				sound.loop = true;
				sound.rolloffMode = AudioRolloffMode.Logarithmic;
				sound.dopplerLevel = 0f;
				sound.maxDistance = 10;
				sound.playOnAwake = false;

				pitchAlteration = UnityEngine.Random.Range(0.9f, 1.1f);

				sound.Play();

				// lprint(activePart.desc() + ": added sound");
			} catch (Exception e) {
				sound = null;
				lprint("sound: " + e.Message);
			}
		}

		public void stepSound()
		{
			if (sound != null) {
				float p = Mathf.Sqrt(Mathf.Abs(vel / maxvel));
				sound.volume = p * GameSettings.SHIP_VOLUME;
				sound.pitch = p * pitchAlteration;
			}
		}

		public void stopSound()
		{
			if (sound != null) {
				sound.Stop();
				AudioSource.Destroy(sound);
				sound = null;
			}
		}

		private void staticizeJoints()
		{
			for (int i = 0; i < joint.joints.Count; i++) {
				ConfigurableJoint j = joint.joints[i];
				if (j) {
					RotJointInfo ji = rji[i];

					// staticize joint rotation
					Quaternion jointRot = ji.localAxis.rotation(tgt);
					j.axis = jointRot * j.axis;
					j.secondaryAxis = jointRot * j.secondaryAxis;
					j.targetRotation = ji.jm.tgtRot0;

					Quaternion connectedBodyRot = ji.connectedBodyAxis.rotation(-tgt);
					j.connectedAnchor = connectedBodyRot * (j.connectedAnchor - ji.connectedBodyNode)
						+ ji.connectedBodyNode;
					j.targetPosition = ji.jm.tgtPos0;

					ji.jm.setup(j);
				}
			}
		}

		private bool staticizeOrgInfo()
		{
			if (joint != activePart.attachJoint) {
				lprint(activePart.desc() + ": skip staticize, same vessel joint");
				return false;
			}
			float angle = tgt;
			Vector3 nodeAxis = -axis.STd(activePart, activePart.vessel.rootPart);
			Quaternion nodeRot = nodeAxis.rotation(angle);
			Vector3 nodePos = node.STp(activePart, activePart.vessel.rootPart);
			_propagate(activePart, nodeRot, nodePos);
			return true;
		}

		private void _propagate(Part p, Quaternion rot, Vector3 pos)
		{
			p.orgPos = rot * (p.orgPos - pos) + pos;
			p.orgRot = rot * p.orgRot;

			for (int i = 0; i < p.children.Count; i++)
				_propagate(p.children[i], rot, pos);
		}

		public void abort(bool hard, string msg)
		{
			lprint((hard ? "HARD " : "") + "ABORTING: " + msg);

			stopSound();

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

	public abstract class ModuleBaseRotate: PartModule, IJointLockState
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

		protected abstract float dynamicDeltaAngle();

		protected abstract void dumpPart();

		protected abstract int countJoints();

		public bool IsJointUnlocked()
		{
			bool ret = currentRotation() != null;
			lprint(part.desc() + ".IsJointUnlocked() is " + ret);
			return ret;
		}

		protected int vesselPartCount;

		private RotationAnimation _rotCur = null;
		protected RotationAnimation rotCur {
			get { return _rotCur; }
			set {
				bool wasRotating = _rotCur != null;
				_rotCur = value;
				bool isRotating = _rotCur != null;
				if (isRotating != wasRotating && !useSmartAutoStruts()) {
					lprint(part.desc() + " triggered CycleAllAutoStruts()");
					vessel.CycleAllAutoStrut();
				}
			}
		}

		protected bool onRails;

		public PartJoint rotatingJoint;
		public Part activePart, proxyPart;
		public string nodeRole = "Init";
		protected Vector3 partNodePos; // node position, relative to part
		public Vector3 partNodeAxis; // node rotation axis, relative to part
		protected Vector3 partNodeUp; // node vector for measuring angle, relative to part

		// localized info cache
		protected string displayName = "";
		protected string displayInfo = "";

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
				angleInfo = String.Format("{0:+0.00;-0.00;0.00}\u00b0 ({1:+0.00;-0.00;0.00}\u00b0/s)",
					rotationAngle(true),
					currentRotation().vel);
			} else {
				angleInfo = String.Format("{0:+0.00;-0.00;0.00}\u00b0 ({1:+0.0000;-0.0000;0.0000}\u00b0\u0394)",
					rotationAngle(false),
					dynamicDeltaAngle());
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
			VesselRotInfo.resetInfo(vessel.id);
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
				rotCur.maxvel = speed;
				action = "updated";
			} else {
				rotCur = new RotationAnimation(activePart, partNodePos, partNodeAxis, rotatingJoint, 0, angle, speed);
				rotCur.smartAutoStruts = useSmartAutoStruts();
				action = "added";
			}
			lprint(String.Format("{0}: enqueueRotation({1}, {2:F4}\u00b0, {3}\u00b0/s), {4}",
				activePart.desc(), partNodeAxis.desc(), angle, speed, action));
		}

		protected void enqueueRotationToSnap(float snap, float speed)
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
		public Vector3 otherPartUp;

		protected override int countJoints()
		{
			return rotatingJoint ? rotatingJoint.joints.Count : 0;
		}

		protected override float rotationAngle(bool dynamic)
		{
			if (!activePart || !proxyPart)
				return float.NaN;

			Vector3 a = partNodeAxis;
			Vector3 v1 = partNodeUp;
			Vector3 v2 = dynamic ?
				otherPartUp.Td(proxyPart.T(), activePart.T()) :
				otherPartUp.STd(proxyPart, activePart);
			return a.axisSignedAngle(v1, v2);
		}

		protected override float dynamicDeltaAngle()
		// = dynamic - static
		{
			if (!proxyPart)
				return float.NaN;

			Vector3 a = partNodeAxis;
			Vector3 vd = otherPartUp.Td(proxyPart.T(), activePart.T());
			Vector3 vs = otherPartUp.STd(proxyPart, activePart);
			return a.axisSignedAngle(vs, vd);
		}

		public override string GetModuleDisplayName()
		{
			if (displayName.Length <= 0)
				displayName = Localizer.Format("#DCKROT_node_displayname");
			return displayName;
		}

		public override string GetInfo()
		{
			if (displayInfo.Length <= 0)
				displayInfo = Localizer.Format("#DCKROT_node_info", rotatingNodeName);
			return displayInfo;
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

					proxyPart = null;
					activePart = null;
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
							nodeList += " \"" + nodes[i].id + "\"";
						lprint(nodeList);
					}
					AttachNode otherNode = rotatingNode != null ? rotatingNode.FindOpposingNode() : null;
					if (rotatingNode != null && otherNode != null) {
						partNodePos = rotatingNode.position;
						partNodeAxis = rotatingNode.orientation;

						partNodeUp = part.up(partNodeAxis);

						Part other = rotatingNode.attachedPart;
						if (part.parent == other) {
							nodeRole = "Active";
							activePart = part;
							proxyPart = other;
						} else if (other.parent == part) {
							nodeRole = "Proxy";
							activePart = other;
							proxyPart = part;
							partNodePos = partNodePos.STp(part, activePart);
							partNodeAxis = -partNodeAxis.STd(part, activePart);
							partNodeUp = activePart.up(partNodeAxis);
						}
					}
					if (activePart)
						rotatingJoint = activePart.attachJoint;
					if (proxyPart)
						otherPartUp = proxyPart.up(partNodeAxis.STd(part, proxyPart));
					break;

				case 2:
					if (rotatingJoint) {
						lprint(part.desc()
							+ ": on "
							+ (activePart == part ? "itself" : activePart.desc()));
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
			enqueueRotationToSnap(rotationStep, rotationSpeed);
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
			lprint("rotPart: " + activePart.desc());
			lprint("rotAxis: " + partNodeAxis.ddesc(activePart));
			lprint("rotPos: " + partNodePos.pdesc(activePart));
			lprint("rotUp: " + partNodeUp.ddesc(activePart));
			lprint("other: " + proxyPart.desc());
			AttachNode[] nodes = part.FindAttachNodes("");
			for (int i = 0; i < nodes.Length; i++) {
				AttachNode n = nodes[i];
				if (rotatingNode != null && rotatingNode.id != n.id)
					continue;
				lprint("  node [" + i + "/" + nodes.Length + "] \"" + n.id + "\""
					+ ", size " + n.size
					+ ", type " + n.nodeType
					+ ", method " + n.attachMethod);
				// lprint("    dirV: " + n.orientation.STd(part, vessel.rootPart).desc());
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

		public override string GetModuleDisplayName()
		{
			if (displayName.Length <= 0)
				displayName = Localizer.Format("#DCKROT_port_displayname");
			return displayName;
		}

		public override string GetInfo()
		{
			if (displayInfo.Length <= 0)
				displayInfo = Localizer.Format("#DCKROT_port_info");
			return displayInfo;
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

					dockingNode = null;
					activePart = proxyPart = null;
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
					} else if (isDockedToParent()) {
						proxyRotationModule = part.parent.FindModuleImplementing<ModuleDockRotate>();
						if (proxyRotationModule) {
							activeRotationModule = this;
							rotatingJoint = part.attachJoint;
							nodeRole = "Active";
						}
					}
					if (activeRotationModule) {
						activePart = activeRotationModule.part;
						proxyPart = proxyRotationModule.part;
					}
					break;

				case 2:
					if (activeRotationModule == this) {
						proxyRotationModule.nodeRole = "Proxy";
						proxyRotationModule.activeRotationModule = activeRotationModule;
						proxyRotationModule.activePart = activePart;
						proxyRotationModule.proxyRotationModule = proxyRotationModule;
						proxyRotationModule.proxyPart = proxyPart;
					}
					break;

				case 3:
					if (activeRotationModule == this) {
						proxyPart = proxyRotationModule.part;
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

		private bool isDockedToParent() // must be used only after setup stage 0
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
			return a.axisSignedAngle(v1, v2);
		}

		protected override float dynamicDeltaAngle()
		// = dynamic - static
		{
			if (!activeRotationModule || !proxyRotationModule)
				return float.NaN;

			Vector3 a = activeRotationModule.partNodeAxis;
			Vector3 vd = proxyRotationModule.partNodeUp.Td(proxyRotationModule.part.T(), activeRotationModule.part.T());
			Vector3 vs = proxyRotationModule.partNodeUp.STd(proxyRotationModule.part, activeRotationModule.part);
			return a.axisSignedAngle(vs, vd);
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
			if (!activeRotationModule)
				return;
			float s = rotationStep;
			if (reverseRotation)
				s = -s;
			activeRotationModule.enqueueRotation(s, rotationSpeed);
		}

		public override void doRotateCounterclockwise()
		{
			if (!activeRotationModule)
				return;
			float s = -rotationStep;
			if (reverseRotation)
				s = -s;
			activeRotationModule.enqueueRotation(s, rotationSpeed);
		}

		public override void doRotateToSnap()
		{
			if (!activeRotationModule)
				return;
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

		protected override void dumpPart()
		{
			lprint("--- DUMP " + part.desc() + " ---");
			lprint("rotPart: " + activePart.desc());
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
				lprint("GetFwdVector(): " + dockingNode.GetFwdVector().desc());
			}

			if (rotatingJoint) {
				lprint(rotatingJoint == part.attachJoint ? "parent joint:" : "same vessel joint:");
				rotatingJoint.dump();
			}

			lprint("--------------------");
		}
	}

	public struct JointManager
	{
		// local space:
		// defined by j.transform.

		// joint space:
		// origin is j.anchor;
		// right is j.axis
		// up is j.secondaryAxis
		// anchor, axis and secondaryAxis are defined in local space.

		private ConfigurableJoint j;
		private Quaternion localToJoint, jointToLocal;
		public Quaternion tgtRot0;
		public Vector3 tgtPos0;

		public void setup(ConfigurableJoint j)
		{
			// the jointToLocal rotation turns Vector3.right (1, 0, 0) to axis
			// and Vector3.up (0, 1, 0) to secondaryAxis

			// jointToLocal * v means:
			// vector v expressed in joint space
			// result is same vector in local space

			// source: https://answers.unity.com/questions/278147/how-to-use-target-rotation-on-a-configurable-joint.html

			this.j = j;

			Vector3 right = j.axis.normalized;
			Vector3 forward = Vector3.Cross(j.axis, j.secondaryAxis).normalized;
			Vector3 up = Vector3.Cross(forward, right).normalized;
			jointToLocal = Quaternion.LookRotation(forward, up);

			localToJoint = jointToLocal.inverse();

			tgtPos0 = j.targetPosition;
			tgtRot0 = j.targetRotation;
		}

		public Vector3 L2Jd(Vector3 v)
		{
			return localToJoint * v;
		}

		public Vector3 J2Ld(Vector3 v)
		{
			return jointToLocal * v;
		}

		public Vector3 L2Jp(Vector3 v)
		{
			return localToJoint * (v - j.anchor);
		}

		public Vector3 J2Lp(Vector3 v)
		{
			return jointToLocal * v + j.anchor;
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

		public static void releaseCrossAutoStruts(this Part part)
		{
			PartSet rotParts = part.allPartsFromHere();

			List<ModuleDockingNode> allDockingNodes = part.vessel.FindPartModulesImplementing<ModuleDockingNode>();
			List<ModuleDockingNode> sameVesselDockingNodes = new List<ModuleDockingNode>();
			for (int i = 0; i < allDockingNodes.Count; i++)
				if (allDockingNodes[i].sameVesselDockJoint)
					sameVesselDockingNodes.Add(allDockingNodes[i]);

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

				bool isSameVesselDockingJoint = false;
				for (int i = 0; !isSameVesselDockingJoint && i < sameVesselDockingNodes.Count; i++)
					if (j == sameVesselDockingNodes[i].sameVesselDockJoint)
						isSameVesselDockingJoint = true;
				if (isSameVesselDockingJoint)
					continue;

				lprint("releasing [" + ++count + "] " + j.desc());
				j.DestroyJoint();
			}
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
			string from = j.Host.desc() + "/" + (j.Child == j.Host ? "=" : j.Child.desc());
			string to = j.Target.desc() + "/" + (j.Parent == j.Target ? "=" : j.Parent.desc());
			return from + " -> " + to;
		}

		public static void dump(this PartJoint j)
		{
			lprint("PartJoint " + j.desc());
			lprint("jAxes: " + j.Axis.desc() + " " + j.SecAxis.desc());
			lprint("jAnchors: " + j.HostAnchor.desc() + " " + j.TgtAnchor.desc());

			for (int i = 0; i < j.joints.Count; i++) {
				lprint("ConfigurableJoint[" + i + "]:");
				j.joints[i].dump(j.Host);
			}
		}

		/******** ConfigurableJoint utilities ********/

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

		public static void dump(this ConfigurableJoint j, Part p = null)
		{
			// Quaternion localToJoint = j.localToJoint();

			if (p && p.vessel) {
				p = p.vessel.rootPart;
			} else {
				p = null;
			}

			lprint("  Link: " + j.gameObject + " to " + j.connectedBody);
			// lprint("  autoConf: " + j.autoConfigureConnectedAnchor);
			// lprint("  swap: " + j.swapBodies);
			lprint("  Axes: " + j.axis.desc() + ", " + j.secondaryAxis.desc());
			if (p)
				lprint("  AxesV: " + j.axis.Td(T(j), T(p)).desc()
					+ ", " + j.secondaryAxis.Td(T(j), T(p)).desc());

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

		public static Quaternion rotation(this Vector3 axis, float angle)
		{
			return Quaternion.AngleAxis(angle, axis);
		}

		public static float axisSignedAngle(this Vector3 axis, Vector3 v1, Vector3 v2)
		{
			v1 = Vector3.ProjectOnPlane(v1, axis).normalized;
			v2 = Vector3.ProjectOnPlane(v2, axis).normalized;
			float angle = Vector3.Angle(v1, v2);
			float s = Vector3.Dot(axis, Vector3.Cross(v1, v2));
			return (s < 0) ? -angle : angle;
		}

		public static string desc(this Vector3 v)
		{
			return v.ToString("F2");
		}

		public static string ddesc(this Vector3 v, Part p)
		{
			string ret = v.desc();
			if (p && p.vessel.rootPart) {
				ret += " VSL" + v.Td(p.T(), p.vessel.rootPart.T()).desc();
			} else {
				ret += " (no vessel)";
			}
			return ret;
		}

		public static string pdesc(this Vector3 v, Part p)
		{
			string ret = v.desc();
			if (p && p.vessel.rootPart) {
				ret += " VSL" + v.Tp(p.T(), p.vessel.rootPart.T()).desc();
			} else {
				ret += " (no vessel)";
			}
			return ret;
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

