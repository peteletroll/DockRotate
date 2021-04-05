using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class JointMotion: MonoBehaviour
	{
		private PartJoint _joint;

		public PartJoint joint { get => _joint; }

		public Vector3 hostAxis, hostNode;
		private Vector3 hostUp, targetUp;

		private float orgRot = 0f;

		public bool verboseSetup {
			get => _controller && _controller.verboseSetup;
		}

		private JointMotionObj _rotCur;
		public JointMotionObj rotCur {
			get { return _rotCur; }
			set {
				bool sas = (_rotCur && _rotCur.smartAutoStruts)
					|| (value && value.smartAutoStruts);

				int delta = (value && !_rotCur) ? +1
					: (!value && _rotCur) ? -1
					: 0;

				_rotCur = value;
				enabled = _rotCur;

				if (delta != 0 && joint && joint.Host && joint.Host.vessel) {
					VesselMotionManager.get(joint.Host.vessel).changeCount(delta);
					joint.Host.vessel.KJRNextCycleAllAutoStrut();
					if (!sas) {
						log(desc(), ": triggered CycleAllAutoStruts()");
						joint.Host.vessel.CycleAllAutoStrut();
					}
				}
			}
		}

		private ModuleBaseRotate _controller;
		public ModuleBaseRotate controller {
			get {
				if (!_controller)
					log(desc(), ": *** WARNING *** null controller");
				return _controller;
			}
			set {
				if (!value) {
					log(desc(), ": *** WARNING *** refusing to set null controller");
					return;
				}
				if (value != _controller) {
					if (verboseSetup) {
						if (_controller) {
							log(desc(), ": change controller "
								+ _controller.part.desc() + " -> " + value.part.desc());
						} else {
							log(desc(), ": set controller " + value.part.desc());
						}
					}
					if (_rotCur) {
						log(desc(), ": refusing to set controller while moving");
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

			JointMotion jm = null;
			JointMotion[] jms = j.gameObject.GetComponents<JointMotion>();
			for (int i = 0; i < jms.Length; i++) {
				if (jms[i].joint == j) {
					if (!jm) {
						jm = jms[i];
					} else {
						log(nameof(JointMotion), ".get(): duplicated " + jms[i].desc());
						Destroy(jms[i]);
					}
				}
			}

			if (!jm) {
				jm = j.gameObject.AddComponent<JointMotion>();
				jm._joint = j;
				log(nameof(JointMotion), ".get(): created " + jm.desc());
			}

			return jm;
		}

		public bool hasController()
		{
			return _controller != null;
		}

		public void setAxis(Part part, Vector3 axis, Vector3 node)
		{
			if (rotCur) {
				log(desc(), ".setAxis(): rotating, skipped");
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
			if (!_controller || verboseSetup)
				log(desc(), ".setAxis(): " + axis.desc() + "@" + node.desc() + "), " + state);
			hostAxis = axis.STd(part, joint.Host);
			hostNode = node.STp(part, joint.Host);
			hostUp = hostAxis.findUp();
			targetUp = hostAxis.STd(joint.Host, joint.Target).findUp();
		}

		public virtual bool enqueueRotation(ModuleBaseRotate source, float angle, float speed, float startSpeed = 0f)
		{
			if (!joint) {
				log(desc(), ".enqueueRotation(): canceled, no joint");
				return false;
			}

			if (speed < 0.1f) {
				log(desc(), ".enqueueRotation(): canceled, no speed");
				return false;
			}

			string action = "";
			if (rotCur) {
				if (rotCur.isBraking()) {
					log(desc(), ".enqueueRotation(): canceled, braking");
					return false;
				}
				rotCur.maxvel = speed;
				if (SmoothMotion.isContinuous(ref angle)) {
					if (!Mathf.Approximately(rotCur.tgt, angle)) {
						rotCur.tgt = angle;
						controller.updateFrozenRotation("MERGECONT");
						action = "updated to cont";
					}
				} else {
					float refAngle = rotCur.isContinuous() ? rotCur.pos + rotCur.curBrakingSpace() : rotCur.tgt;
					rotCur.tgt = refAngle + angle;
					controller.updateFrozenRotation("MERGELIM");
					action = "updated to lim";
				}
			} else {
				JointMotionObj r = new JointMotionObj(this, 0, angle, speed);
				r.vel = startSpeed;
				controller = source;
				r.electricityRate = source.electricityRate;
				r.smartAutoStruts = source.smartAutoStruts;
				rotCur = r;
				action = "added";
			}
			if (action != "")
				log(desc(), String.Format(": enqueueRotation({0}, {1:F4}\u00b0, {2}\u00b0/s, {3}\u00b0/s), {4}",
					hostAxis.desc(), rotCur.tgt, rotCur.maxvel, rotCur.vel, action));
			return true;
		}

		public void updateOrgRot()
		{
			Vector3 a = hostAxis;
			Vector3 v1 = hostUp;
			Vector3 v2 = targetUp.STd(joint.Target, joint.Host);
			float orgRotPrev = orgRot;
			orgRot = a.axisSignedAngle(v1, v2);
			if (!Mathf.Approximately(orgRot, orgRotPrev))
				log(desc(), ".updateOrgRot(): " + orgRot + "\u00b0");
		}

		public float rotationAngle()
		{
			return Mathf.DeltaAngle(0f, orgRot + (_rotCur ? _rotCur.pos : 0f));
		}

		public float rotationTarget()
		{
			if (!_rotCur)
				return orgRot;

			float tgt = _rotCur.tgt;
			if (SmoothMotion.isContinuous(ref tgt))
				return tgt > 0 ? float.PositiveInfinity : float.NegativeInfinity;

			tgt = Mathf.DeltaAngle(0f, orgRot + tgt);
			if (Mathf.Abs(tgt) < 1e-4f)
				tgt = 0f;
			return tgt;
		}

		private float dynamicDeltaAngleRestart = 0f;

		public float dynamicDeltaAngle()
		// = dynamic - static
		{
			float ret = float.NaN;
			if (Time.fixedTime > dynamicDeltaAngleRestart) {
				try {
					Vector3 a = hostAxis;
					Vector3 vd = targetUp.Td(joint.Target.T(), joint.Host.T());
					Vector3 vs = targetUp.STd(joint.Target, joint.Host);
					ret = a.axisSignedAngle(vs, vd);
				} catch (Exception e) {
					float delayException = 20f;
					dynamicDeltaAngleRestart = Time.fixedTime + delayException;
					log(this.desc(), ".dynamicDeltaAngle(): " + e.Message
						+ ": disabling for " + delayException + " seconds");
					log(this.desc(), ": safetyCheck() is " + joint.safetyCheck());
				}
			}
			return ret;
		}

		public float angleToSnap(float snap)
		{
			snap = Mathf.Abs(snap);
			if (snap < 0.1f)
				return 0f;
			float refAngle = !rotCur ? rotationAngle() :
				rotCur.isContinuous() ? rotationAngle() + rotCur.curBrakingSpace() + snap * Mathf.Sign(rotCur.vel) / 2f :
				orgRot + rotCur.tgt;
			if (float.IsNaN(refAngle))
				return 0f;
			float snapAngle = snap * Mathf.Floor(refAngle / snap + 0.5f);
			return snapAngle - refAngle;
		}

		protected bool brakeRotationKey()
		{
			return GameSettings.MODIFIER_KEY.GetKey()
				&& GameSettings.BRAKES.GetKeyDown()
				&& joint && joint.Host && joint.Host.vessel == FlightGlobals.ActiveVessel;
		}

		public void FixedUpdate()
		{
			if (!rotCur || !HighLogic.LoadedSceneIsFlight) {
				// log(joint.desc(), ": disabling useless MonoBehaviour updates");
				enabled = false;
				return;
			}

			if (rotCur.done()) {
				log(desc(), ": removing rotation (done)");
				rotCur = null;
				return;
			}

			rotCur.clampAngle();
			if (brakeRotationKey())
				rotCur.brake();
			rotCur.advance(Time.fixedDeltaTime);
			controller.updateFrozenRotation("FIXED");
		}

		public void OnDestroy()
		{
			log(desc(), ".OnDestroy()");
			rotCur = null;
			stopSound();
		}

		/******** sound stuff ********/

		public const float pitchAlterationRateMax = 0.1f;

		public AudioSource sound;

		public float soundVolume = 1f;
		public float soundPitch = 1f;
		public float pitchAlteration = 1f;

		public void startSound()
		{
			if (sound)
				return;

			if (!rotCur || !controller) {
				log(desc(), "sound: no " + (rotCur ? "controller" : "rotation"));
				return;
			}

			try {
				soundVolume = controller.soundVolume;
				soundPitch = controller.soundPitch;
				AudioClip clip = GameDatabase.Instance.GetAudioClip(controller.soundClip);
				if (!clip) {
					log(desc(), "sound: clip \"" + controller.soundClip + "\" not found");
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
				log(desc(), "sound: " + e.Message);
				stopSound();
			}
		}

		public void stepSound()
		{
			if (rotCur && sound != null) {
				float p = Mathf.Sqrt(Mathf.Abs(rotCur.vel / rotCur.maxvel));
				sound.volume = soundVolume * p * GameSettings.SHIP_VOLUME;
				sound.pitch = p * soundPitch * pitchAlteration;
			}
		}

		public void stopSound()
		{
			AudioSource s = sound;
			sound = null;
			if (s != null) {
				s.Stop();
				Destroy(s);
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

		private Part hostPart { get { return jm.joint.Host; } }
		private Part targetPart { get { return jm.joint.Target; } }

		public ModuleBaseRotate controller { get => jm ? jm.controller : null; }

		private Vector3 axis { get { return jm.hostAxis; } }
		private Vector3 node { get { return jm.hostNode; } }

		private struct RotJointInfo
		{
			public ConfigurableJointManager cjm;
			public Vector3 localAxis, localNode;
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
			hostPart.vessel.KJRNextCycleAllAutoStrut();
			if (smartAutoStruts) {
				hostPart.releaseCrossAutoStruts(true);
			} else {
				List<Part> parts = hostPart.vessel.parts;
				for (int i = 0; i < parts.Count; i++)
					parts[i].ReleaseAutoStruts();
			}
			int c = jm.joint.joints.Count;
			rji = new RotJointInfo[c];
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = jm.joint.joints[i];

				ref RotJointInfo ji = ref rji[i];

				ji.cjm = new ConfigurableJointManager();
				ji.cjm.setup(j);

				ji.localAxis = axis.Td(hostPart.T(), j.T());
				ji.localNode = node.Tp(hostPart.T(), j.T());

				ji.connectedBodyAxis = axis.STd(hostPart, targetPart)
					.Td(targetPart.T(), targetPart.rb.T());
				ji.connectedBodyNode = node.STp(hostPart, targetPart)
					.Tp(targetPart.T(), targetPart.rb.T());

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
				ref RotJointInfo ji = ref rji[i];
				ji.cjm.setRotation(pos, ji.localAxis, ji.localNode);
				/*
				if (jm.verboseEvents && Time.frameCount % 10 == 0)
					log(jm.desc(), ": currentTorque[" + i + "] = " + j.currentTorque.desc()
						+ " |" + j.currentTorque.magnitude.ToString("E10") + "|");
				*/
			}

			jm.stepSound();

			if (jm.controller) {
				float s = jm.controller.speed();
				if (!Mathf.Approximately(s, maxvel)) {
					log(jm.controller.part.desc() + ": speed change " + maxvel + " -> " + s);
					maxvel = s;
				}
				if (!jm.controller.rotationEnabled && isContinuous() && !isBraking()) {
					log(jm.desc(), ": disabled rotation, braking");
					brake();
				}
			}

			if (deltat > 0f && electricityRate > 0f) {
				double el = hostPart.RequestResource("ElectricCharge", (double) electricityRate * deltat);
				electricity += el;
				if (el <= 0d && !isBraking()) {
					log(jm.desc(), ": no electricity, braking rotation");
					brake();
				}
			}
		}

		protected override void onStop()
		{
			jm.stopSound();

			onStep(0);

			staticize();

			int c = VesselMotionManager.get(hostPart.vessel).changeCount(0);
			log(hostPart.desc(), ": rotation stopped [" + c + "], "
				+ electricity.ToString("F2") + " electricity");
			electricity = 0d;
		}

		public void staticize()
		{
			staticizeJoints();
			staticizeOrgInfo();
			jm.updateOrgRot();
		}

		private void staticizeJoints()
		{
			int c = jm.joint.joints.Count;
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = jm.joint.joints[i];
				if (!j)
					continue;

				ref RotJointInfo ji = ref rji[i];

				// staticize joint rotation

				Quaternion localRotation = ji.localAxis.rotation(pos);
				j.axis = localRotation * j.axis;
				j.secondaryAxis = localRotation * j.secondaryAxis;
				j.targetRotation = ji.cjm.tgtRot0;

				// staticize joint position

				Quaternion connectedBodyRot = ji.connectedBodyAxis.rotation(-pos);
				j.connectedAnchor = connectedBodyRot * (j.connectedAnchor - ji.connectedBodyNode)
					+ ji.connectedBodyNode;
				j.targetPosition = ji.cjm.tgtPos0;

				ji.cjm.setup();
			}
		}

		private bool staticizeOrgInfo()
		{
			if (jm.joint.isOffTree()) {
				log(jm.desc(), ": skip staticizeOrgInfo(), off tree");
				return false;
			}
			float angle = pos;
			Vector3 nodeAxis = -axis.STd(hostPart, hostPart.vessel.rootPart);
			Quaternion nodeRot = nodeAxis.rotation(angle);
			Vector3 nodePos = node.STp(hostPart, hostPart.vessel.rootPart);
			_propagate(hostPart, nodeRot, nodePos);
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

