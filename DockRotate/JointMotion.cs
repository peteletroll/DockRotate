using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class JointMotion: MonoBehaviour, ISmoothMotionListener
	{
		private PartJoint _joint;
		public PartJoint joint { get => _joint; }
		public Vessel vessel { get => joint ? joint.Host.vessel : null; }

		public Vector3 hostAxis, hostNode;
		private Vector3 hostUp, targetUp;

		private SmoothMotionDispatcher rotation;

		private JointMotionObj _rotCur;
		public JointMotionObj rotCur {
			get { return _rotCur; }
			set {
				if (!joint || !joint.Host || !joint.Host.vessel)
					return;

				bool sas = (_rotCur ? _rotCur.smartAutoStruts : false)
					|| (value ? value.smartAutoStruts : false);

				int delta = (value && !_rotCur) ? +1
					: (!value && _rotCur) ? -1
					: 0;

				_rotCur = value;
				VesselMotionManager.get(joint.Host.vessel).changeCount(delta);
				if (!sas) {
					log(joint.Host.desc(), ": triggered CycleAllAutoStruts()");
					joint.Host.vessel.CycleAllAutoStrut();
				}
			}
		}

		private ModuleBaseRotate _controller;
		public ModuleBaseRotate controller {
			get {
				if (!_controller)
					log(joint.desc(), ": *** WARNING *** null controller");
				return _controller;
			}
			set {
				if (!value) {
					log(joint.desc(), ": *** WARNING *** refusing to set null controller");
					return;
				}
				if (value != _controller) {
					if (_controller) {
						log(joint.desc(), ": change controller " + _controller.part.desc() + " -> " + value.part.desc());
					} else {
						log(joint.desc(), ": set controller " + value.part.desc());
					}
					if (_rotCur) {
						log(joint.desc(), ": refusing to set controller while moving");
						return;
					}
				}

				_controller = value;
				if (_controller)
					_controller.putAxis(this);
			}
		}

		public static JointMotion get(PartJoint j)
		{
			if (!j)
				return null;

			if (j.gameObject != j.Host.gameObject)
				log(nameof(JointMotion), ".get(): *** WARNING *** gameObject incoherency");

			JointMotion[] jms = j.gameObject.GetComponents<JointMotion>();
			for (int i = 0; i < jms.Length; i++)
				if (jms[i].joint == j)
					return jms[i];

			JointMotion jm = j.gameObject.AddComponent<JointMotion>();
			jm._joint = j;
			log(nameof(JointMotion), ".get(): created " + jm.desc());
			return jm;
		}

		public void setAxis(Part part, Vector3 axis, Vector3 node)
		{
			if (rotCur) {
				log(part.desc(), ".setAxis(): rotating, skipped");
				return;
			}

			string state = "none";
			if (part == joint.Host) {
				state = "direct";
				// no conversion needed
			} else if (part == joint.Target) {
				state = "inverse";
				axis = -axis;
			} else {
				log(desc(), ".setAxis(): part " + part.desc() + " not in " + joint.desc());
			}
			if (!controller || controller.verboseEvents)
				log(desc(), ".setAxis(): " + axis.desc() + "@" + node.desc() + "), " + state);
			hostAxis = axis.STd(part, joint.Host);
			hostNode = node.STp(part, joint.Host);
			hostUp = joint.Host.up(hostAxis);
			targetUp = joint.Target.up(hostAxis.STd(joint.Host, joint.Target));
		}

		public virtual bool enqueueRotation(ModuleBaseRotate source, float angle, float speed, float startSpeed = 0f)
		{
			if (!joint)
				return false;

			if (speed < 0.1f)
				return false;

			string action = "none";
			bool showlog = true;
			if (rotCur) {
				bool trace = false;
				if (rotCur.isBraking()) {
					log(joint.desc(), ".enqueueRotation(): canceled, braking");
					return false;
				}
				rotCur.maxvel = speed;
				action = "updated";
				if (SmoothMotion.isContinuous(ref angle)) {
					if (rotCur.isContinuous() && angle * rotCur.tgt > 0f)
						showlog = false; // already continuous the right way
					if (trace && showlog)
						log(joint.desc(), "MERGE CONTINUOUS " + angle + " -> " + rotCur.tgt);
					rotCur.tgt = angle;
					controller.updateFrozenRotation("MERGECONT");
				} else {
					if (trace)
						log(joint.desc(), "MERGE LIMITED " + angle + " -> " + rotCur.rot0 + " + " + rotCur.tgt);
					if (rotCur.isContinuous()) {
						if (trace)
							log(joint.desc(), "MERGE INTO CONTINUOUS");
						rotCur.tgt = rotCur.pos + rotCur.curBrakingSpace() + angle;
					} else {
						if (trace)
							log(joint.desc(), "MERGE INTO LIMITED");
						rotCur.tgt = rotCur.tgt + angle;
					}
					if (trace)
						log(joint.desc(), "MERGED: POS " + rotCur.pos + " TGT " + rotCur.tgt);
					controller.updateFrozenRotation("MERGELIM");
				}
			} else {
				log(joint.desc(), ": creating rotation");
				JointMotionObj r = new JointMotionObj(this, 0, angle, speed);
				r.rot0 = rotationAngle(false);
				r.vel = startSpeed;
				controller = source;
				r.electricityRate = source.electricityRate;
				r.smartAutoStruts = source.smartAutoStruts;
				rotCur = r;
				action = "added";
			}
			if (showlog)
				log(joint.desc(), String.Format(": enqueueRotation({0}, {1:F4}\u00b0, {2}\u00b0/s, {3}\u00b0/s), {4}",
					hostAxis.desc(), rotCur.tgt, rotCur.maxvel, rotCur.vel, action));
			return true;
		}

		public float rotationAngle(bool dynamic)
		{
			Vector3 a = hostAxis;
			Vector3 v1 = hostUp;
			Vector3 v2 = dynamic ?
				targetUp.Td(joint.Target.T(), joint.Host.T()) :
				targetUp.STd(joint.Target, joint.Host);
			return a.axisSignedAngle(v1, v2);
		}

		public float dynamicDeltaAngle()
		// = dynamic - static
		{
			Vector3 a = hostAxis;
			Vector3 vd = targetUp.Td(joint.Target.T(), joint.Host.T());
			Vector3 vs = targetUp.STd(joint.Target, joint.Host);
			return a.axisSignedAngle(vs, vd);
		}

		public float angleToSnap(float snap)
		{
			snap = Mathf.Abs(snap);
			if (snap < 0.1f)
				return 0f;
			float a = !rotCur ? rotationAngle(false) :
				rotCur.isContinuous() ? rotCur.rot0 + rotCur.pos :
				rotCur.rot0 + rotCur.tgt;
			if (float.IsNaN(a))
				return 0f;
			float f = snap * Mathf.Floor(a / snap + 0.5f);
			return f - a;
		}

		protected bool brakeRotationKey()
		{
			return vessel && vessel == FlightGlobals.ActiveVessel
				&& GameSettings.MODIFIER_KEY.GetKey()
				&& GameSettings.BRAKES.GetKeyDown();
		}

		public void FixedUpdate()
		{
			if (!vessel)
				MonoBehaviour.Destroy(this);

			if (!rotCur || HighLogic.LoadedScene != GameScenes.FLIGHT)
				return;

			if (rotCur.done()) {
				log(joint.desc(), ": removing rotation (1)");
				rotCur = null;
				return;
			}

			rotCur.clampAngle();
			if (brakeRotationKey())
				rotCur.brake();
			rotCur.advance(Time.fixedDeltaTime);
			controller.updateFrozenRotation("FIXED");
		}

		public void onStart(SmoothMotionDispatcher source)
		{
		}

		public void onStep(SmoothMotionDispatcher source, float deltat)
		{
		}

		public void onStop(SmoothMotionDispatcher source)
		{
		}

		public void Awake()
		{
			log(desc(), ".Awake()");
			// FIXME: all the stuff JointMotionObj does should go into SmoothMotionDispatcher
			rotation = new SmoothMotionDispatcher(this);
		}

		public void Start()
		{
			log(desc(), ".Start()");
		}

		public void OnDestroy()
		{
			log(desc(), ".OnDestroy()");
			rotCur = null;
			if (sound)
				MonoBehaviour.Destroy(sound);
		}

		/******** sound stuff ********/

		public const float pitchAlterationRateMax = 0.1f;

		public AudioSource sound;

		public float soundVolume = 1f;
		public float pitchAlteration = 1f;

		public void startSound()
		{
			if (sound)
				return;

			if (!rotCur || !controller) {
				log("sound: no " + (rotCur ? "controller" : "rotation"));
				return;
			}

			try {
				soundVolume = controller.soundVolume;
				AudioClip clip = GameDatabase.Instance.GetAudioClip(controller.soundClip);
				if (!clip) {
					log("sound: clip \"" + controller.soundClip + "\" not found");
					return;
				}

				sound = joint.Host.gameObject.AddComponent<AudioSource>();
				sound.clip = clip;
				sound.volume = 0f;
				sound.pitch = 0f;
				sound.loop = true;
				sound.rolloffMode = AudioRolloffMode.Logarithmic;
				sound.spatialBlend = 1f;
				sound.minDistance = 1f;
				sound.maxDistance = 1000f;
				sound.playOnAwake = false;

				uint pa = (33u * (joint.Host.flightID ^ joint.Target.flightID)) % 10000u;
				pitchAlteration = 2f * pitchAlterationRateMax * (pa / 10000f)
					+ (1f - pitchAlterationRateMax);

				sound.Play();
			} catch (Exception e) {
				log("sound: " + e.Message);
				if (sound)
					MonoBehaviour.Destroy(sound);
				sound = null;
			}
		}

		public void stepSound()
		{
			if (sound != null && rotCur) {
				float p = Mathf.Sqrt(Mathf.Abs(rotCur.vel / rotCur.maxvel));
				sound.volume = soundVolume * p * GameSettings.SHIP_VOLUME;
				sound.pitch = p * pitchAlteration;
			}
		}

		public void stopSound()
		{
			if (sound != null) {
				sound.Stop();
				MonoBehaviour.Destroy(sound);
				sound = null;
			}
		}

		public string desc(bool bare = false)
		{
			return (bare ? "" : "JM:") + GetInstanceID() + ":" + joint.desc(true);
		}

		protected static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}

	public class JointMotionObj: SmoothMotion
	{
		private JointMotion jm;

		public bool smartAutoStruts = false;

		public double electricity = 0d;
		public float electricityRate = 1f;
		public float rot0 = 0f;

		private Part activePart { get { return jm.joint.Host; } }
		private Part proxyPart { get { return jm.joint.Target; } }

		private Vector3 axis { get { return jm.hostAxis; } }
		private Vector3 node { get { return jm.hostNode; } }

		private struct RotJointInfo
		{
			public ConfigurableJointManager cjm;
			public Vector3 localAxis, localNode;
			public Vector3 jointAxis, jointNode;
			public Vector3 connectedBodyAxis, connectedBodyNode;
		}
		private RotJointInfo[] rji;

		public JointMotionObj(JointMotion jm, float pos, float tgt, float maxvel)
		{
			this.jm = jm;

			this.pos = pos;
			this.tgt = tgt;
			this.maxvel = maxvel;

			this.vel = 0;
		}

		protected override void onStart()
		{
			if (smartAutoStruts) {
				activePart.releaseCrossAutoStruts();
			} else {
				// not needed with new IsJointUnlocked() logic
				// but IsJointUnlocked() logic is bugged now :-(
				List<Part> parts = activePart.vessel.parts;
				for (int i = 0; i < parts.Count; i++)
					parts[i].ReleaseAutoStruts();
			}
			int c = jm.joint.joints.Count;
			rji = new RotJointInfo[c];
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = jm.joint.joints[i];

				RotJointInfo ji;

				ji.cjm = new ConfigurableJointManager();
				ji.cjm.setup(j);

				ji.localAxis = axis.Td(activePart.T(), j.T());
				ji.localNode = node.Tp(activePart.T(), j.T());

				ji.jointAxis = ji.cjm.L2Jd(ji.localAxis);
				ji.jointNode = ji.cjm.L2Jp(ji.localNode);

				ji.connectedBodyAxis = axis.STd(activePart, proxyPart)
					.Td(proxyPart.T(), proxyPart.rb.T());
				ji.connectedBodyNode = node.STp(activePart, proxyPart)
					.Tp(proxyPart.T(), proxyPart.rb.T());

				rji[i] = ji;

				j.reconfigureForRotation();
			}

			jm.startSound();
		}

		protected override void onStep(float deltat)
		{
			int c = jm.joint.joints.Count;
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = jm.joint.joints[i];
				if (!j)
					continue;
				RotJointInfo ji = rji[i];
				ji.cjm.setRotation(pos, ji.localAxis, ji.localNode);
			}

			jm.stepSound();

			if (jm.controller) {
				float s = jm.controller.speed();
				if (!Mathf.Approximately(s, maxvel)) {
					log(jm.controller.part.desc() + ": speed change " + maxvel + " -> " + s);
					maxvel = s;
				}
			}

			if (deltat > 0f && electricityRate > 0f) {
				double el = activePart.RequestResource("ElectricCharge", (double) electricityRate * deltat);
				electricity += el;
				if (el <= 0d) {
					log(jm.desc(), "no electricity, braking rotation");
					brake();
				}
			}
		}

		protected override void onStop()
		{
			jm.stopSound();

			onStep(0);

			staticize();

			int c = VesselMotionManager.get(activePart).changeCount(0);
			log(activePart.desc(), ": rotation stopped [" + c + "], "
				+ electricity.ToString("F2") + " electricity");
			electricity = 0d;
		}

		public void staticize()
		{
			log(jm.desc(), ".staticize() at pos = " + pos + "\u00b0");
			staticizeJoints();
			staticizeOrgInfo();
		}

		private void staticizeJoints()
		{
			int c = jm.joint.joints.Count;
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = jm.joint.joints[i];
				if (!j)
					continue;
				RotJointInfo ji = rji[i];

				// staticize joint rotation

				ConfigurableJointManager.staticizeRotation(ji.cjm);

				// FIXME: this should be moved to JointManager
				Quaternion connectedBodyRot = ji.connectedBodyAxis.rotation(-pos);
				j.connectedAnchor = connectedBodyRot * (j.connectedAnchor - ji.connectedBodyNode)
					+ ji.connectedBodyNode;
				j.targetPosition = ji.cjm.tgtPos0;

				ji.cjm.setup();
			}
		}

		private bool staticizeOrgInfo()
		{
			if (jm.joint != activePart.attachJoint) {
				log(jm.desc(), ": skip staticize, same vessel joint");
				return false;
			}
			float angle = pos;
			Vector3 nodeAxis = -axis.STd(activePart, activePart.vessel.rootPart);
			Quaternion nodeRot = nodeAxis.rotation(angle);
			Vector3 nodePos = node.STp(activePart, activePart.vessel.rootPart);
			_propagate(activePart, nodeRot, nodePos);
			return true;
		}

		private static void _propagate(Part p, Quaternion rot, Vector3 pos)
		{
			p.orgPos = rot * (p.orgPos - pos) + pos;
			p.orgRot = rot * p.orgRot;

			int c = p.children.Count;
			for (int i = 0; i < c; i++)
				_propagate(p.children[i], rot, pos);
		}

		public static implicit operator bool(JointMotionObj r)
		{
			return r != null;
		}

		protected static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}

