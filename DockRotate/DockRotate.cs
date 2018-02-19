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

		public Vector3[] jointRotationAxis;
		public Quaternion[] startRotation;
		public Vector3[] startPosition;
		private bool started = false, finished = false;

		const float accelTime = 2.0f;
		const float stopMargin = 1.5f;

		public static bool lprint(string msg)
		{
			return ModuleDockRotate.lprint(msg);
		}

		public RotationAnimation(ModuleDockRotate rotationModule, float pos, float tgt, float maxvel)
		{
			this.rotationModule = rotationModule;
			this.joint = rotationModule.part.attachJoint;

			this.pos = pos;
			this.tgt = tgt;
			this.maxvel = maxvel;

			this.vel = 0;
			this.maxacc = maxvel / accelTime;
		}

		public void advance(float deltat)
		{
			if (finished)
				return;
			if (!started) {
				onStart();
				started = true;
				lprint("rotation started (" + pos + ", " + tgt + ")");
			}

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
			int c = joint.joints.Count;
			jointRotationAxis = new Vector3[c];
			startRotation = new Quaternion[c];
			startPosition = new Vector3[c];
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = joint.joints[i];
				jointRotationAxis[i] = rotationModule.jointRotationAxis(j);
				startRotation[i] = j.targetRotation;
				startPosition[i] = j.targetPosition;
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
				j.targetRotation = rot;
				// j.targetPosition = rot * j.anchor - j.anchor;
				// lprint("adv " + j.targetRotation.eulerAngles + " " + j.targetPosition);
				// joint.joints[i].anchor = rot * joint.joints[i].anchor;
				// joint.joints[i].connectedAnchor = rot * joint.joints[i].connectedAnchor;
			}

			// first rough attempt for electricity consumption
			double el = rotationModule.part.RequestResource("ElectricCharge", 1.0 * deltat);
			if (el <= 0.0)
				abort();
		}

		private void onStop()
		{
			lprint("stop rot axis " + currentRotation(0).desc());
			pos = tgt;
			onStep(0);
			/*
			rotatingJoint.angularXMotion = savedXMotion;
			for (int i = 0; i < joint.joints.Count; i++)
				ModuleDockRotate.lprint("restored XMotion " + joint.joints[i].angularXMotion);
			*/
		}

		public Quaternion currentRotation(int i)
		{
			// the proxy inline rotation bug is here!
			// newRotation must be computed according to joint axis

			// return axis for axial on axial must be Vector3.right = (1, 0, 0)
			// return axis for inline on axial must be Vector3.right = (1, 0, 0)
			// return axis for axial on inline must be Vector3.down = (0, -1, 0)
			// return axis for inline on inline must be Vector3.back = (0, 0, -1)

			// partNodeAxis for axial ports is Vector3.up = (0, 1, 0)
			// partNodeAxis for inline ports is Vector3.back = (0, 0, -1)

			// for active part:
			// otherPartNodeAxis for axial on axial good is (0, -1, 0)
			// otherPartNodeAxis for inline on axial good is (0, 0, 1)
			// otherPartNodeAxis for axial on inline bad is (0, -1, 0)
			// otherPartNodeAxis for inline on inline bad is (0, 0, 1)

			// for proxy part:
			// otherPartNodeAxis for axial on axial good is (0, -1, 0)
			// otherPartNodeAxis for inline on axial good is (0, -1, 0)
			// otherPartNodeAxis for axial on inline bad is (0, 0, 1)
			// otherPartNodeAxis for inline on inline bad is (0, 0, 1)

			// Quaternion newRotation = Quaternion.Euler(new Vector3(pos, 0, 0));
			ModuleDockRotate referenceModule = rotationModule.proxyRotationModule;
			Vector3 rotationAxis = referenceModule.partNodeAxis;
			rotationAxis = Vector3.right;
			Quaternion newRotation = Quaternion.AngleAxis(pos, rotationAxis);
			return startRotation[i] * newRotation;
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

		public void abort()
		{
			tgt = pos;
			vel = 0;
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

#if DEBUG
		[KSPField(
			guiName = "Role",
			guiActive = true, guiActiveEditor = true
		)]
#endif
		public String nodeRole = "none";

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
			float a = rotationAngle(false);
			float f = rotationStep * Mathf.Floor(a / rotationStep + 0.5f);
			lprint("snap " + a + " to " + f + " (" + (f - a) + ")");
			activeRotationModule.enqueueRotation(f - a, rotationSpeed);
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
		private string lastNodeState = "-";
		public ModuleDockRotate otherRotationModule;
		public ModuleDockRotate activeRotationModule;
		public ModuleDockRotate proxyRotationModule;
		private Vector3 partNodePos; // node position, relative to part
		public Vector3 partNodeAxis; // node rotation axis, relative to part
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
		}

		private void stagedSetup()
		{
			if (onRails || !part || !vessel)
				return;

			if (rotCur != null && setupStageCounter > 0)
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
					otherRotationModule = activeRotationModule = proxyRotationModule = null;
					nodeRole = "-";
					partNodePos = partNodeAxis = partNodeUp = new Vector3(9.9f, 9.9f, 9.9f);

					vesselPartCount = vessel ? vessel.parts.Count : -1;
					lastNodeState = "-";

					dockingNode = part.FindModuleImplementing<ModuleDockingNode>();
					if (dockingNode) {
						partNodePos = Tp(Vector3.zero, T(dockingNode), T(part));
						partNodeAxis = Td(Vector3.forward, T(dockingNode), T(part));
						partNodeUp = Td(Vector3.up, T(dockingNode), T(part));
						lastNodeState = dockingNode.state;
					}
					break;

				case 1:
					if (!dockingNode)
						break;

					if (isActive()) {
						activeRotationModule = this;
						proxyRotationModule = otherRotationModule = part.parent.FindModuleImplementing<ModuleDockRotate>();
						nodeRole = "Active";
					} else {
						for (int i = 0; i < part.children.Count; i++) {
							Part p = part.children[i];
							ModuleDockRotate dr = p.FindModuleImplementing<ModuleDockRotate>();
							if (dr && dr.isActive()) {
								activeRotationModule = otherRotationModule = dr;
								break;
							}
						}
						if (activeRotationModule) {
							proxyRotationModule = this;
							nodeRole = "Proxy";
						} else {
							nodeRole = "None";
						}
					}

					string status = "inactive";
					if (activeRotationModule == this) {
						status = "active";
					} else if (activeRotationModule) {
						status = "proxy to " + activeRotationModule.part.desc();
					}
					lprint("setup(" + part.desc() + "): " + status);

					break;
			}

			if (performedSetupStage) {
				// lprint ("setup(" + descPart (part) + "): step " + setupStageCounter + " done");
			}

			setupStageCounter++;
		}

		private bool isActive() // must be used only after setup stage 0;
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

		bool hasGoodState(ModuleDockingNode node)
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

		private float rotationAngle(bool dynamic)
		{
			if (!activeRotationModule || !proxyRotationModule)
				return float.NaN;

			Vector3 v1 = activeRotationModule.partNodeUp;
			Vector3 v2 = dynamic ?
				Td(proxyRotationModule.partNodeUp, T(proxyRotationModule.part), T(activeRotationModule.part)) :
				STd(proxyRotationModule.partNodeUp, proxyRotationModule.part, activeRotationModule.part);
			Vector3 a = activeRotationModule.partNodeAxis;

			float angle = Vector3.Angle(v1, v2);
			float axisAngle = Vector3.Angle(a, Vector3.Cross(v2, v1));

			return (axisAngle > 90) ? angle : -angle;
		}

		public Vector3 jointRotationAxis(ConfigurableJoint joint)
		{
			return Td(proxyRotationModule.partNodeAxis, T(proxyRotationModule.part), T(joint));
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

		private void setMaxSpeed()
		{
			UI_Control ctl;
			ctl = Fields["rotationSpeed"].uiControlEditor;
			if (ctl != null) {
				lprint("setting editor " + ctl + " at " + maxSpeed);
				((UI_FloatRange) ctl).maxValue = Mathf.Abs(maxSpeed);
			}
			ctl = Fields["rotationSpeed"].uiControlFlight;
			if (ctl != null) {
				lprint("setting flight " + ctl + " at " + maxSpeed);
				((UI_FloatRange) ctl).maxValue = Mathf.Abs(maxSpeed);
			}
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
				lprint(part.desc() + " changed from " + lastNodeState + " to " + dockingNode.state);
				lastNodeState = dockingNode.state;
				needReset = true;
			}

			if (needReset)
				resetVessel();

			if (rotCur != null)
				advanceRotation(Time.fixedDeltaTime);

			stagedSetup();
		}

		void enqueueRotation(float angle, float speed)
		{
			if (activeRotationModule != this) {
				lprint("activeRotationModule() called on wrong module, ignoring");
				return;
			}

			lprint(part.desc() + ": enqueueRotation(" + angle + ", " + speed + ")");

			if (rotCur != null) {
				rotCur.tgt += angle;
				lprint(part.desc() + ": rotation updated");
			} else {
				rotCur = new RotationAnimation(this, 0, angle, speed);
			}
		}

		private void staticizeRotation(RotationAnimation rot)
		{
			float angle = rot.tgt;
			Vector3 nodeAxis = STd(proxyRotationModule.partNodeAxis, proxyRotationModule.part, vessel.rootPart);
			Quaternion nodeRot = Quaternion.AngleAxis(angle, nodeAxis);
			// lprint("staticize " + nodeRot.eulerAngles);
			_propagate(part, nodeRot);

			lprint("staticize joint axis: " + nodeAxis);
			PartJoint joint = part.attachJoint;
			for (int i = 0; i < joint.joints.Count; i++) {
				joint.joints[i].secondaryAxis = nodeRot * part.attachJoint.Joint.secondaryAxis;
				joint.joints[i].targetRotation = Quaternion.identity;
				// lprint("CHKROT: " + Quaternion.identity + " " + rot.startRotation[i]);
			}
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
			if (activeRotationModule != this) {
				lprint("advanceRotation() called on wrong module, ignoring");
				return;
			}

			if (!part.attachJoint || !part.attachJoint.Joint) {
				lprint("detached, aborting rotation");
				rotCur = null;
				return;
			}

			rotCur.advance(deltat);

			if (rotCur.done()) {
				lprint(part.desc() + ": rotation finished");
				staticizeRotation(rotCur);
				rotCur = null;
			}
		}

		public void disableJoint(ConfigurableJoint joint)
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

		/******** Reference change utilities - dynamic ********/

		private static Vector3 Td(Vector3 v, Transform from, Transform to)
		{
			return to.InverseTransformDirection(from.TransformDirection(v));
		}

		private static Vector3 Tp(Vector3 v, Transform from, Transform to)
		{
			return to.InverseTransformPoint(from.TransformPoint(v));
		}

		private static Transform T(Vessel v)
		{
			return v.rootPart.transform;
		}

		private static Transform T(Part p)
		{
			return p.transform;
		}

		private static Transform T(ConfigurableJoint j)
		{
			return j.transform;
		}

		private static Transform T(Rigidbody b)
		{
			return b.transform;
		}

		private static Transform T(ModuleDockingNode m)
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

		private void dumpJoint(ConfigurableJoint joint)
		{
			lprint("  link: " + joint.gameObject + " to " + joint.connectedBody);
			lprint("  autoConf: " + joint.autoConfigureConnectedAnchor);
			lprint("  swap: " + joint.swapBodies);
			lprint("  axis: " + joint.axis);
			lprint("  secAxis: " + joint.secondaryAxis);
			lprint("  thdAxis: " + Vector3.Cross(joint.axis, joint.secondaryAxis));
			lprint("  axisV: " + Td(joint.axis, T(joint), T(vessel.rootPart)));
			lprint("  secAxisV: " + Td(joint.secondaryAxis, T(joint), T(vessel.rootPart)));
			lprint("  jSpacePartAxis: " + Td(partNodeAxis, T(part), T(joint)));

			/*
			lprint("  AXMot: " + joint.angularXMotion);
			lprint("  LAXLim: " + joint.lowAngularXLimit.desc());
			lprint("  HAXLim: " + joint.highAngularXLimit.desc());
			lprint("  AXDrv: " + joint.angularXDrive.desc());
			lprint("  TgtRot: " + joint.targetRotation.eulerAngles);

			lprint("  YMot: " + joint.yMotion);
			lprint("  YDrv: " + joint.yDrive);
			lprint("  ZMot: " + joint.zMotion);
			lprint("  ZDrv: " + joint.zDrive.desc());
			*/

			lprint("  TgtRot: " + joint.targetRotation.desc());
			lprint("  TgtRotAxisP: " + Td(joint.targetRotation.axis(), T(joint), T(part)));

			lprint("  TgtPos: " + joint.targetPosition);
			lprint("  TgtPosP: " + Tp(joint.targetPosition, T(joint), T(part)));
			lprint("  Anchors: " + joint.anchor + " " + joint.connectedAnchor);
			lprint("  AnchorsP: " + Tp(joint.anchor, T(joint), T(part))
				+ " " + Tp(joint.connectedAnchor, T(joint.connectedBody), T(part)));

			// lprint("Joint YMot:   " + joint.Joint.angularYMotion);
			// lprint("Joint YLim:   " + descLim(joint.Joint.angularYLimit));
			// lprint("Joint aYZDrv: " + descDrv(joint.Joint.angularYZDrive));
			// lprint("Joint RMode:  " + joint.Joint.rotationDriveMode);
		}

		private void dumpJoint(PartJoint joint)
		{
			// lprint("Joint Parent: " + descPart(joint.Parent));
			lprint("jChild: " + joint.Child.desc());
			lprint("jHost: " + joint.Host.desc());
			lprint("jTarget: " + joint.Target.desc());
			lprint("jAxis: " + joint.Axis);
			lprint("jSecAxis: " + joint.SecAxis);
			for (int i = 0; i < joint.joints.Count; i++) {
				lprint("ConfigurableJoint[" + i + "]:");
				dumpJoint(joint.joints[i]);
			}
		}

		private void dumpPart() {
			lprint("--- DUMP " + part.desc() + " ---");
			/*
			lprint("mass: " + part.mass);
			lprint("parent: " + descPart(part.parent));
			*/
			lprint("orgPos: " + part.orgPos);
			lprint("orgRot: " + part.orgRot.desc());

			if (dockingNode) {
				lprint("size: " + dockingNode.nodeType);
				lprint("state: " + dockingNode.state);

				ModuleDockingNode other = dockingNode.dockedPartUId != 0 ? dockingNode.FindOtherNode() : null;
				lprint("other: " + (other ? other.part.desc() : "none"));

				/*
				lprint("partNodePos: " + partNodePos);
				lprint("partNodeAxis: " + partNodeAxis);
				lprint("partNodeUp: " + partNodeUp);
				*/

				lprint("partNodeAxisV: " + STd(partNodeAxis, part, vessel.rootPart));

				if (otherRotationModule) {
					// lprint("otherPartNodeAxis1: " + Td(otherRotationModule.partNodeAxis, T(otherRotationModule.part), T(part)));
					lprint("otherPartNodeAxis: " + STd(otherRotationModule.partNodeAxis, otherRotationModule.part, part));
				}

				lprint("rotSt: " + rotationAngle(false));
				lprint("rotDy: " + rotationAngle(true));
			}

			dumpJoint(part.attachJoint);

			lprint("--------------------");
		}
	}

	static class Extensions
	{
		/******** Part utilities ********/

		public static string desc(this Part part)
		{
			if (!part)
				return "<null>";
			return part.name + "_" + part.flightID;
		}

		/******** ConfigurableJoint utilities ********/

		public static Quaternion axisRotation(this ConfigurableJoint j)
		{
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

		/******** Quaternion utilities ********/

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

