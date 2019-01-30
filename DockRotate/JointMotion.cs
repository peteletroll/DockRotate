using System;
using UnityEngine;

namespace DockRotate
{
	public class JointMotion: SmoothMotion
	{
		public static implicit operator bool(JointMotion r)
		{
			return r != null;
		}

		private Part activePart, proxyPart;
		private Vector3 node, axis;
		private PartJoint joint;
		public bool smartAutoStruts = false;

		public ModuleBaseRotate controller = null;

		public const float pitchAlterationRateMax = 0.1f;
		public static string soundFile = "DockRotate/DockRotateMotor";
		public AudioSource sound;
		public float soundVolume = 1f;
		public float pitchAlteration = 1f;

		public float electricityRate = 1f;
		public float rot0 = 0f;

		private struct RotJointInfo
		{
			public ConfigurableJointManager jm;
			public Vector3 localAxis, localNode;
			public Vector3 jointAxis, jointNode;
			public Vector3 connectedBodyAxis, connectedBodyNode;
		}
		private RotJointInfo[] rji;

		private static bool lprint(string msg)
		{
			return ModuleBaseRotate.lprint(msg);
		}

		public JointMotion(Part part, Vector3 node, Vector3 axis, PartJoint joint, float pos, float tgt, float maxvel)
		{
			this.activePart = part;
			this.node = node;
			this.axis = axis;
			this.joint = joint;

			this.proxyPart = joint.Host == part ? joint.Target : joint.Host;

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
				activePart.vessel.releaseAllAutoStruts();
			}
			int c = joint.joints.Count;
			rji = new RotJointInfo[c];
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = joint.joints[i];

				RotJointInfo ji;

				ji.jm = new ConfigurableJointManager();
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
				if (!j)
					continue;
				RotJointInfo ji = rji[i];
				ji.jm.setRotation(pos, ji.localAxis, ji.localNode);
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
				double el = activePart.RequestResource("ElectricCharge", (double)electricityRate * deltat);
				electricity += el;
				if (el <= 0d) {
					lprint("no electricity, braking rotation");
					brake();
				}
			}
		}

		protected override void onStop()
		{
			// lprint("stop rot axis " + currentRotation(0).desc());

			stopSound();

			onStep(0);

			staticize();

			int c = VesselMotionManager.get(activePart).changeCount(0);
			lprint(activePart.desc() + ": rotation stopped [" + c + "], "
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

		public void staticize()
		{
			lprint("staticize() at pos = " + pos + "\u00b0");
			staticizeJoints();
			staticizeOrgInfo();
		}

		private void staticizeJoints()
		{
			for (int i = 0; i < joint.joints.Count; i++) {
				ConfigurableJoint j = joint.joints[i];
				if (j) {
					RotJointInfo ji = rji[i];

					// staticize joint rotation

					ji.jm.staticizeRotation();

					// FIXME: this should be moved to JointManager
					Quaternion connectedBodyRot = ji.connectedBodyAxis.rotation(-pos);
					j.connectedAnchor = connectedBodyRot * (j.connectedAnchor - ji.connectedBodyNode)
						+ ji.connectedBodyNode;
					j.targetPosition = ji.jm.tgtPos0;

					ji.jm.setup();
				}
			}
		}

		private bool staticizeOrgInfo()
		{
			if (joint != activePart.attachJoint) {
				lprint(activePart.desc() + ": skip staticize, same vessel joint");
				return false;
			}
			float angle = pos;
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
	}
}

