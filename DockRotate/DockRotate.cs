using System;
using System.Collections.Generic;
using UnityEngine;

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

		private Quaternion[] axisRotation;
		private Vector3[] jointAxis;
		private Quaternion[] startTgtRotation;
		private Vector3[] startTgtPosition;
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
				lprint("rotation started (" + pos + ", " + tgt + ") on vessel " + vesselId);
			}

			vel = newvel;
			pos += deltat * vel;

			onStep(deltat);

			if (!finished && done(deltat)) {
				onStop();
				// lprint("rotation stopped");
			}
		}

		private void onStart()
		{
			incCount();
			joint.Host.vessel.releaseAllAutoStruts();
			int c = joint.joints.Count;
			axisRotation = new Quaternion[c];
			jointAxis = new Vector3[c];
			startTgtRotation = new Quaternion[c];
			startTgtPosition = new Vector3[c];
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = joint.joints[i];
				axisRotation[i] = j.axisRotation();
				jointAxis[i] = ModuleDockRotate.Td(rotationModule.partNodeAxis,
					ModuleDockRotate.T(rotationModule.part),
					ModuleDockRotate.T(joint.joints[i]));
				startTgtRotation[i] = j.targetRotation;
				startTgtPosition[i] = j.targetPosition;
				ConfigurableJointMotion f = ConfigurableJointMotion.Free;
				j.angularXMotion = f;
				j.angularYMotion = f;
				j.angularZMotion = f;
				j.xMotion = f;
				j.yMotion = f;
				j.zMotion = f;
				if (i != 0) {
					JointDrive d = j.xDrive;
					d.positionSpring = 0;
					j.xDrive = d;
					j.yDrive = d;
					j.zDrive = d;
				}
			}
		}

		private void onStep(float deltat)
		{
			for (int i = 0; i < joint.joints.Count; i++) {
				ConfigurableJoint j = joint.joints[i];
				Quaternion rot = currentRotation(i);
				if (j) {
					j.targetRotation = rot;
					// j.targetPosition = rot * j.anchor - j.anchor;
					// lprint("adv " + j.targetRotation.eulerAngles + " " + j.targetPosition);
					// joint.joints[i].anchor = rot * joint.joints[i].anchor;
					// joint.joints[i].connectedAnchor = rot * joint.joints[i].connectedAnchor;
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
				Quaternion jointRot = Quaternion.AngleAxis(tgt, jointAxis[i]);
				ConfigurableJoint j = joint.joints[i];
				if (j) {
					j.axis = jointRot * j.axis;
					j.secondaryAxis = jointRot * j.secondaryAxis;
					j.targetRotation = startTgtRotation[i];
				}
			}
			if (decCount() <= 0) {
				lprint("securing autostruts on vessel " + vesselId);
				joint.Host.vessel.secureAllAutoStruts();
			}
		}

		private Quaternion currentRotation(int i)
		{
			Quaternion newJointRotation = Quaternion.AngleAxis(pos, jointAxis[i]);

			Quaternion rot = axisRotation[i].inverse()
				* newJointRotation * startTgtRotation[i]
				* axisRotation[i];

			return startTgtRotation[i] * rot;
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
		[KSPField(guiName = "Rotation", guiActive = true, guiActiveEditor = true, isPersistant = true)]
		public bool rotationEnabled = false;

		[KSPField(
			guiName = "Angle", guiUnits = "\u00b0", guiFormat = "0.00",
			guiActive = true, guiActiveEditor = true
		)]
		public float dockingAngle;

		[KSPField(
			guiName = "Status",
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
			guiName = "Rotation Step",
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
			guiName = "Rotation Speed",
			guiUnits = "\u00b0/s"
		)]
		public float rotationSpeed = 5;

		[KSPField(
			isPersistant = true
		)]
		public float maxSpeed = 90;

		[UI_Toggle(affectSymCounterparts = UI_Scene.None)]
		[KSPField(guiActive = true, isPersistant = true, guiName = "Reverse Rotation")]
		public bool reverseRotation = false;

		[KSPAction(guiName = "Rotate Clockwise (+)", requireFullControl = true)]
		public void RotateClockwise(KSPActionParam param)
		{
			RotateClockwise();
		}

		[KSPEvent(
			guiName = "Rotate Clockwise (+)",
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

		[KSPAction(guiName = "Rotate Counterclockwise (-)", requireFullControl = true)]
		public void RotateCounterclockwise(KSPActionParam param)
		{
			RotateCounterclockwise();
		}

		[KSPEvent(
			guiName = "Rotate Counterclockwise (-)",
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

		[KSPEvent(
			guiName = "Rotate to Snap",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateToSnap()
		{
			if (rotCur != null || !canStartRotation())
				return;
			activeRotationModule.enqueueRotationToSnap(rotationStep, rotationSpeed);
		}

		[KSPEvent(
			guiName = "Toggle Autostrut Display",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void ToggleAutostrutDisplay()
		{
			PhysicsGlobals.AutoStrutDisplay = !PhysicsGlobals.AutoStrutDisplay;
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
#endif

		// things to be set up by stagedSetup()
		// the active module of the couple is the farthest from the root part
		// the proxy module of the couple is the closest to the root part

		private int vesselPartCount;
		private ModuleDockingNode dockingNode;
		public string nodeRole = "-";
		private string lastNodeState = "-";
		private Part lastSameVesselDockPart;
		private ModuleDockRotate activeRotationModule;
		private ModuleDockRotate proxyRotationModule;
		public PartJoint rotatingJoint;
		private Vector3 partNodePos; // node position, relative to part
		public Vector3 partNodeAxis; // node rotation axis, relative to part, reference Vector3.forward
		private Vector3 partNodeUp; // node vector for measuring angle, relative to part

		private int setupStageCounter = 0;

		private void resetVessel()
		{
			List<ModuleDockRotate> rotationModules = vessel.FindPartModulesImplementing<ModuleDockRotate>();
			for (int i = 0; i < rotationModules.Count; i++) {
				ModuleDockRotate m = rotationModules[i];
				if (m.setupStageCounter != 0) {
					// lprint("reset " + descPart(m.part));
					m.setupStageCounter = 0;
				}
			}
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

					if (dockingNode.sameVesselDockJoint) {
						ModuleDockRotate otherModule = dockingNode.sameVesselDockJoint.Target.FindModuleImplementing<ModuleDockRotate>();
						if (otherModule) {
							activeRotationModule = this;
							proxyRotationModule = otherModule;
							rotatingJoint = dockingNode.sameVesselDockJoint;
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
						lprint("pair " + activeRotationModule.part.desc()
							+ " on " + proxyRotationModule.part.desc());
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
				&& countJoints() == 1
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
			"RotateToSnap.ER",
			"ToggleAutostrutDisplay.E"
		};

		private void checkGuiActive()
		{
			int i;

			dockingAngle = rotationAngle(true);

			bool newGuiActive = canStartRotation();

			nodeStatus = "";

			int nJoints = countJoints();
#if DEBUG
			nodeStatus = nodeRole + " [" + nJoints + "]";
#else
			if (nJoints > 1) {
				nodeStatus = "Can't Move, Try Redock [" + nJoints + "]";
				newGuiActive = false;
			}
#endif

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
							ev.guiName = PhysicsGlobals.AutoStrutDisplay ? "Hide Autostruts" : "Show Autostruts";
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
			lprint("OnAwake()");
			base.OnAwake();
			GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
		}

		public void OnDestroy()
		{
			lprint("OnDestroy()");
			GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
		}

		public void OnVesselGoOnRails(Vessel v)
		{
			if (v != vessel)
				return;
			// lprint("OnVesselGoOnRails()");
			onRails = true;
			resetVessel();
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (v != vessel)
				return;
			// lprint("OnVesselGoOffRails()");
			onRails = false;
			resetVessel();
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);
			if ((state & StartState.Editor) != 0)
				return;

			lprint(part.desc() + ".OnStart(" + state + ")");

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

			bool needReset = false;

			if (vessel && vessel.parts.Count != vesselPartCount)
				needReset = true;

			if (dockingNode && dockingNode.state != lastNodeState) {
				ModuleDockingNode other = dockingNode.otherNode();
				lprint(part.desc() + " changed from " + lastNodeState
					+ " to " + dockingNode.state
					+ " with " + (other ? other.part.desc() : "node"));
				if (other && other.vessel == vessel) {
					if (rotCur != null)
						lprint("same vessel, not stopping");
				} else {
					needReset = true;
					if (rotCur != null)
						rotCur.abort(false, "docking port state changed");
				}
				lastNodeState = dockingNode.state;
			}

			Part svdp = (dockingNode && dockingNode.sameVesselDockJoint) ? dockingNode.sameVesselDockJoint.Target : null;
			if (dockingNode && rotCur == null && svdp != lastSameVesselDockPart) {
				lprint(part.desc() + " changed same vessel joint");
				needReset = true;
				lastSameVesselDockPart = svdp;
			}

			if (needReset)
				resetVessel();

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

			lprint(part.desc() + ": enqueueRotation(" + angle + ", " + speed + ")");
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

			if (rotCur != null) {
				lprint("rotation active, can't enqueueRotationToSnap(" + snap + ", " + speed + ")");
				return;
			}
			float a = rotationAngle(true);
			if (float.IsNaN(a))
				return;
			float f = snap * Mathf.Floor(a / snap + 0.5f);
			lprint("snap " + a + " to " + f + " (" + (f - a) + ")");
			enqueueRotation(f - a, rotationSpeed);
		}

		private void staticizeRotation(RotationAnimation rot)
		{
			if (rot == null)
				return;
			if (activeRotationModule != this)
				return;
			if (rotatingJoint != part.attachJoint) {
				lprint("skip staticize: same vessel joint");
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
				lprint(part.desc() + ": rotation finished");
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
				lprint("detached, aborting rotation");
				if (rotCur != null)
					rotCur.abort(true, "detached");
				return;
			}

			rotCur.advance(deltat);
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
			lprint("  link: " + j.gameObject + " to " + j.connectedBody);
			lprint("  autoConf: " + j.autoConfigureConnectedAnchor);
			lprint("  swap: " + j.swapBodies);
			lprint("  axis: " + j.axis);
			lprint("  secAxis: " + j.secondaryAxis);

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

			// /*
			lprint("  AXMot: " + j.angularXMotion);
			lprint("  LAXLim: " + j.lowAngularXLimit.desc());
			lprint("  HAXLim: " + j.highAngularXLimit.desc());
			lprint("  AXDrv: " + j.angularXDrive.desc());
			lprint("  TgtRot: " + j.targetRotation.eulerAngles);

			lprint("  YMot: " + j.yMotion);
			lprint("  YDrv: " + j.yDrive);
			lprint("  ZMot: " + j.zMotion);
			lprint("  ZDrv: " + j.zDrive.desc());
			// */

			/*
			lprint("  TgtRot: " + joint.targetRotation.desc());
			lprint("  TgtRotAxisP: " + Td(joint.targetRotation.axis(), T(joint), T(part)));

			lprint("  TgtPos: " + joint.targetPosition);
			lprint("  TgtPosP: " + Tp(joint.targetPosition, T(joint), T(part)));
			lprint("  Anchors: " + joint.anchor + " " + joint.connectedAnchor);
			lprint("  AnchorsP: " + Tp(joint.anchor, T(joint), T(part))
				+ " " + Tp(joint.connectedAnchor, T(joint.connectedBody), T(part)));
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
			lprint("jChild: " + j.Child.desc());
			lprint("jHost: " + j.Host.desc());
			lprint("jTarget: " + j.Target.desc());
			lprint("jAxis: " + j.Axis);
			lprint("jSecAxis: " + j.SecAxis);
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
			lprint("role: " + nodeRole);
			lprint("status: " + nodeStatus);
			lprint("orgPos: " + part.orgPos);
			lprint("orgRot: " + part.orgRot.desc());

			if (dockingNode) {
				lprint("size: " + dockingNode.nodeType);
				lprint("state: " + dockingNode.state);

				ModuleDockingNode other = dockingNode.otherNode();
				lprint("other: " + (other ? other.part.desc() : "none"));

				/*
				lprint("partNodePos: " + partNodePos);
				lprint("partNodeAxis: " + partNodeAxis);
				lprint("partNodeUp: " + partNodeUp);
				*/

				lprint("partNodeAxisV: " + STd(partNodeAxis, part, vessel.rootPart));

				lprint("rot: static " + rotationAngle(false) + ", dynamic " + rotationAngle(true));
			}

			PartJoint svj = dockingNode.sameVesselDockJoint;
			if (svj) {
				lprint("sameVesselDockJoint:");
				dumpJoint(svj);
			}

			if (rotatingJoint)
				dumpJoint(rotatingJoint);

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
			if (node.dockedPartUId >= 0)
				return null;
			return node.FindOtherNode();
		}

		/******** ConfigurableJoint utilities ********/

		public static Quaternion axisRotation(this ConfigurableJoint j)
		{
			// the returned rotation turns Vector3.right to axis
			// and Vector3.up to secondaryAxis
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
			return angle.ToString("F2") + "\u00b0" + axis;
		}
	}
}

