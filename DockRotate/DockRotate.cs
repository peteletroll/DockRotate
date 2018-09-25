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

		public int continuousRotation = 0;

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
				if (goingRightWay && continuousRotation != 0) {
					// continuous, keep going
					float newtgt = tgt + 180.0f * continuousRotation;
					ModuleBaseRotate.lprint("keep going " + tgt + " -> " + newtgt);
					tgt = newtgt;
					clampAngle();
				} else {
					// braking
					newvel -= deltat * Mathf.Sign(vel) * maxacc;
				}
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

		public bool clampAngle()
		{
			if (tgt < -180f || tgt > 180f) {
				float newzero = 360f * Mathf.Floor(tgt / 360f + 0.5f);
				ModuleBaseRotate.lprint("clampAngle(): newzero " + newzero + " from tgt " + tgt);
				tgt -= newzero;
				pos -= newzero;
				return true;
			}
			return false;
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

		public void brake()
		{
			continuousRotation = 0;
			float brakingTime = Mathf.Abs(vel) / maxacc;
			float brakingSpace = vel / 2 * brakingTime;
			tgt = pos + brakingSpace;
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

		public float timeref = 0.0f;

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
			// lprint("vesselRotCount is now " + ret);
			return vi.rotCount = ret;
		}

		public override void advance(float deltat)
		{
			if (finished)
				return;
			timeref += deltat;
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
					abort("no electric charge");
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

		public void forceStaticize()
		{
			lprint("forceStaticize() at " + tgt + "\u00b0");
			staticizeOrgInfo();
			staticizeJoints();
		}

		private void staticizeJoints()
		{
			float angle = tgt;
			for (int i = 0; i < joint.joints.Count; i++) {
				ConfigurableJoint j = joint.joints[i];
				if (j) {
					RotJointInfo ji = rji[i];

					// staticize joint rotation
					Quaternion jointRot = ji.localAxis.rotation(angle);
					j.axis = jointRot * j.axis;
					j.secondaryAxis = jointRot * j.secondaryAxis;
					j.targetRotation = ji.jm.tgtRot0;

					Quaternion connectedBodyRot = ji.connectedBodyAxis.rotation(-angle);
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

		public void abort(string msg)
		{
			lprint("ABORTING: " + msg);
			stopSound();
			tgt = pos;
			vel = 0;
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
			guiActiveEditor = true,
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
			guiActiveEditor = true,
			isPersistant = true,
			guiName = "#DCKROT_rotation_speed",
			guiUnits = "\u00b0/s"
		)]
		public float rotationSpeed = 5;

		[UI_Toggle(affectSymCounterparts = UI_Scene.None)]
		[KSPField(
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			guiName = "#DCKROT_reverse_rotation"
		)]
		public bool reverseRotation = false;

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
		public String nodeStatus = "";
#endif

#if DEBUG
		[UI_Toggle()]
		[KSPField(
			guiName = "Verbose Events",
			guiActive = true,
			guiActiveEditor = false
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
			guiActiveEditor = false
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
				if (rotationReverseKey()) {
					tgt.doRotateCounterclockwise();
				} else {
					tgt.doRotateClockwise();
				}
			}
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
			if (tgt) {
				if (rotationReverseKey()) {
					tgt.doRotateClockwise();
				} else {
					tgt.doRotateCounterclockwise();
				}
			}
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

		protected const float CONTINUOUS = 99999.0f;

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
			if (r != null)
				r.brake();
		}

		protected bool rotationReverseKey()
		{
			return GameSettings.MODIFIER_KEY.GetKey();
		}

		protected bool brakeRotationKey()
		{
			return GameSettings.MODIFIER_KEY.GetKey()
				&& GameSettings.BRAKES.GetKeyDown();
		}

		public bool IsJointUnlocked()
		{
			bool ret = currentRotation() != null;
			// lprint(part.desc() + ".IsJointUnlocked() is " + ret);
			return ret;
		}

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
		protected bool inEditor;

		public PartJoint rotatingJoint;
		public Part activePart, proxyPart;
		public string nodeRole = "Init";
		protected Vector3 partNodePos; // node position, relative to part
		public Vector3 partNodeAxis; // node rotation axis, relative to part
		protected Vector3 partNodeUp; // node vector for measuring angle, relative to part

		// localized info cache
		protected string displayName = "";
		protected string displayInfo = "";

		[KSPField(isPersistant = true)]
		public int frozenContinuousRotation = 0;

		[KSPField(isPersistant = true)]
		public Vector3 frozenRotation = Vector3.zero;

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
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": OnVesselGoOffRails(" + v.persistentId + ") [" + vessel.persistentId + "]");
			if (v != vessel)
				return;
			// VesselRotInfo.resetInfo(vessel.id);
			onRails = false;
			setup();
		}

		public void RightBeforeStructureChangeIds(uint id1, uint id2)
		{
			if (!vessel)
				return;
			uint id = vessel.persistentId;
			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeStructureChangeIds("
					+ id1 + ", " + id2 + ") [" + id + "]");
			if (id1 == id || id2 == id) {
				RightBeforeStructureChange();
			}
		}

		public void RightBeforeStructureChangeAction(GameEvents.FromToAction<Part, Part> action)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeStructureChangeAction("
					+ action.from.desc() + ", " + action.to.desc() + ")");
			if (action.from.vessel == vessel || action.to.vessel == vessel) {
				RightBeforeStructureChange();
			}
		}

		public void RightBeforeStructureChangePart(Part p)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeStructureChangePart(" + part.desc() + ")");
			if (p.vessel == vessel) {
				RightBeforeStructureChange();
			}
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
			if (action.from.vessel == vessel || action.to.vessel == vessel) {
				RightAfterStructureChange();
			}
		}

		public void RightAfterStructureChangePart(Part p)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightAfterStructureChangePart(" + p.desc() + ")");
			if (p.vessel == vessel) {
				RightAfterStructureChange();
			}
		}

		private void RightAfterStructureChange()
		{
			if (verboseEvents)
				lprint(part.desc() + ": RightAfterStructureChange()");
			setup();
		}

		private void RightAfterSameVesselDock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightAfterSameVesselDock("
					+ action.from.part.desc() + ", " + action.to.part.desc() + ")");
			if (action.to.part == part || action.from.part == part) {
				setup();
			}
		}

		private void RightAfterSameVesselUndock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			if (!vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightAfterSameVesselUndock("
					+ action.from.part.desc() + ", " + action.to.part.desc() + ")");
			if (action.to.part == part || action.from.part == part) {
				setup();
			}
		}

		public override void OnAwake()
		{
			base.OnAwake();

			GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);

			GameEvents.onVesselDocking.Add(RightBeforeStructureChangeIds);
			GameEvents.onDockingComplete.Add(RightAfterStructureChangeAction);
			GameEvents.onPartUndock.Add(RightBeforeStructureChangePart);
			GameEvents.onPartUndockComplete.Add(RightAfterStructureChangePart);

			GameEvents.onPartCouple.Add(RightBeforeStructureChangeAction);
			GameEvents.onPartCoupleComplete.Add(RightAfterStructureChangeAction);
			GameEvents.onPartDeCouple.Add(RightBeforeStructureChangePart);
			GameEvents.onPartDeCoupleComplete.Add(RightAfterStructureChangePart);

			GameEvents.onSameVesselDock.Add(RightAfterSameVesselDock);
			GameEvents.onSameVesselUndock.Add(RightAfterSameVesselUndock);
		}

		public virtual void OnDestroy()
		{
			GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);

			GameEvents.onVesselDocking.Remove(RightBeforeStructureChangeIds);
			GameEvents.onDockingComplete.Remove(RightAfterStructureChangeAction);
			GameEvents.onPartUndock.Remove(RightBeforeStructureChangePart);
			GameEvents.onPartUndockComplete.Remove(RightAfterStructureChangePart);

			GameEvents.onPartCouple.Remove(RightBeforeStructureChangeAction);
			GameEvents.onPartCoupleComplete.Remove(RightAfterStructureChangeAction);
			GameEvents.onPartDeCouple.Remove(RightBeforeStructureChangePart);
			GameEvents.onPartDeCoupleComplete.Remove(RightAfterStructureChangePart);

			GameEvents.onSameVesselDock.Remove(RightAfterSameVesselDock);
			GameEvents.onSameVesselUndock.Remove(RightAfterSameVesselUndock);
		}

		protected static string[] guiList = {
			"nodeRole",
			"rotationStep",
			"rotationSpeed",
			"reverseRotation",
			"RotateClockwise",
			"RotateCounterclockwise",
			"RotateToSnap",
			"ToggleAutoStrutDisplay"
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

			lprint(part.desc() + ": " + fld.Length + " fields, " + evt.Length + " events");
		}

		private void checkGuiActive()
		{
			bool newGuiActive = canStartRotation();

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

			inEditor = (state & StartState.Editor) != 0;
			if (inEditor)
				return;

			setupGuiActive();

			checkGuiActive();
		}

		public override void OnUpdate()
		{
			base.OnUpdate();
			if (inEditor)
				return;

			bool guiActive = canStartRotation();

#if DEBUG
			nodeStatus = "";
			int nJoints = countJoints();
			nodeStatus = nodeRole + " [" + nJoints + "]";
			Fields["nodeStatus"].guiActive = guiActive && nodeStatus.Length > 0;
#endif

			RotationAnimation cr = currentRotation();
			if (cr != null) {
				angleInfo = String.Format("{0:+0.00;-0.00;0.00}\u00b0 ({1:+0.00;-0.00;0.00}\u00b0/s)",
					rotationAngle(true),
					cr.vel);
			} else {
				angleInfo = String.Format("{0:+0.00;-0.00;0.00}\u00b0 ({1:+0.0000;-0.0000;0.0000}\u00b0\u0394)",
					rotationAngle(false),
					dynamicDeltaAngle());
			}

			Fields["angleInfo"].guiActive = guiActive;

			Events["StopRotation"].guiActive = currentRotation() != null;

			checkGuiActive();

#if DEBUG
			Events["ToggleAutoStrutDisplay"].guiName = PhysicsGlobals.AutoStrutDisplay ? "Hide Autostruts" : "Show Autostruts";
#endif
		}

		protected abstract void setup();

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

		protected void enqueueFrozenRotation(float angle, float speed, float startSpeed = 0.0f)
		{
			Vector3 prev = frozenRotation;
			if (frozenRotation[0] == 0.0f) {
				frozenRotation[0] = angle;
				frozenRotation[1] = speed;
				frozenRotation[2] = startSpeed;
			} else {
				frozenRotation[0] += angle;
			}
			lprint(part.desc() + ": frozen rotation " + prev + " -> " + frozenRotation);
		}

		protected virtual void enqueueRotation(float angle, float speed, float startSpeed = 0.0f)
		{
			if (!rotatingJoint)
				return;

			if (speed < 0.5)
				return;

			string action = "none";
			if (rotCur != null) {
				if (rotCur.continuousRotation != 0) {
					lprint(part.desc() + ": leaving continuous rotation alone");
				} else {
					rotCur.tgt += angle;
					rotCur.maxvel = speed;
					action = "updated";
				}
			} else {
				action = "added";
				int continuous = 0;
				if (Mathf.Abs(angle) >= CONTINUOUS - 0.5f) {
					angle = Mathf.Clamp(angle, -180f, 180f);
					continuous = (int)Mathf.Sign(angle);
					action = "added continuous";
				}

				rotCur = new RotationAnimation(activePart, partNodePos, partNodeAxis, rotatingJoint, 0, angle, speed);
				rotCur.vel = startSpeed;
				rotCur.continuousRotation = continuous;
				rotCur.smartAutoStruts = useSmartAutoStruts();
			}
			lprint(String.Format("{0}: enqueueRotation({1}, {2:F4}\u00b0, {3}\u00b0/s, {4}\u00b0/s), {5}",
				activePart.desc(), partNodeAxis.desc(), rotCur.tgt, rotCur.maxvel, rotCur.vel, action));
		}

		protected float angleToSnap(float snap)
		{
			snap = Mathf.Abs(snap);
			if (snap < 0.5f)
				return 0.0f;
			float a = rotCur == null ? rotationAngle(false) : rotCur.tgt;
			if (float.IsNaN(a))
				return 0.0f;
			float f = snap * Mathf.Floor(a / snap + 0.5f);
			return f - a;
		}

		protected void enqueueRotationToSnap(float snap, float speed)
		{
			if (snap < 0.5f)
				snap = 15.0f;
			enqueueRotation(angleToSnap(snap), rotationSpeed);
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

		protected void freezeCurrentRotation(string msg, bool keepSpeed)
		{
			if (rotCur != null) {
				float angle = rotCur.tgt - rotCur.pos;
				if (rotCur.continuousRotation != 0)
					angle = rotCur.continuousRotation * CONTINUOUS;
				enqueueFrozenRotation(angle, rotCur.maxvel, keepSpeed ? rotCur.vel : 0.0f);
				rotCur.abort(msg);
				rotCur.forceStaticize();
				rotCur = null;
			}
		}

		protected abstract RotationAnimation currentRotation();

		protected void checkFrozenRotation()
		{
			if (frozenRotation[0] != 0.0f) {
				Vector3 fr = frozenRotation;
				frozenRotation = Vector3.zero;
				lprint(part.desc() + ": resuming rotation " + fr);
				enqueueRotation(fr[0], fr[1], fr[2]);
			}
		}

		public void FixedUpdate()
		{
			if (onRails || HighLogic.LoadedScene != GameScenes.FLIGHT)
				return;

			checkFrozenRotation();

			if (rotCur != null) {
				if (brakeRotationKey())
					rotCur.brake();
				advanceRotation(Time.fixedDeltaTime);
			}

			if (frozenContinuousRotation != 0 && rotCur == null) {
				lprint(part.desc() + ": restoring continuous rotation");
				enqueueRotation(frozenContinuousRotation * CONTINUOUS, rotationSpeed);
			}

			int newfcr = rotCur != null ? rotCur.continuousRotation : 0;
			if (newfcr != frozenContinuousRotation) {
				lprint(part.desc() + ": frozenContinuousRotation from " + frozenContinuousRotation + " to " + newfcr);
				frozenContinuousRotation = newfcr;
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

		private int lastVesselSave = 0;

		public void RightBeforeVesselSave(GameEvents.FromToAction<ProtoVessel, ConfigNode> action)
		{
			int now = Time.frameCount;
			if (lastVesselSave == now)
				return;
			lastVesselSave = now;

			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeVesselSave()");
			AttachNode node = part.PhysicsSignificance == 0 ?
				part.FindAttachNode(rotatingNodeName) : null;
			if (node == null)
				return;
			Part other = node.attachedPart;
			if (!other)
				return;
			lprint(part.desc() + ": connected to " + other.desc());
			other.forcePhysics();
		}

		public override void OnAwake()
		{
			GameEvents.onProtoVesselSave.Add(RightBeforeVesselSave);
			base.OnAwake();
		}

		public override void OnDestroy()
		{
			GameEvents.onProtoVesselSave.Remove(RightBeforeVesselSave);
			base.OnDestroy();
		}

		protected override void setup()
		{
			if (onRails || !part || !vessel)
				return;

			setupGuiActive();

			rotationStep = Mathf.Abs(rotationStep);
			rotationSpeed = Mathf.Abs(rotationSpeed);

			rotatingJoint = null;
			activePart = proxyPart = null;
			partNodePos = partNodeAxis = partNodeUp = otherPartUp = undefV3;

			nodeRole = "None";

			if (part.FindModuleImplementing<ModuleDockRotate>())
				return;

			rotatingNode = part.PhysicsSignificance == 0 ?
				part.FindAttachNode(rotatingNodeName) : null;
			if (rotatingNode == null) {
				lprint(part.desc() + " has no node named \"" + rotatingNodeName + "\"");
				AttachNode[] nodes = part.FindAttachNodes("");
				string nodeList = part.desc() + " available nodes:";
				for (int i = 0; i < nodes.Length; i++)
					nodeList += " \"" + nodes[i].id + "\"";
				lprint(nodeList);
				return;
			}

			// FIXME: this should probably go
			AttachNode otherNode = rotatingNode.FindOpposingNode();
			if (false && otherNode == null)
				return;

			partNodePos = rotatingNode.position;
			partNodeAxis = rotatingNode.orientation;

			partNodeUp = part.up(partNodeAxis);

			Part other = rotatingNode.attachedPart;
			if (other)
				other.forcePhysics();

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

		public override void doRotateClockwise()
		{
			float s = rotationStep;
			if (s <= 0.5f)
				s = CONTINUOUS;
			if (reverseRotation)
				s = -s;
			enqueueRotation(s, rotationSpeed);
		}

		public override void doRotateCounterclockwise()
		{
			float s = -rotationStep;
			if (s >= -0.5f)
				s = -CONTINUOUS;
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

#if DEBUG
		[KSPEvent(
			guiName = "#DCKROT_redock",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void ReDock()
		{
			if (dockingNode)
				dockingNode.state = "Ready";
		}
#endif

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

			rotationStep = Mathf.Abs(rotationStep);
			rotationSpeed = Mathf.Abs(rotationSpeed);

			activePart = proxyPart = null;
			rotatingJoint = null;
			activeRotationModule = proxyRotationModule = null;
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
			if (onRails || !part || !vessel)
				return;

			setupGuiActive();

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
				proxyRotationModule.nodeRole = "Proxy";
				proxyRotationModule.activeRotationModule = activeRotationModule;
				proxyRotationModule.activePart = activePart;
				proxyRotationModule.proxyRotationModule = proxyRotationModule;
				proxyRotationModule.proxyPart = proxyPart;
				proxyPart = proxyRotationModule.part;
				if (verboseEvents)
					lprint(activeRotationModule.part.desc()
						+ ": on " + proxyRotationModule.part.desc());
			}

			if (dockingNode.snapRotation && dockingNode.snapOffset > 0
				&& activeRotationModule == this
				&& (rotationEnabled || proxyRotationModule.rotationEnabled)) {
				enqueueFrozenRotation(angleToSnap(dockingNode.snapOffset), rotationSpeed);
			}
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
					+ ", types " + dockingNode.nodeType + "/" + parentNode.nodeType);

			bool ret = dockingNode.nodeType == parentNode.nodeType
				&& nodeDist < 0.3f && nodeAngle < 5.0f;

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

		protected override void enqueueRotation(float angle, float speed, float startSpeed = 0.0f)
		{
			if (activeRotationModule == this) {
				base.enqueueRotation(angle, speed, startSpeed);
			} else if (activeRotationModule && activeRotationModule.activeRotationModule == activeRotationModule) {
				activeRotationModule.enqueueRotation(angle, speed, startSpeed);
			} else {
				lprint("enqueueRotation() called on wrong module, ignoring");
			}
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
			float s = rotationStep;
			if (s <= 0.5f)
				s = CONTINUOUS;
			if (reverseRotation)
				s = -s;
			enqueueRotation(s, rotationSpeed);
		}

		public override void doRotateCounterclockwise()
		{
			if (!activeRotationModule)
				return;
			float s = -rotationStep;
			if (s >= -0.5f)
				s = -CONTINUOUS;
			if (reverseRotation)
				s = -s;
			enqueueRotation(s, rotationSpeed);
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
			/*
			lprint("mass: " + part.mass);
			lprint("parent: " + descPart(part.parent));
			*/
			lprint("role: " + nodeRole);
#if DEBUG
			lprint("status: " + nodeStatus);
#endif
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

		public static void forcePhysics(this Part part)
		{
			if (part.PhysicsSignificance != 0 || part.physicalSignificance != Part.PhysicalSignificance.FULL) {
				lprint(part.desc() + ": calling PromoteToPhysicalPart()");
				part.PromoteToPhysicalPart();
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

