using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;

namespace DockRotate
{
	public class RotationAnimation
	{
		private ModuleDockRotate rotationModule;
		private PartJoint joint;

		public float pos, tgt, vel;
		private float maxvel, maxacc;

		private Guid vesselId;
		private Part startParent;

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
			return ModuleDockRotate.lprint(msg);
		}

		public RotationAnimation(ModuleDockRotate rotationModule, float pos, float tgt, float maxvel)
		{
			this.rotationModule = rotationModule;
			this.joint = rotationModule.rotatingJoint;

			this.vesselId = rotationModule.part.vessel.id;
			this.startParent = rotationModule.part.parent;

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

		public static void resetCount(Guid vesselId)
		{
			vesselRotCount.Remove(vesselId);
		}

		public void advance(float deltat)
		{
			if (rotationModule.part.parent != startParent)
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
			joint.Host.vessel.releaseAllAutoStruts();
			int c = joint.joints.Count;
			rji = new RotJointInfo[c];
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = joint.joints[i];

				rji[i].localToJoint = j.localToJoint();
				rji[i].jointToLocal = rji[i].localToJoint.inverse();
				rji[i].jointAxis = ModuleDockRotate.Td(rotationModule.partNodeAxis,
					ModuleDockRotate.T(rotationModule.part),
					ModuleDockRotate.T(joint.joints[i]));
				rji[i].startTgtRotation = j.targetRotation;
				rji[i].startTgtPosition = j.targetPosition;

				ConfigurableJointMotion f = ConfigurableJointMotion.Free;
				j.angularXMotion = f;
				j.angularYMotion = f;
				j.angularZMotion = f;
				j.xMotion = f;
				j.yMotion = f;
				j.zMotion = f;
			}
			lprint(rotationModule.part.desc() + ": started "
				+ pos + "\u00b0 -> " + tgt + "\u00b0, "
				+ maxvel + "\u00b0/s");
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

			// first rough attempt for electricity consumption
			if (deltat > 0) {
				double el = rotationModule.part.RequestResource("ElectricCharge", 1.0 * deltat);
				if (el <= 0.0)
					abort(false, "no electric charge");
			}
		}

		private void onStop()
		{
			// lprint("stop rot axis " + currentRotation(0).desc());
			pos = tgt;
			onStep(0);

			for (int i = 0; i < joint.joints.Count; i++) {
				Quaternion jointRot = Quaternion.AngleAxis(tgt, rji[i].jointAxis);
				ConfigurableJoint j = joint.joints[i];
				if (j) {
					// staticize rotation
					j.axis = jointRot * j.axis;
					j.secondaryAxis = jointRot * j.secondaryAxis;
					j.targetRotation = rji[i].startTgtRotation;

					// staticize target anchors
					Vector3 tgtAxis = ModuleDockRotate.Td(rotationModule.proxyRotationModule.partNodeAxis,
						ModuleDockRotate.T(rotationModule.proxyRotationModule.part),
						ModuleDockRotate.T(rotationModule.proxyRotationModule.part.rb));
					Quaternion tgtRot = Quaternion.AngleAxis(pos, tgtAxis);
					j.connectedAnchor = tgtRot * j.connectedAnchor;
					j.targetPosition = rji[i].startTgtPosition;
				}
			}
			if (decCount() <= 0) {
				lprint("securing autostruts on vessel " + vesselId);
				joint.Host.vessel.secureAllAutoStruts();
			}
			lprint(rotationModule.part.desc() + ": rotation stopped");
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
			tgt = pos;
			vel = 0;
			if (hard)
				finished = true;
		}
	}

	public class ModuleDockRotate: PartModule
	{
		[UI_Toggle()]
		[KSPField(guiName = "#DCKROT_rotation", guiActive = true, guiActiveEditor = true, isPersistant = true)]
		public bool rotationEnabled = false;

		[KSPField(
			guiName = "#DCKROT_angle", guiUnits = "\u00b0", guiFormat = "0.00",
			guiActive = true, guiActiveEditor = true
		)]
		public float dockingAngle;

		[KSPField(
			guiName = "#DCKROT_status",
			guiActive = false, guiActiveEditor = false
		)]
		public String nodeStatus = "";

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

		[KSPField(
			isPersistant = true
		)]
		public float maxSpeed = 90;

		[UI_Toggle(affectSymCounterparts = UI_Scene.None)]
		[KSPField(guiActive = true, isPersistant = true, guiName = "#DCKROT_reverse_rotation")]
		public bool reverseRotation = false;

		[KSPAction(guiName = "#DCKROT_rotate_clockwise", requireFullControl = true)]
		public void RotateClockwise(KSPActionParam param)
		{
			ModuleDockRotate tgt = actionTarget();
			if (tgt)
				tgt.RotateClockwise();
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_clockwise",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateClockwise()
		{
			if (canStartRotation()) {
				float s = rotationStep;
				if (reverseRotation)
					s = -s;
				activeRotationModule.enqueueRotation(s, rotationSpeed);
			}
		}

		[KSPAction(guiName = "#DCKROT_rotate_counterclockwise", requireFullControl = true)]
		public void RotateCounterclockwise(KSPActionParam param)
		{
			ModuleDockRotate tgt = actionTarget();
			if (tgt)
				tgt.RotateCounterclockwise();
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_counterclockwise",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateCounterclockwise()
		{
			if (canStartRotation()) {
				float s = -rotationStep;
				if (reverseRotation)
					s = -s;
				activeRotationModule.enqueueRotation(s, rotationSpeed);
			}
		}

		[KSPAction(guiName = "#DCKROT_rotate_to_snap", requireFullControl = true)]
		public void RotateToSnap(KSPActionParam param)
		{
			ModuleDockRotate tgt = actionTarget();
			if (tgt)
				tgt.RotateToSnap();
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_to_snap",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateToSnap()
		{
			if (!canStartRotation())
				return;
			activeRotationModule.enqueueRotationToSnap(rotationStep, rotationSpeed);
		}

		[KSPEvent(
			guiName = "#DCKROT_toggle_autostrut_display",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void ToggleAutostrutDisplay()
		{
			PhysicsGlobals.AutoStrutDisplay = !PhysicsGlobals.AutoStrutDisplay;
		}

#if DEBUG
		[KSPEvent(
			guiName = "#DCKROT_dump",
			guiActive = true,
			guiActiveEditor = false
		)]
		public void Dump()
		{
			dumpPart();
		}
#endif

		// things to be set up by stagedSetup()
		// the active module of the couple is the farthest from the root part
		// the proxy module of the couple is the closest to the root part

		private int vesselPartCount;
		private ModuleDockingNode dockingNode;
		public string nodeRole = "Init";
		private string lastNodeState = "Init";
		private Part lastSameVesselDockPart;
		public ModuleDockRotate activeRotationModule;
		public ModuleDockRotate proxyRotationModule;
		public PartJoint rotatingJoint;
		private Vector3 partNodePos; // node position, relative to part
		public Vector3 partNodeAxis; // node rotation axis, relative to part, reference Vector3.forward
		private Vector3 partNodeUp; // node vector for measuring angle, relative to part

		private int setupStageCounter = 0;

		private void resetVessel(string msg)
		{
			bool reset = false;
			List<ModuleDockRotate> rotationModules = vessel.FindPartModulesImplementing<ModuleDockRotate>();
			for (int i = 0; i < rotationModules.Count; i++) {
				ModuleDockRotate m = rotationModules[i];
				if (m.setupStageCounter != 0) {
					// lprint("reset " + descPart(m.part));
					reset = true;
					m.setupStageCounter = 0;
				}
			}
			if (reset && msg.Length > 0)
				lprint(part.desc() + " resets vessel: " + msg);
			RotationAnimation.resetCount(part.vessel.id);
		}

		private void stagedSetup()
		{
			if (onRails || !part || !vessel)
				return;

			if (rotCur != null)
				return;

			bool performedSetupStage = true;

			switch (setupStageCounter) {

				default:
					performedSetupStage = false;
					break;

				case 0:
					rotationStep = Mathf.Abs(rotationStep);
					rotationSpeed = Mathf.Abs(rotationSpeed);

					dockingNode = null;
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
						partNodePos = Tp(Vector3.zero, T(dockingNode), T(part));
						partNodeAxis = Td(Vector3.forward, T(dockingNode), T(part));
						partNodeUp = Td(Vector3.up, T(dockingNode), T(part));
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

			if (performedSetupStage) {
				// lprint ("setup(" + descPart (part) + "): step " + setupStageCounter + " done");
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
				&& (partNodePos - Tp(parentRotate.partNodePos, T(parentRotate.part), T(part))).magnitude < 1.0f
				&& Vector3.Angle(partNodeAxis, Td(Vector3.back, T(parentNode), T(part))) < 3;

			// lprint("isActive(" + descPart(part) + ") = " + ret);

			return ret;
		}

		private bool hasGoodState(ModuleDockingNode node)
		{
			if (!node || node.state == null)
				return false;
			string s = node.state;
			bool ret = s.StartsWith("Docked") || s == "PreAttached";
			// lprint("hasGoodState(" + s + ") = " + ret);
			return ret;
		}

		private RotationAnimation rotCur = null;

		private bool onRails;

		private bool canStartRotation()
		{
			return !onRails
				&& rotationEnabled
				&& activeRotationModule
				&& vessel
				&& vessel.CurrentControlLevel == Vessel.ControlLevel.FULL;
		}

		private int countJoints()
		{
			if (!activeRotationModule)
				return 0;
			if (!activeRotationModule.rotatingJoint)
				return 0;
			return activeRotationModule.rotatingJoint.joints.Count;
		}

		private float rotationAngle(bool dynamic)
		{
			if (!activeRotationModule || !proxyRotationModule)
				return float.NaN;

			Vector3 a = activeRotationModule.partNodeAxis;
			Vector3 v1 = activeRotationModule.partNodeUp;
			Vector3 v2 = dynamic ?
				Td(proxyRotationModule.partNodeUp, T(proxyRotationModule.part), T(activeRotationModule.part)) :
				STd(proxyRotationModule.partNodeUp, proxyRotationModule.part, activeRotationModule.part);
			v2 = Vector3.ProjectOnPlane(v2, a).normalized;

			float angle = Vector3.Angle(v1, v2);
			float axisAngle = Vector3.Angle(a, Vector3.Cross(v2, v1));

			return (axisAngle > 90) ? angle : -angle;
		}

		private static char[] guiListSep = { '.' };

		private static string[] guiList = {
			// F: is a KSPField;
			// E: is a KSPEvent;
			// e: show in editor;
			// R: hide when rotating;
			// D: show only with debugMode activated
			"dockingAngle.F",
			"nodeRole.F",
			"rotationStep.Fe",
			"rotationSpeed.Fe",
			"reverseRotation.Fe",
			"RotateClockwise.E",
			"RotateCounterclockwise.E",
			"RotateToSnap.E",
			"ToggleAutostrutDisplay.E"
		};

		private void checkGuiActive()
		{
			int i;

			dockingAngle = rotationAngle(true);

			bool newGuiActive = canStartRotation();

			nodeStatus = "";

			int nJoints = countJoints();
			nodeStatus = nodeRole + " [" + nJoints + "]";

			Fields["nodeStatus"].guiActive = nodeStatus.Length > 0;

			for (i = 0; i < guiList.Length; i++) {
				string[] spec = guiList[i].Split(guiListSep);
				if (spec.Length != 2) {
					lprint("bad guiList entry " + guiList[i]);
					continue;
				}

				string name = spec[0];
				string flags = spec[1];

				bool editorGui = flags.IndexOf('e') >= 0;

				bool thisGuiActive = newGuiActive;

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
						if (flags.IndexOf('R') >= 0 && activeRotationModule && activeRotationModule.rotCur != null)
							thisGuiActive = false;
						ev.guiActive = thisGuiActive;
						ev.guiActiveEditor = thisGuiActive && editorGui;
						if (name == "ToggleAutostrutDisplay") {
							ev.guiName = PhysicsGlobals.AutoStrutDisplay ? "#DCKROT_hide_autostruts" : "#DCKROT_show_autostruts";
						}
					}
				} else {
					lprint("bad guiList flags " + guiList[i]);
					continue;
				}
			}

			// setMaxSpeed();
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

		public void OnVesselGoOnRails(Vessel v)
		{
			if (v != vessel)
				return;
			// lprint("OnVesselGoOnRails()");
			onRails = true;
			resetVessel("go on rails");
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (v != vessel)
				return;
			// lprint("OnVesselGoOffRails()");
			onRails = false;
			resetVessel("go off rails");
		}

		public override void OnStart(StartState state)
		{
			// lprint(part.desc() + ".OnStart(" + state + ")");
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

		public void FixedUpdate()
		{
			if (HighLogic.LoadedScene != GameScenes.FLIGHT)
				return;

			string resetMsg = "";

			if (vessel && vessel.parts.Count != vesselPartCount)
				resetMsg = "part count changed";

			if (dockingNode && dockingNode.state != lastNodeState) {
				ModuleDockingNode other = dockingNode.otherNode();
				lprint(part.desc() + ": from " + lastNodeState
					+ " to " + dockingNode.state
					+ " with " + (other ? other.part.desc() : "none"));
				if (other && other.vessel == vessel) {
					if (rotCur != null)
						lprint(part.desc() + ": same vessel, not stopping");
				} else {
					resetMsg = "docking port state changed";
					if (rotCur != null)
						rotCur.abort(false, resetMsg);
				}
				lastNodeState = dockingNode.state;
			}

			Part svdp = (dockingNode && dockingNode.sameVesselDockJoint) ?
				dockingNode.sameVesselDockJoint.Target : null;
			if (dockingNode && rotCur == null && svdp != lastSameVesselDockPart) {
				resetMsg = "changed same vessel joint";
				lastSameVesselDockPart = svdp;
			}

			if (resetMsg.Length > 0)
				resetVessel(resetMsg);

			if (rotCur != null)
				advanceRotation(Time.fixedDeltaTime);

			stagedSetup();
		}

		private void enqueueRotation(float angle, float speed)
		{
			if (activeRotationModule != this) {
				lprint("enqueueRotation() called on wrong module, ignoring");
				return;
			}

			lprint(part.desc() + ": enqueueRotation(" + angle + "\u00b0, " + speed + "\u00b0/s)");
			if (speed < 0.5)
				return;

			if (rotCur != null) {
				rotCur.tgt += angle;
				lprint(part.desc() + ": rotation updated");
			} else {
				rotCur = new RotationAnimation(this, 0, angle, speed);
			}
		}

		private void enqueueRotationToSnap(float snap, float speed)
		{
			snap = Mathf.Abs(snap);
			if (snap < 0.5)
				return;

			float a = rotCur == null ? rotationAngle(true) : rotCur.tgt;
			if (float.IsNaN(a))
				return;
			float f = snap * Mathf.Floor(a / snap + 0.5f);
			lprint(part.desc() + ": snap " + a + " to " + f + " (" + (f - a) + ")");
			enqueueRotation(f - a, rotationSpeed);
		}

		private void staticizeRotation(RotationAnimation rot)
		{
			if (rot == null)
				return;
			if (activeRotationModule != this)
				return;
			if (rotatingJoint != part.attachJoint) {
				lprint(part.desc() + ": skip staticize, same vessel joint");
				return;
			}
			float angle = rot.tgt;
			Vector3 nodeAxis = STd(proxyRotationModule.partNodeAxis, proxyRotationModule.part, vessel.rootPart);
			Quaternion nodeRot = Quaternion.AngleAxis(angle, nodeAxis);
			_propagate(part, nodeRot);
		}

		private void _propagate(Part p, Quaternion rot)
		{
			Vector3 dp = p.orgPos - part.orgPos;
			Vector3 rdp = rot * dp;
			Vector3 newPos = rdp + part.orgPos;
			p.orgPos = newPos;

			p.orgRot = rot * p.orgRot;

			for (int i = 0; i < p.children.Count; i++)
				_propagate(p.children[i], rot);
		}

		private void advanceRotation(float deltat)
		{
			if (rotCur == null)
				return;
			if (rotCur.done()) {
				staticizeRotation(rotCur);
				rotCur = null;
				return;
			}

			if (activeRotationModule != this) {
				lprint("advanceRotation() called on wrong module, aborting");
				if (rotCur != null)
					rotCur.abort(false, "wrong module");
				return;
			}

			if (!part.attachJoint || !part.attachJoint.Joint) {
				lprint(part.desc() + " detached, aborting rotation");
				if (rotCur != null)
					rotCur.abort(true, "detached");
				return;
			}

			rotCur.advance(deltat);
		}

		private ModuleDockRotate actionTarget()
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

		/******** Reference change utilities - dynamic ********/

		public static Vector3 Td(Vector3 v, Transform from, Transform to)
		{
			return to.InverseTransformDirection(from.TransformDirection(v));
		}

		public static Vector3 Tp(Vector3 v, Transform from, Transform to)
		{
			return to.InverseTransformPoint(from.TransformPoint(v));
		}

		public static Transform T(Vessel v)
		{
			return v.rootPart.transform;
		}

		public static Transform T(Part p)
		{
			return p.transform;
		}

		public static Transform T(ConfigurableJoint j)
		{
			return j.transform;
		}

		public static Transform T(Rigidbody b)
		{
			return b.transform;
		}

		public static Transform T(ModuleDockingNode m)
		{
			return m.nodeTransform;
		}

		/******** Reference change utilities - static ********/

		private static Vector3 STd(Vector3 v, Part from, Part to)
		{
			return Quaternion.Inverse(to.orgRot) * (from.orgRot * v);
		}

		private static Vector3 STp(Vector3 v, Part from, Part to)
		{
			// untested yet
			Vector3 vv = from.orgPos + from.orgRot * v;
			return Quaternion.Inverse(to.orgRot) * (vv - to.orgPos);
		}

		/******** Debugging stuff ********/

		public static bool lprint(string msg)
		{
			print("[DockRotate]: " + msg);
			return true;
		}

		private void dumpJoint(ConfigurableJoint j)
		{
			// Quaternion localToJoint = j.localToJoint();

			lprint("  Link: " + j.gameObject + " to " + j.connectedBody);
			// lprint("  autoConf: " + j.autoConfigureConnectedAnchor);
			// lprint("  swap: " + j.swapBodies);
			lprint("  Axes: " + j.axis.desc() + ", " + j.secondaryAxis.desc());
			// lprint("  localToJoint: " + localToJoint.desc());
			lprint("  Anchors: " + j.anchor.desc()
				+ " -> " + j.connectedAnchor.desc()
				+ " [" + Tp(j.connectedAnchor, T(j.connectedBody), T(j)).desc() + "]");

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
			// lprint("  TgtPosP: " + Tp(j.targetPosition, T(j), T(part))); - FIXME

			/* FIXME
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

		private void dumpJoint(PartJoint j)
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
				dumpJoint(j.joints[i]);
			}
		}

		private void dumpPart() {
			lprint("--- DUMP " + part.desc() + " ---");
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

				lprint("partNodeAxisV: " + STd(partNodeAxis, part, vessel.rootPart).desc());

				lprint("rot: static " + rotationAngle(false) + ", dynamic " + rotationAngle(true));
			}

			if (rotatingJoint) {
				lprint(rotatingJoint == part.attachJoint ? "parent joint:" : "same vessel joint:"); 
				dumpJoint(rotatingJoint);
			}

			lprint("--------------------");
		}
	}

	public static class Extensions
	{
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
			ModuleDockRotate mdr = part.FindModuleImplementing<ModuleDockRotate>();
			return part.name + "_" + part.flightID
				+ (mdr ? "_" + mdr.nodeRole : "");
		}

		/******** ModuleDockingMode utilities ********/

		public static ModuleDockingNode otherNode(this ModuleDockingNode node)
		{
			// this prevents a warning
			if (node.dockedPartUId <= 0)
				return null;
			return node.FindOtherNode();
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

		public static void disable(this ConfigurableJoint joint)
		{
			ConfigurableJointMotion f = ConfigurableJointMotion.Free;
			JointDrive d = joint.angularXDrive;
			d.positionSpring = 0;
			d.positionDamper = 0;
			d.maximumForce = 1e20f;
			joint.angularXMotion = f;
			joint.angularXDrive = d;
			joint.angularYMotion = f;
			joint.angularZMotion = f;
			joint.angularYZDrive = d;
			joint.xMotion = f;
			joint.yMotion = f;
			joint.zMotion = f;
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
	}
}

