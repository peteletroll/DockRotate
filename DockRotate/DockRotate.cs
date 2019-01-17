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

		public const float CONTINUOUS = 999999f;

		public float maxvel = 1f;
		private float maxacc = 1f;

		private bool braking = false;

		private bool started = false, finished = false;

		public float elapsed = 0f;
		public double electricity = 0d;

		private const float accelTime = 2f;
		private const float stopMargin = 1.5f;

		protected abstract void onStart();
		protected abstract void onStep(float deltat);
		protected abstract void onStop();

		public float curBrakingSpace(float deltat = 0f)
		{
			float time = Mathf.Abs(vel) / maxacc + 2 * stopMargin * deltat;
			return vel / 2 * time;
		}

		public void advance(float deltat)
		{
			if (finished)
				return;

			isContinuous(); // normalizes tgt for continuous rotation

			maxacc = Mathf.Clamp(maxvel / accelTime, 1f, 180f);

			bool goingRightWay = (tgt - pos) * vel >= 0;
			float brakingSpace = Mathf.Abs(curBrakingSpace(deltat));

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
			elapsed += deltat;

			onStep(deltat);

			if (!finished && checkFinished(deltat))
				onStop();
		}

		public void brake()
		{
			tgt = pos + curBrakingSpace();
			braking = true;
		}

		public bool isBraking()
		{
			return braking;
		}

		public bool clampAngle()
		{
			if (pos < -3600f || pos > 3600f) {
				float newzero = 360f * Mathf.Floor(pos / 360f + 0.5f);
				// ModuleBaseRotate.lprint("clampAngle(): newzero " + newzero + " from pos " + pos);
				tgt -= newzero;
				pos -= newzero;
				return true;
			}
			return false;
		}

		public static bool isContinuous(ref float target)
		{
			if (Mathf.Abs(target) > CONTINUOUS / 2f) {
				target = Mathf.Sign(target) * CONTINUOUS;
				return true;
			}
			return false;
		}

		public bool isContinuous()
		{
			return isContinuous(ref tgt);
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
		public static implicit operator bool(RotationAnimation r)
		{
			return r != null;
		}

		private Part activePart, proxyPart;
		private Vector3 node, axis;
		private PartJoint joint;
		public bool smartAutoStruts = false;

		bool needStaticize = false;

		public ModuleBaseRotate controller = null;

		private Guid vesselId;

		public const float pitchAlterationRateMax = 0.1f;
		public static string soundFile = "DockRotate/DockRotateMotor";
		public AudioSource sound;
		public float soundVolume = 1f;
		public float pitchAlteration = 1f;

		public float electricityRate = 1f;
		public float rot0 = 0f;

		private struct RotJointInfo
		{
			public JointManager jm;
			public Vector3 localAxis, localNode;
			public Vector3 jointAxis, jointNode;
			public Vector3 connectedBodyAxis, connectedBodyNode;

			public void setRotation(float angle)
			{
				jm.setRotation(angle, localAxis, localNode);
			}

			public void staticizeRotation()
			{
				jm.staticizeRotation();
			}
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

			this.pos = pos;
			this.tgt = tgt;
			this.maxvel = maxvel;

			this.vel = 0;

			if (joint && joint.gameObject)
				lprint("new RotationAnimation.joint.gameObject = " + joint.gameObject);
			if (activePart && activePart.gameObject)
				lprint("new RotationAnimation.activePart.gameObject = " + activePart.gameObject);
			if (proxyPart && proxyPart.gameObject)
				lprint("new RotationAnimation.proxyPart.gameObject = " + proxyPart.gameObject);
		}

		private int changeCount(int delta)
		{
			VesselRotInfo vi = VesselRotInfo.getInfo(vesselId);
			int ret = vi.rotCount + delta;
			if (ret < 0) {
				lprint("WARNING: vesselRotCount[" + vesselId + "] = " + ret + " in changeCount(" + delta + ")");
				ret = 0;
			}
			// lprint("vesselRotCount is now " + ret);
			return vi.rotCount = ret;
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

			needStaticize = true;

			/*
			lprint(String.Format("{0}: started {1:F4}\u00b0 at {2}\u00b0/s",
				part.desc(), tgt, maxvel));
			*/
		}

		protected override void onStep(float deltat)
		{
			if (!needStaticize)
				lprint("*** WARNING *** needStaticizeJoint incoherency in OnStep(" + deltat + ")");

			for (int i = 0; i < joint.joints.Count; i++) {
				ConfigurableJoint j = joint.joints[i];
				if (!j)
					continue;
				RotJointInfo ji = rji[i];
				ji.setRotation(pos);
			}

			stepSound();

			if (controller) {
				float s = controller.speed();
				if (!Mathf.Approximately(s, maxvel)) {
					lprint(controller.part.desc() + ": speed change " + maxvel + " -> " + s);
					maxvel = s;
				}
			}

			if (deltat > 0f && electricityRate > 0f) {
				double el = activePart.RequestResource("ElectricCharge", (double) electricityRate * deltat);
				electricity += el;
				if (el <= 0d)
					abort("no electric charge");
			}
		}

		protected override void onStop()
		{
			// lprint("stop rot axis " + currentRotation(0).desc());

			stopSound();

			onStep(0);

			staticize();

			if (changeCount(-1) <= 0) {
				if (smartAutoStruts) {
					lprint("securing autostruts on vessel " + vesselId);
					joint.Host.vessel.secureAllAutoStruts();
				} else {
					// no action needed with IsJointUnlocked() logic
					// but IsJointUnlocked() logic is bugged now
					joint.Host.vessel.secureAllAutoStruts();
				}
			}
			lprint(activePart.desc() + ": rotation stopped, "
				+ electricity.ToString("F2") + " electricity");
		}

		public void startSound()
		{
			if (sound)
				return;

			try {
				AudioClip clip = GameDatabase.Instance.GetAudioClip(soundFile);
				if (!clip) {
					lprint("clip " + soundFile + " not found");
					return;
				}

				sound = activePart.gameObject.AddComponent<AudioSource>();
				sound.clip = clip;
				sound.volume = 0;
				sound.pitch = 0;
				sound.loop = true;
				sound.rolloffMode = AudioRolloffMode.Logarithmic;
				sound.spatialBlend = 1f;
				sound.minDistance = 1f;
				sound.maxDistance = 1000f;
				sound.playOnAwake = false;

				uint pa = (33u * (activePart.flightID ^ proxyPart.flightID)) % 10000u;
				pitchAlteration = 2f * pitchAlterationRateMax * (pa / 10000f)
					+ (1f - pitchAlterationRateMax);

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
				sound.volume = soundVolume * p * GameSettings.SHIP_VOLUME;
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

		public void forceStaticize()
		{
			lprint("forceStaticize() at " + tgt + "\u00b0");
			staticize();
		}

		public void staticize()
		{
			if (!needStaticize) {
				lprint("skipping repeated staticize()");
				return;
			}
			rotateOrgInfo(tgt);
			staticizeJoints();
			needStaticize = false;
		}

		private void staticizeJoints()
		{
			for (int i = 0; i < joint.joints.Count; i++) {
				ConfigurableJoint j = joint.joints[i];
				if (j) {
					RotJointInfo ji = rji[i];

					checkChanges("axis", ji.jm.axis0, j.axis);
					checkChanges("secAxis", ji.jm.secAxis0, j.secondaryAxis);
					checkChanges("anchor", ji.jm.anchor0, j.anchor);
					checkChanges("connAnchor", ji.jm.connAnchor0, j.connectedAnchor);

					// staticize joint rotation

					ji.staticizeRotation();

					// FIXME: this should be moved to JointManager
					Quaternion connectedBodyRot = ji.connectedBodyAxis.rotation(-tgt);
					j.connectedAnchor = connectedBodyRot * (j.connectedAnchor - ji.connectedBodyNode)
						+ ji.connectedBodyNode;
					j.targetPosition = ji.jm.tgtPos0;

					ji.jm.setup();
				}
			}
			lprint("staticizeJoints() complete");
		}

		private void checkChanges(string name, Vector3 v0, Vector3 v1)
		{
			float d = v0.magnitude + v1.magnitude;
			if (d < 1f)
				d = 1f;
			if ((v1 - v0).magnitude / d < 0.001f)
				return;
			lprint(name + " CHANGED: " + v0.desc() + " -> " + v1.desc());
		}

		private bool rotateOrgInfo(float angle)
		{
			if (joint != activePart.attachJoint) {
				lprint(activePart.desc() + ": skip staticize, same vessel joint");
				return false;
			}
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

		public void abort(string msg)
		{
			lprint("ABORTING: " + msg + ": " + pos + "\u00b0 -> " + tgt + "\u00b0 (" + (tgt - pos) + "\u00b0 left)");
			stopSound();
			tgt = pos;
			vel = 0;
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

		[UI_FloatEdit(
			minValue = 0f, maxValue = 360f,
			incrementSlide = 0.5f, incrementSmall = 5f, incrementLarge = 30f,
			sigFigs = 1, unit = "\u00b0"
		)]
		[KSPField(
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			guiName = "#DCKROT_rotation_step",
			guiUnits = "\u00b0"
		)]
		public float rotationStep = 15f;

		[UI_FloatEdit(
			minValue = 1, maxValue = 8f * 360f,
			incrementSlide = 1f, incrementSmall = 15f, incrementLarge = 180f,
			sigFigs = 0, unit = "\u00b0/s"
		)]
		[KSPField(
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			guiName = "#DCKROT_rotation_speed",
			guiUnits = "\u00b0/s"
		)]
		public float rotationSpeed = 5f;

		[UI_Toggle(affectSymCounterparts = UI_Scene.None)]
		[KSPField(
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			guiName = "#DCKROT_reverse_rotation"
		)]
		public bool reverseRotation = false;

		[KSPField(isPersistant = true)]
		public float soundVolume = 0.5f;

		[UI_Toggle()]
		[KSPField(
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			advancedTweakable = true,
			guiName = "#DCKROT_smart_autostruts"
		)]
		public bool smartAutoStruts = false;

		[KSPField(
			guiName = "#DCKROT_angle",
			guiActive = true,
			guiActiveEditor = false
		)]
		public string angleInfo;

#if DEBUG
		[KSPField(
			guiName = "#DCKROT_status",
			guiActive = false,
			guiActiveEditor = false
		)]
		public string nodeStatus = "";
#endif

#if DEBUG
		[UI_Toggle()]
		[KSPField(
			guiName = "Verbose Events",
			guiActive = true,
			guiActiveEditor = false,
			isPersistant = true
		)]
#endif
		public bool verboseEvents = false;

		[KSPAction(
			guiName = "#DCKROT_stop_rotation",
			requireFullControl = true
		)]
		public void StopRotation(KSPActionParam param)
		{
			ModuleBaseRotate tgt = actionTarget();
			if (tgt)
				tgt.doStopRotation();
		}

		[KSPEvent(
			guiName = "#DCKROT_stop_rotation",
			guiActive = false,
			guiActiveEditor = false,
			requireFullControl = true
		)]
		public void StopRotation()
		{
			doStopRotation();
		}

		[KSPAction(
			guiName = "#DCKROT_rotate_clockwise",
			requireFullControl = true
		)]
		public void RotateClockwise(KSPActionParam param)
		{
			ModuleBaseRotate tgt = actionTarget();
			if (tgt) {
				if (reverseActionRotationKey()) {
					tgt.doRotateCounterclockwise();
				} else {
					tgt.doRotateClockwise();
				}
			}
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_clockwise",
			guiActive = false,
			guiActiveEditor = false,
			requireFullControl = true
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
			if (tgt) {
				if (reverseActionRotationKey()) {
					tgt.doRotateClockwise();
				} else {
					tgt.doRotateCounterclockwise();
				}
			}
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_counterclockwise",
			guiActive = false,
			guiActiveEditor = false,
			requireFullControl = true
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
			guiActiveEditor = false,
			requireFullControl = true
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

		protected static Vector3 undefV3 = new Vector3(9.9f, 9.9f, 9.9f);

		public abstract void doRotateClockwise();

		public abstract void doRotateCounterclockwise();

		public abstract void doRotateToSnap();

		public abstract bool useSmartAutoStruts();

		protected abstract float rotationAngle(bool dynamic);

		protected abstract float dynamicDeltaAngle();

		protected abstract void dumpPart();

		protected abstract int countJoints();

		public void doStopRotation()
		{
			RotationAnimation r = currentRotation();
			if (r)
				r.brake();
		}

		protected bool reverseActionRotationKey()
		{
			return GameSettings.MODIFIER_KEY.GetKey();
		}

		protected bool brakeRotationKey()
		{
			return FlightGlobals.ActiveVessel == vessel
				&& GameSettings.MODIFIER_KEY.GetKey()
				&& GameSettings.BRAKES.GetKeyDown();
		}

		public bool IsJointUnlocked()
		{
			bool ret = currentRotation();
			// lprint(part.desc() + ".IsJointUnlocked() is " + ret);
			return ret;
		}

		private RotationAnimation _rotCur = null;
		protected RotationAnimation rotCur {
			get { return _rotCur; }
			set {
				bool wasRotating = _rotCur;
				_rotCur = value;
				bool isRotating = _rotCur;
				if (isRotating != wasRotating && !useSmartAutoStruts()) {
					lprint(part.desc() + " triggered CycleAllAutoStruts()");
					vessel.CycleAllAutoStrut();
				}
			}
		}

		protected bool onRails = true; // FIXME: obsoleted by setupDone?

		public PartJoint rotatingJoint;
		public Part activePart, proxyPart;
		public string nodeRole = "Init";
		protected Vector3 partNodePos; // node position, relative to part
		public Vector3 partNodeAxis; // node rotation axis, relative to part
		protected Vector3 partNodeUp; // node vector for measuring angle, relative to part

		// localized info cache
		protected string cached_moduleDisplayName = "";
		protected string cached_info = "";

		[KSPField(isPersistant = true)]
		public Vector3 frozenRotation = Vector3.zero;

		[KSPField(isPersistant = true)]
		public uint frozenRotationControllerID = 0;

		[KSPField(isPersistant = true)]
		public float electricityRate = 1f;

		protected abstract ModuleBaseRotate controller(uint id);

		protected bool setupDone = false;
		protected abstract void setup();

		private void doSetup()
		{
			if (onRails || !part || !vessel) {
				lprint("WARNING: doSetup() called at a bad time");
				return;
			}

			try {
				setupGuiActive();
				setup();
			} catch (Exception e) {
				string sep = new string('-', 80);
				lprint(sep);
				lprint("Exception during setup:\n" + e.StackTrace);
				lprint(sep);
			}

			setupDone = true;
		}

		public void OnVesselGoOnRails(Vessel v)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": OnVesselGoOnRails(" + v.persistentId + ") [" + vessel.persistentId + "]");
			if (v != vessel)
				return;
			freezeCurrentRotation("go on rails", false);
			// VesselRotInfo.resetInfo(vessel.id);
			onRails = true;
			setupDone = false;
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": OnVesselGoOffRails(" + v.persistentId + ") [" + vessel.persistentId + "]");
			if (v != vessel)
				return;
			VesselRotInfo.resetInfo(vessel.id);
			onRails = false;
			setupDone = false;
			// start speed always 0 when going off rails
			frozenRotation[2] = 0f;
			doSetup();
		}

		public void OnCameraChange(CameraManager.CameraMode mode)
		{
			Camera camera = CameraManager.GetCurrentCamera();
			if (verboseEvents && camera) {
				lprint(part.desc() + ": OnCameraChange(" + mode + "): " + camera.desc());
				Camera[] cameras = Camera.allCameras;
				for (int i = 0; i < cameras.Length; i++)
					lprint("camera[" + i + "] = " + cameras[i].desc());
			}
		}

		private void RightBeforeStructureChangeJointUpdate(Vessel v)
		{
			if (v != vessel)
				return;
			if (verboseEvents && activePart)
				lprint(part.desc() + ": RightBeforeStructureChangeJointUpdate(): ORG " + activePart.descOrg());
			if (rotCur) {
				lprint(part.desc() + ": RightBeforeStructureChangeJointUpdate(): calling staticizeJoints()");
				rotCur.staticize();
			}
		}

		public void RightBeforeStructureChangeIds(uint id1, uint id2)
		{
			if (!vessel)
				return;
			uint id = vessel.persistentId;
			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeStructureChangeIds("
					+ id1 + ", " + id2 + ") [" + id + "]");
			if (id1 == id || id2 == id)
				RightBeforeStructureChange();
		}

		public void RightBeforeStructureChangeAction(GameEvents.FromToAction<Part, Part> action)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeStructureChangeAction("
					+ action.from.desc() + ", " + action.to.desc() + ")");
			if (action.from.vessel == vessel || action.to.vessel == vessel)
				RightBeforeStructureChange();
		}

		public void RightBeforeStructureChangePart(Part p)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeStructureChangePart(" + part.desc() + ")");
			if (p.vessel == vessel)
				RightBeforeStructureChange();
		}

		public void RightBeforeStructureChange()
		{
			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeStructureChange()");
			freezeCurrentRotation("structure change", true);
		}

		public void RightAfterStructureChangeAction(GameEvents.FromToAction<Part, Part> action)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightAfterStructureChangeAction("
					+ action.from.desc() + ", " + action.to.desc() + ")");
			if (action.from.vessel == vessel || action.to.vessel == vessel)
				RightAfterStructureChange();
		}

		public void RightAfterStructureChangePart(Part p)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightAfterStructureChangePart(" + p.desc() + ")");
			if (p.vessel == vessel)
				RightAfterStructureChange();
		}

		private void RightAfterStructureChange()
		{
			if (verboseEvents)
				lprint(part.desc() + ": RightAfterStructureChange()");
			doSetup();
		}

		private void RightAfterSameVesselDock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightAfterSameVesselDock("
					+ action.from.part.desc() + ", " + action.to.part.desc() + ")");
			if (action.to.part == part || action.from.part == part)
				doSetup();
		}

		private void RightAfterSameVesselUndock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightAfterSameVesselUndock("
					+ action.from.part.desc() + ", " + action.to.part.desc() + ")");
			if (action.to.part == part || action.from.part == part)
				doSetup();
		}

		public override void OnAwake()
		{
			onRails = true;
			setupDone = false;

			base.OnAwake();

			GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);

			GameEvents.OnCameraChange.Add(OnCameraChange);

			GameEvents.onActiveJointNeedUpdate.Add(RightBeforeStructureChangeJointUpdate);

			GameEvents.onPartCouple.Add(RightBeforeStructureChangeAction);
			GameEvents.onPartCoupleComplete.Add(RightAfterStructureChangeAction);
			GameEvents.onPartDeCouple.Add(RightBeforeStructureChangePart);
			GameEvents.onPartDeCoupleComplete.Add(RightAfterStructureChangePart);

			GameEvents.onVesselDocking.Add(RightBeforeStructureChangeIds);
			GameEvents.onDockingComplete.Add(RightAfterStructureChangeAction);
			GameEvents.onPartUndock.Add(RightBeforeStructureChangePart);
			GameEvents.onPartUndockComplete.Add(RightAfterStructureChangePart);

			GameEvents.onSameVesselDock.Add(RightAfterSameVesselDock);
			GameEvents.onSameVesselUndock.Add(RightAfterSameVesselUndock);
		}

		public virtual void OnDestroy()
		{
			GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);

			GameEvents.OnCameraChange.Remove(OnCameraChange);

			GameEvents.onActiveJointNeedUpdate.Remove(RightBeforeStructureChangeJointUpdate);

			GameEvents.onPartCouple.Remove(RightBeforeStructureChangeAction);
			GameEvents.onPartCoupleComplete.Remove(RightAfterStructureChangeAction);
			GameEvents.onPartDeCouple.Remove(RightBeforeStructureChangePart);
			GameEvents.onPartDeCoupleComplete.Remove(RightAfterStructureChangePart);

			GameEvents.onVesselDocking.Remove(RightBeforeStructureChangeIds);
			GameEvents.onDockingComplete.Remove(RightAfterStructureChangeAction);
			GameEvents.onPartUndock.Remove(RightBeforeStructureChangePart);
			GameEvents.onPartUndockComplete.Remove(RightAfterStructureChangePart);

			GameEvents.onSameVesselDock.Remove(RightAfterSameVesselDock);
			GameEvents.onSameVesselUndock.Remove(RightAfterSameVesselUndock);
		}

		protected static string[] guiList = {
			"nodeRole",
			"angleInfo",
			"rotationStep",
			"rotationSpeed",
			"reverseRotation",
			"RotateClockwise",
			"RotateCounterclockwise",
			"RotateToSnap"
		};

		private BaseField[] fld;
		private BaseEvent[] evt;

		protected void setupGuiActive()
		{
			fld = null;
			evt = null;

			List<BaseField> fl = new List<BaseField>();
			List<BaseEvent> el = new List<BaseEvent>();

			for (int i = 0; i < guiList.Length; i++) {
				string n = guiList[i];
				BaseField f = Fields[n];
				if (f != null)
					fl.Add(f);
				BaseEvent e = Events[n];
				if (e != null)
					el.Add(e);
			}

			fld = fl.ToArray();
			evt = el.ToArray();

			// lprint(part.desc() + ": " + fld.Length + " fields, " + evt.Length + " events");
		}

		private void checkGuiActive()
		{
			bool newGuiActive = FlightGlobals.ActiveVessel == vessel && canStartRotation();

			if (fld != null)
				for (int i = 0; i < fld.Length; i++)
					if (fld[i] != null)
						fld[i].guiActive = newGuiActive;

			if (evt != null)
				for (int i = 0; i < evt.Length; i++)
					if (evt[i] != null)
						evt[i].guiActive = newGuiActive;
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);

			setupGuiActive();

			checkGuiActive();
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (MapView.MapIsEnabled)
				return;

			bool guiActive = canStartRotation();
			RotationAnimation cr = currentRotation();

#if DEBUG
			nodeStatus = "";
			int nJoints = countJoints();
			nodeStatus = nodeRole + " [" + nJoints + "]";
			if (cr)
				nodeStatus += " " + cr.pos + "\u00b0 -> "+ cr.tgt + "\u00b0";
			Fields["nodeStatus"].guiActive = guiActive && nodeStatus.Length > 0;
#endif

			if (cr) {
				angleInfo = String.Format("{0:+0.00;-0.00;0.00}\u00b0 ({1:+0.00;-0.00;0.00}\u00b0/s){2}",
					rotationAngle(true), cr.vel,
					(cr.controller == this ? " CTL" : ""));
			} else {
				angleInfo = String.Format("{0:+0.00;-0.00;0.00}\u00b0 ({1:+0.0000;-0.0000;0.0000}\u00b0\u0394)",
					rotationAngle(false), dynamicDeltaAngle());
			}

			Events["StopRotation"].guiActive = cr;

			checkGuiActive();

#if DEBUG
			Events["ToggleAutoStrutDisplay"].guiName = PhysicsGlobals.AutoStrutDisplay ? "Hide Autostruts" : "Show Autostruts";
#endif
		}

		protected virtual ModuleBaseRotate actionTarget()
		{
			return canStartRotation() ? this : null;
		}

		protected virtual bool canStartRotation()
		{
			return setupDone
				&& rotationEnabled
				&& vessel
				&& vessel.CurrentControlLevel == Vessel.ControlLevel.FULL;
		}

		public float step()
		{
			float s = rotationStep;
			if (s < 0.1f)
				s = SmoothMotion.CONTINUOUS;
			if (reverseRotation)
				s = -s;
			return s;
		}

		public float speed()
		{
			float s = Mathf.Abs(rotationSpeed);
			return s >= 1f ? s : 1f;
		}

		protected bool enqueueRotation(Vector3 frozen)
		{
			return enqueueRotation(frozen[0], frozen[1], frozen[2]);
		}

		protected virtual bool enqueueRotation(float angle, float speed, float startSpeed = 0f)
		{
			if (!rotatingJoint)
				return false;

			if (speed < 0.1f)
				return false;

			string action = "none";
			bool showlog = true;
			if (rotCur) {
				bool trace = false;
				if (rotCur.isBraking()) {
					lprint(part.desc() + ": enqueueRotation() canceled, braking");
					return false;
				}
				rotCur.controller = this;
				rotCur.maxvel = speed;
				action = "updated";
				if (SmoothMotion.isContinuous(ref angle)) {
					if (rotCur.isContinuous() && angle * rotCur.tgt > 0f)
						showlog = false; // already continuous the right way
					if (trace && showlog)
						lprint("MERGE CONTINUOUS " + angle + " -> " + rotCur.tgt);
					rotCur.tgt = angle;
					updateFrozenRotation("MERGECONT");
				} else {
					if (trace)
						lprint("MERGE LIMITED " + angle + " -> " + rotCur.rot0 + " + " + rotCur.tgt);
					if (rotCur.isContinuous()) {
						if (trace)
							lprint("MERGE INTO CONTINUOUS");
						rotCur.tgt = rotCur.pos + rotCur.curBrakingSpace() + angle;
					} else {
						if (trace)
							lprint("MERGE INTO LIMITED");
						rotCur.tgt = rotCur.tgt + angle;
					}
					if (trace)
						lprint("MERGED: POS " + rotCur.pos +" TGT " + rotCur.tgt);
					updateFrozenRotation("MERGELIM");
				}
			} else {
				rotCur = new RotationAnimation(activePart, partNodePos, partNodeAxis, rotatingJoint, 0, angle, speed);
				rotCur.rot0 = rotationAngle(false);
				rotCur.controller = this;
				rotCur.electricityRate = electricityRate;
				rotCur.soundVolume = soundVolume;
				rotCur.vel = startSpeed;
				rotCur.smartAutoStruts = useSmartAutoStruts();
				action = "added";

			}
			if (showlog) {
				lprint(String.Format("{0}: enqueueRotation({1}, {2:F4}\u00b0, {3}\u00b0/s, {4}\u00b0/s), {5}",
					activePart.desc(), partNodeAxis.desc(), rotCur.tgt, rotCur.maxvel, rotCur.vel, action));
				lprint("ORG0 " + activePart.descOrg());
			}
			return true;
		}

		protected float angleToSnap(float snap)
		{
			snap = Mathf.Abs(snap);
			if (snap < 0.5f)
				return 0f;
			float a = !rotCur ? rotationAngle(false) :
				rotCur.isContinuous() ? rotCur.rot0 + rotCur.pos :
				rotCur.rot0 + rotCur.tgt;
			if (float.IsNaN(a))
				return 0f;
			float f = snap * Mathf.Floor(a / snap + 0.5f);
			return f - a;
		}

		protected bool enqueueRotationToSnap(float snap, float speed)
		{
			if (snap < 0.1f)
				snap = 15f;
			return enqueueRotation(angleToSnap(snap), speed);
		}

		protected virtual void advanceRotation(float deltat)
		{
			if (!rotCur)
				return;

			if (rotCur.done()) {
				rotCur = null;
				return;
			}

			rotCur.advance(deltat);
		}

		protected void freezeCurrentRotation(string msg, bool keepSpeed)
		{
			if (rotCur) {
				rotCur.isContinuous();
				float angle = rotCur.tgt - rotCur.pos;
				enqueueFrozenRotation(angle, rotCur.maxvel, keepSpeed ? rotCur.vel : 0f);
				rotCur.abort(msg);
				rotCur.forceStaticize();
				rotCur = null;
			}
		}

		protected abstract RotationAnimation currentRotation();

		protected void checkFrozenRotation()
		{
			if (!setupDone)
				return;

			if (!Mathf.Approximately(frozenRotation[0], 0f) && !currentRotation()) {
				enqueueRotation(frozenRotation);
				RotationAnimation cr = currentRotation();
				if (cr)
					cr.controller = controller(frozenRotationControllerID);
			}

			updateFrozenRotation("CHECK");
		}

		protected void updateFrozenRotation(string context)
		{
			Vector3 prevRot = frozenRotation;
			uint prevID = frozenRotationControllerID;
			if (rotCur && rotCur.isContinuous()) {
				frozenRotation.Set(rotCur.tgt, rotCur.maxvel, 0f);
				frozenRotationControllerID = (rotCur && rotCur.controller) ? rotCur.controller.part.flightID : 0;
			} else {
				frozenRotation = Vector3.zero;
				frozenRotationControllerID = 0;
			}
			if (frozenRotation != prevRot || frozenRotationControllerID != prevID)
				lprint(part.desc() + ": updateFrozenRotation("
					+ context + "): "
					+ prevRot + "@" + prevID
					+ " -> " + frozenRotation + "@" + frozenRotationControllerID);
		}

		protected void enqueueFrozenRotation(float angle, float speed, float startSpeed = 0f)
		{
			Vector3 prev = frozenRotation;
			angle += frozenRotation[0];
			SmoothMotion.isContinuous(ref angle);
			frozenRotation.Set(angle, speed, startSpeed);
			lprint(part.desc() + ": enqueueFrozenRotation(): " + prev + " -> " + frozenRotation);
		}

		public void FixedUpdate()
		{
			if (!setupDone || HighLogic.LoadedScene != GameScenes.FLIGHT)
				return;

			checkFrozenRotation();

			if (rotCur) {
				rotCur.clampAngle();
				if (brakeRotationKey())
					rotCur.brake();
				advanceRotation(Time.fixedDeltaTime);
				updateFrozenRotation("FIXED");
			}
		}

		/******** Debugging stuff ********/

		public static bool lprint(string msg)
		{
			print("[DockRotate:" + Time.frameCount + "]: " + msg);
			return true;
		}
	}

	public class ModuleNodeRotate: ModuleBaseRotate
	{
		[KSPField(isPersistant = true)]
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
			if (!activePart || !proxyPart)
				return float.NaN;

			Vector3 a = partNodeAxis;
			Vector3 vd = otherPartUp.Td(proxyPart.T(), activePart.T());
			Vector3 vs = otherPartUp.STd(proxyPart, activePart);
			return a.axisSignedAngle(vs, vd);
		}

		public override string GetModuleDisplayName()
		{
			if (cached_moduleDisplayName.Length <= 0)
				cached_moduleDisplayName = Localizer.Format("#DCKROT_node_displayname");
			return cached_moduleDisplayName;
		}

		public override string GetInfo()
		{
			if (cached_info.Length <= 0)
				cached_info = Localizer.Format("#DCKROT_node_info", rotatingNodeName);
			return cached_info;
		}

		protected override void setup()
		{
			rotatingJoint = null;
			activePart = proxyPart = null;
			partNodePos = partNodeAxis = partNodeUp = otherPartUp = undefV3;

			nodeRole = "None";

			if (part.FindModuleImplementing<ModuleDockRotate>())
				return;

			if (!part.hasPhysics()) {
				lprint(part.desc() + ": physicsless, NodeRotate disabled");
				return;
			}

			rotatingNode = part.FindAttachNode(rotatingNodeName);
			if (rotatingNode == null) {
				lprint(part.desc() + " has no node named \"" + rotatingNodeName + "\"");
				AttachNode[] nodes = part.FindAttachNodes("");
				string nodeList = part.desc() + " available nodes:";
				for (int i = 0; i < nodes.Length; i++)
					nodeList += " \"" + nodes[i].id + "\"";
				lprint(nodeList);
				return;
			}

			partNodePos = rotatingNode.position;
			partNodeAxis = rotatingNode.orientation;
			partNodeUp = part.up(partNodeAxis);

			Part other = rotatingNode.attachedPart;
			if (!other)
				return;

			other.forcePhysics();
			if (!other.rb) {
				lprint(part.desc() + ": other part " + other.desc() + " has no Rigidbody");
				// return;
			}

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

			if (activePart)
				rotatingJoint = activePart.attachJoint;
			if (proxyPart)
				otherPartUp = proxyPart.up(partNodeAxis.STd(part, proxyPart));

			if (verboseEvents && rotatingJoint) {
				lprint(part.desc()
					+ ": on "
					+ (activePart == part ? "itself" : activePart.desc()));
			}
		}

		protected override ModuleBaseRotate controller(uint id)
		{
			return part.flightID == id ? this : null;
		}

		public override void doRotateClockwise()
		{
			enqueueRotation(step(), speed());
		}

		public override void doRotateCounterclockwise()
		{
			enqueueRotation(-step(), speed());
		}

		public override void doRotateToSnap()
		{
			enqueueRotationToSnap(rotationStep, speed());
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
			return rotatingJoint && base.canStartRotation();
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
		/*

			the active module of the couple is the farthest from the root part
			the proxy module of the couple is the closest to the root part

			docking node states:

			* PreAttached
			* Docked (docker/same vessel/dockee) - (docker) and (same vessel) are coupled with (dockee)
			* Ready
			* Disengage
			* Acquire
			* Acquire (dockee)

		*/

		private ModuleDockingNode dockingNode;
		public ModuleDockRotate activeRotationModule;
		public ModuleDockRotate proxyRotationModule;
		public ModuleDockRotate otherRotationModule;

#if DEBUG
		[KSPEvent(
			guiName = "#DCKROT_redock",
			guiActive = false,
			guiActiveEditor = false,
			requireFullControl = true
		)]
		public void ReDock()
		{
			if (dockingNode)
				dockingNode.state = "Ready";
		}
#endif

		public override string GetModuleDisplayName()
		{
			if (cached_moduleDisplayName.Length <= 0)
				cached_moduleDisplayName = Localizer.Format("#DCKROT_port_displayname");
			return cached_moduleDisplayName;
		}

		public override string GetInfo()
		{
			if (cached_info.Length <= 0)
				cached_info = Localizer.Format("#DCKROT_port_info");
			return cached_info;
		}

		private int lastBasicSetupFrame = -1;

		private void basicSetup()
		{
			int now = Time.frameCount;
			if (lastBasicSetupFrame == now) {
				if (false && verboseEvents)
					lprint(part.desc() + ": skip repeated basicSetup()");
				return;
			}
			lastBasicSetupFrame = now;

			activePart = proxyPart = null;
			rotatingJoint = null;
			activeRotationModule = proxyRotationModule = otherRotationModule = null;
			nodeRole = "None";
			partNodePos = partNodeAxis = partNodeUp = undefV3;
#if DEBUG
			nodeStatus = "";
#endif

			dockingNode = part.FindModuleImplementing<ModuleDockingNode>();

			if (dockingNode) {
				partNodePos = Vector3.zero.Tp(dockingNode.T(), part.T());
				partNodeAxis = Vector3.forward.Td(dockingNode.T(), part.T());
				partNodeUp = Vector3.up.Td(dockingNode.T(), part.T());
			}
		}

		protected override void setup()
		{
			basicSetup();

			if (!dockingNode)
				return;

			PartJoint svj = dockingNode.sameVesselDockJoint;
			if (svj) {
				ModuleDockRotate otherModule = svj.Target.FindModuleImplementing<ModuleDockRotate>();
				if (otherModule) {
					activeRotationModule = this;
					proxyRotationModule = otherModule;
					rotatingJoint = svj;
					nodeRole = "ActiveSame";
				}
			} else if (isDockedToParent(false)) {
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

			if (activeRotationModule == this) {
				proxyRotationModule.nodeRole = svj ? "ProxySame" : "Proxy";
				proxyRotationModule.activeRotationModule = activeRotationModule;
				proxyRotationModule.activePart = activePart;
				proxyRotationModule.proxyRotationModule = proxyRotationModule;
				proxyRotationModule.proxyPart = proxyPart;
				proxyPart = proxyRotationModule.part;
				otherRotationModule = proxyRotationModule;
				proxyRotationModule.otherRotationModule = activeRotationModule;
				if (verboseEvents)
					lprint(activeRotationModule.part.desc()
						+ ": on " + proxyRotationModule.part.desc());
			}

			if (dockingNode.snapRotation && dockingNode.snapOffset > 0f
				&& activeRotationModule == this
				&& (rotationEnabled || proxyRotationModule.rotationEnabled)) {
				enqueueFrozenRotation(angleToSnap(dockingNode.snapOffset), rotationSpeed);
			}
		}

		protected override ModuleBaseRotate controller(uint id)
		{
			if (activeRotationModule && activeRotationModule.part.flightID == id)
				return activeRotationModule;
			if (proxyRotationModule && proxyRotationModule.part.flightID == id)
				return proxyRotationModule;
			return null;
		}

		private bool isDockedToParent(bool verbose) // must be used only after basicSetup()
		{
			if (verbose)
				lprint(part.desc() + ": isDockedToParent()");

			if (!part || !part.parent) {
				if (verbose)
					lprint(part.desc() + ": isDockedToParent() finds no parent");
				return false;
			}

			ModuleDockingNode parentNode = part.parent.FindModuleImplementing<ModuleDockingNode>();
			ModuleDockRotate parentRotate = part.parent.FindModuleImplementing<ModuleDockRotate>();
			if (parentRotate)
				parentRotate.basicSetup();

			if (!dockingNode || !parentNode || !parentRotate) {
				if (verbose)
					lprint(part.desc() + ": isDockedToParent() has missing modules");
				return false;
			}

			float nodeDist = (partNodePos - parentRotate.partNodePos.Tp(parentRotate.part.T(), part.T())).magnitude;
			float nodeAngle = Vector3.Angle(partNodeAxis, Vector3.back.Td(parentNode.T(), part.parent.T()).STd(part.parent, part));
			if (verbose)
				lprint(part.desc() + ": isDockedToParent(): dist " + nodeDist + ", angle " + nodeAngle
					+ ", types " + dockingNode.allTypes() + "/" + parentNode.allTypes());

			bool ret = dockingNode.nodeTypes.Overlaps(parentNode.nodeTypes)
				&& nodeDist < 1f && nodeAngle < 5f;

			if (verbose)
				lprint(part.desc() + ": isDockedToParent() returns " + ret);

			return ret;
		}

		protected override bool canStartRotation()
		{
			return activeRotationModule && base.canStartRotation();
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

		protected override bool enqueueRotation(float angle, float speed, float startSpeed = 0f)
		{
			bool ret = false;
			if (activeRotationModule == this) {
				ret = base.enqueueRotation(angle, speed, startSpeed);
			} else if (activeRotationModule && activeRotationModule.activeRotationModule == activeRotationModule) {
				ret = activeRotationModule.enqueueRotation(angle, speed, startSpeed);
			} else {
				lprint("enqueueRotation() called on wrong module, ignoring, active part "
					+ (activeRotationModule ? activeRotationModule.part.desc() : "null"));
			}
			if (ret) {
				RotationAnimation cr = currentRotation();
				if (cr)
					cr.controller = this;
			}
			return ret;
		}

		protected override void advanceRotation(float deltat)
		{
			base.advanceRotation(deltat);

			if (activeRotationModule != this) {
				lprint("advanceRotation() called on wrong module, aborting");
				rotCur = null;
			}
		}

		public override void doRotateClockwise()
		{
			if (!activeRotationModule)
				return;
			enqueueRotation(step(), speed());
		}

		public override void doRotateCounterclockwise()
		{
			if (!activeRotationModule)
				return;
			enqueueRotation(-step(), speed());
		}

		public override void doRotateToSnap()
		{
			if (!activeRotationModule)
				return;
			float snap = rotationStep;
			if (snap < 0.1f && otherRotationModule)
				snap = otherRotationModule.rotationStep;
			activeRotationModule.enqueueRotationToSnap(snap, speed());
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

		public override void OnUpdate()
		{
			base.OnUpdate();
			BaseEvent ev = Events["ReDock"];
			if (ev != null) {
				ev.guiActive = dockingNode
					&& dockingNode.state == "Disengage";
			}
		}

		/******** Debugging stuff ********/

		protected override void dumpPart()
		{
			lprint("--- DUMP " + part.desc() + " ---");
			lprint("rotPart: " + activePart.desc());
			lprint("role: " + nodeRole);
#if DEBUG
			lprint("status: " + nodeStatus);
#endif
			lprint("org: " + part.orgPos.desc() + ", " + part.orgRot.desc());

			if (dockingNode) {
				lprint("state: " + dockingNode.state);

				lprint("types: " + dockingNode.allTypes());

				ModuleDockingNode other = dockingNode.otherNode();
				lprint("other: " + (other ? other.part.desc() : "none"));

				lprint("partNodeAxisV: " + partNodeAxis.STd(part, vessel.rootPart).desc());
				lprint("GetFwdVector(): " + dockingNode.GetFwdVector().desc());
				lprint("nodeTransform: " + dockingNode.nodeTransform.desc(8));
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
		// right is j.axis;
		// up is j.secondaryAxis;
		// anchor, axis and secondaryAxis are defined in local space.

		private ConfigurableJoint joint;
		private Quaternion localToJoint, jointToLocal;
		public Quaternion tgtRot0;
		public Vector3 tgtPos0;
		public Vector3 axis0, secAxis0, anchor0, connAnchor0;

		public void setup(ConfigurableJoint joint)
		{
			this.joint = joint;
			setup();
		}

		public void setup()
		{
			// the jointToLocal rotation turns Vector3.right (1, 0, 0) to axis
			// and Vector3.up (0, 1, 0) to secondaryAxis

			// jointToLocal * v means:
			// vector v expressed in joint space
			// result is same vector in local space

			// source: https://answers.unity.com/questions/278147/how-to-use-target-rotation-on-a-configurable-joint.html

			Vector3 right = joint.axis.normalized;
			Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
			Vector3 up = Vector3.Cross(forward, right).normalized;
			jointToLocal = Quaternion.LookRotation(forward, up);

			localToJoint = jointToLocal.inverse();

			tgtPos0 = joint.targetPosition;
			if (tgtPos0 != Vector3.zero)
				Debug.Log("JointManager: tgtPos0 = " + tgtPos0.desc());

			tgtRot0 = joint.targetRotation;
			if (tgtRot0 != Quaternion.identity)
				Debug.Log("JointManager: tgtRot0 = " + tgtRot0.desc());

			axis0 = joint.axis;
			secAxis0 = joint.secondaryAxis;
			anchor0 = joint.anchor;
			connAnchor0 = joint.connectedAnchor;
		}

		public void setRotation(float angle, Vector3 axis, Vector3 node)
		// axis and node are in local space
		{
			Quaternion jointRotation = L2Jr(axis.rotation(angle));
			Vector3 jointNode = L2Jp(node);
			joint.targetRotation = tgtRot0 * jointRotation;
			joint.targetPosition = jointRotation * (tgtPos0 - jointNode) + jointNode;
		}

		// FIXME: this must include position staticization
		public void staticizeRotation()
		{
			Quaternion localRotation = J2Lr(tgtRot0.inverse() * joint.targetRotation);
			joint.axis = localRotation * joint.axis;
			joint.secondaryAxis = localRotation * joint.secondaryAxis;
			joint.targetRotation = tgtRot0;
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
			return localToJoint * (v - joint.anchor);
		}

		public Vector3 J2Lp(Vector3 v)
		{
			return jointToLocal * v + joint.anchor;
		}

		public Quaternion L2Jr(Quaternion r)
		{
			return localToJoint * r * jointToLocal;
		}

		public Quaternion J2Lr(Quaternion r)
		{
			return jointToLocal * r * localToJoint;
		}
	}

	public static class SmartAutostruts
	{
		private static bool lprint(string msg)
		{
			Debug.Log("[SmartAutostruts:" + Time.frameCount + "]: " + msg);
			return true;
		}

		/******** PartSet utilities ********/

		public class PartSet: Dictionary<uint, Part>
		{
			private Part[] partArray = null;
			private ModuleGrappleNode[] klawArray = null;

			public void add(Part part)
			{
				partArray = null;
				klawArray = null;
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
				foreach (KeyValuePair<uint, Part> i in this)
					ret.Add(i.Value);
				return partArray = ret.ToArray();
			}

			public ModuleGrappleNode[] klaws()
			{
				if (klawArray != null)
					return klawArray;
				Part[] p = parts();
				List<ModuleGrappleNode> ret = new List<ModuleGrappleNode>();
				for (int i = 0; i < p.Length; i++) {
					ModuleGrappleNode k = p[i].getKlaw();
					if (k)
						ret.Add(k);
				}
				return klawArray = ret.ToArray();
			}

			public void dump()
			{
				Part[] p = parts();
				for (int i = 0; i < p.Length; i++)
					ModuleBaseRotate.lprint("rotPart " + p[i].desc());
			}
		}

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

		/******** Object.FindObjectsOfType<PartJoint>() cache ********/

		private static PartJoint[] cached_allJoints = null;
		private static int cached_allJoints_frame = 0;

		private static PartJoint[] getAllJoints()
		{
			if (cached_allJoints != null && cached_allJoints_frame == Time.frameCount)
				return cached_allJoints;
			cached_allJoints = UnityEngine.Object.FindObjectsOfType<PartJoint>();
			cached_allJoints_frame = Time.frameCount;
			return cached_allJoints;
		}

		/******** Vessel Autostruts cache ********/

		private static PartJoint[] cached_allAutostrutJoints = null;
		private static Vessel cached_allAutostrutJoints_vessel = null;
		private static int cached_allAutostrutJoints_frame = 0;

		private static PartJoint[] getAllAutostrutJoints(Vessel vessel)
		{
			if (cached_allAutostrutJoints != null && cached_allAutostrutJoints_vessel == vessel && cached_allAutostrutJoints_frame == Time.frameCount)
				return cached_allAutostrutJoints;

			List<ModuleDockingNode> allDockingNodes = vessel.FindPartModulesImplementing<ModuleDockingNode>();
			List<ModuleDockingNode> sameVesselDockingNodes = new List<ModuleDockingNode>();
			for (int i = 0; i < allDockingNodes.Count; i++)
				if (allDockingNodes[i].sameVesselDockJoint)
					sameVesselDockingNodes.Add(allDockingNodes[i]);

			PartJoint[] allJoints = getAllJoints();
			List<PartJoint> allAutostrutJoints = new List<PartJoint>();
			for (int ii = 0; ii < allJoints.Length; ii++) {
				PartJoint j = allJoints[ii];
				if (!j)
					continue;
				if (!j.Host || j.Host.vessel != vessel)
					continue;
				if (!j.Target || j.Target.vessel != vessel)
					continue;
				if (j == j.Host.attachJoint)
					continue;
				if (j == j.Target.attachJoint)
					continue;

				bool isSameVesselDockingJoint = false;
				for (int i = 0; !isSameVesselDockingJoint && i < sameVesselDockingNodes.Count; i++)
					if (j == sameVesselDockingNodes[i].sameVesselDockJoint)
						isSameVesselDockingJoint = true;
				if (isSameVesselDockingJoint)
					continue;

				allAutostrutJoints.Add(j);
				lprint("Autostrut [" + allAutostrutJoints.Count + "] " + j.desc());
			}

			cached_allAutostrutJoints = allAutostrutJoints.ToArray();
			cached_allAutostrutJoints_vessel = vessel;
			cached_allAutostrutJoints_frame = Time.frameCount;
			return cached_allAutostrutJoints;
		}

		/******** public interface ********/

		public static void releaseCrossAutoStruts(this Part part)
		{
			PartSet rotParts = part.allPartsFromHere();

			PartJoint[] allAutostrutJoints = getAllAutostrutJoints(part.vessel);

			int count = 0;
			for (int ii = 0; ii < allAutostrutJoints.Length; ii++) {
				PartJoint j = allAutostrutJoints[ii];
				if (!j)
					continue;
				if (rotParts.contains(j.Host) == rotParts.contains(j.Target))
					continue;

				lprint(part.desc() + ": releasing [" + ++count + "] " + j.desc());
				j.DestroyJoint();
			}
		}
	}

	public static class DynamicReferenceChanges
	{
		public static string desc(this Transform t, int parents = 0)
		{
			if (!t)
				return "world";
			string ret = t.name + ":" + t.GetInstanceID() + ":"
				+ t.localRotation.desc() + "@" + t.localPosition.desc()
				+ "/"
				+ t.rotation.desc() + "@" + t.position.desc();
			if (parents > 0)
				ret += "\n\t< " + t.parent.desc(parents - 1);
			return ret;
		}

		public static Vector3 Td(this Vector3 v, Transform from, Transform to)
		{
			if (from)
				v = from.TransformDirection(v);
			if (to)
				v = to.InverseTransformDirection(v);
			return v;
		}

		public static Vector3 Tp(this Vector3 v, Transform from, Transform to)
		{
			if (from)
				v = from.TransformPoint(v);
			if (to)
				v = to.InverseTransformPoint(v);
			return v;
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
	}

	public static class StaticReferenceChanges
	{
		public static string descOrg(this Part p)
		{
			return p ? p.orgRot.desc() + "@" + p.orgPos.desc() : "null-part";
		}

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

	public static class Extensions
	{
		private static bool lprint(string msg)
		{
			return ModuleBaseRotate.lprint(msg);
		}

		/******** Camera utilities ********/

		public static string desc(this Camera c)
		{
			if (!c)
				return "null";
			return c.name + "(" + c.cameraType + ")";
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

		public static string desc(this Part part)
		{
			if (!part)
				return "<null>";
			ModuleBaseRotate mbr = part.FindModuleImplementing<ModuleBaseRotate>();
			return part.name + "_" + part.flightID
				+ (mbr ? "_" + mbr.nodeRole : "");
		}

		public static Vector3 up(this Part part, Vector3 axis)
		{
			Vector3 up1 = Vector3.ProjectOnPlane(Vector3.up, axis);
			Vector3 up2 = Vector3.ProjectOnPlane(Vector3.forward, axis);
			return (up1.magnitude > up2.magnitude ? up1 : up2).normalized;
		}

		public static ModuleGrappleNode getKlaw(this Part part)
		{
			return part ? part.FindModuleImplementing<ModuleGrappleNode>() : null;
		}

		/******** Physics Activation utilities ********/

		public static bool hasPhysics(this Part part)
		{
			bool ret = (part.physicalSignificance == Part.PhysicalSignificance.FULL);
			if (ret != part.rb) {
				lprint(part.desc() + ": hasPhysics() Rigidbody incoherency: "
					+ part.physicalSignificance + ", " + (part.rb ? "rb ok" : "rb null"));
				ret = part.rb;
			}
			return ret;
		}

		public static bool forcePhysics(this Part part)
		{
			if (!part || part.hasPhysics())
				return false;

			lprint(part.desc() + ": calling PromoteToPhysicalPart(), " + part.physicalSignificance + ", " + part.PhysicsSignificance);
			part.PromoteToPhysicalPart();
			lprint(part.desc() + ": called PromoteToPhysicalPart(), " + part.physicalSignificance + ", " + part.PhysicsSignificance);
			if (part.parent) {
				if (part.attachJoint) {
					lprint(part.desc() + ": parent joint exists already: " + part.attachJoint.desc());
				} else {
					AttachNode nodeHere = part.FindAttachNodeByPart(part.parent);
					AttachNode nodeParent = part.parent.FindAttachNodeByPart(part);
					AttachModes m = (nodeHere != null && nodeParent != null) ?
						AttachModes.STACK : AttachModes.SRF_ATTACH;
					part.CreateAttachJoint(m);
					lprint(part.desc() + ": created joint " + m + " " + part.attachJoint.desc());
				}
			}

			return true;
		}

		/******** ModuleDockingMode utilities ********/

		public static ModuleDockingNode otherNode(this ModuleDockingNode node)
		{
			// this prevents a warning
			if (node.dockedPartUId <= 0)
				return null;
			return node.FindOtherNode();
		}

		public static string allTypes(this ModuleDockingNode node)
		{
			string lst = "";
			foreach (string t in node.nodeTypes) {
				if (lst.Length > 0)
					lst += ",";
				lst += t;
			}
			return lst;
		}

		/******** AttachNode utilities ********/

		public static string desc(this AttachNode n)
		{
			if (n == null)
				return "null";
			return "[\"" + n.id + "\": "
				+ n.owner.desc() + " -> " + n.attachedPart.desc()
				+ ", size " + n.size + "]";
		}

		/******** PartJoint utilities ********/

		public static string desc(this PartJoint j)
		{
			if (j == null)
				return "null";
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
			return "drv(frc=" + drive.maximumForce
				+ " spr=" + drive.positionSpring
				+ " dmp=" + drive.positionDamper
				+ ")";
		}

		public static string desc(this SoftJointLimit limit)
		{
			return "lim(lim=" + limit.limit
				+ " bnc=" + limit.bounciness
				+ " dst=" + limit.contactDistance
				+ ")";
		}

		public static string desc(this SoftJointLimitSpring spring)
		{
			return "spr(spr=" + spring.spring
				+ " dmp=" + spring.damper
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
			lprint("  Axes: " + j.axis.desc() + ", " + j.secondaryAxis.desc());
			if (p)
				lprint("  AxesV: " + j.axis.Td(j.T(), j.T()).desc()
					+ ", " + j.secondaryAxis.Td(j.T(), p.T()).desc());

			lprint("  Anchors: " + j.anchor.desc()
				+ " -> " + j.connectedAnchor.desc()
				+ " [" + j.connectedAnchor.Tp(j.connectedBody.T(), j.T()).desc() + "]");

			lprint("  Tgt: " + j.targetPosition.desc() + ", " + j.targetRotation.desc());

			lprint("  angX: " + _jdump(j.angularXMotion, j.angularXDrive, j.lowAngularXLimit, j.angularXLimitSpring));
			lprint("  angY: " + _jdump(j.angularYMotion, j.angularYZDrive, j.angularYLimit, j.angularYZLimitSpring));
			lprint("  angZ: " + _jdump(j.angularZMotion, j.angularYZDrive, j.angularZLimit, j.angularYZLimitSpring));
			lprint("  linX: " + _jdump(j.xMotion, j.xDrive, j.linearLimit, j.linearLimitSpring));
			lprint("  linY: " + _jdump(j.yMotion, j.yDrive, j.linearLimit, j.linearLimitSpring));
			lprint("  linZ: " + _jdump(j.zMotion, j.zDrive, j.linearLimit, j.linearLimitSpring));

			lprint("  proj: " + j.projectionMode + " ang=" + j.projectionAngle + " dst=" + j.projectionDistance);
		}

		private static string _jdump(ConfigurableJointMotion mot, JointDrive drv, SoftJointLimit lim, SoftJointLimitSpring spr)
		{
			return mot.ToString() + " " + drv.desc() + " " + lim.desc() + " " + spr.desc();
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
			return v.ToString(v == Vector3.zero ? "F0" : "F2");
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

		public static string desc(this Quaternion q)
		{
			float angle;
			Vector3 axis;
			q.ToAngleAxis(out angle, out axis);
			if (angle == 0f)
				axis = Vector3.zero;
			return angle.ToString(angle == 0 ? "F0" : "F1") + "\u00b0" + axis.desc();
		}
	}
}

