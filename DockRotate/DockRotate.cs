using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class RotationAnimation
	{
		public float pos, tgt, vel;
		private float maxvel, maxacc;
		private PartJoint joint;
		public Quaternion[] startRotation;
		public Vector3[] startPosition;
		private bool started = false, finished = false;

		const float accelTime = 2.0f;
		const float stopMargin = 1.5f;

		static public void lprint(string msg)
		{
			ModuleDockRotate.lprint(msg);
		}

		public RotationAnimation(float pos, float tgt, float maxvel, PartJoint joint)
		{
			this.pos = pos;
			this.tgt = tgt;
			this.maxvel = maxvel;
			this.joint = joint;

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

			onStep();

			if (!finished && done(deltat)) {
				onStop();
				lprint("rotation stopped");
			}
		}

		private void onStart()
		{
			int c = joint.joints.Count;
			startRotation = new Quaternion[c];
			startPosition = new Vector3[c];
			for (int i = 0; i < c; i++) {
				ConfigurableJoint j = joint.joints[i];
				startRotation[i] = j.targetRotation;
				startPosition[i] = j.targetPosition;
				ConfigurableJointMotion f = ConfigurableJointMotion.Free;
				j.angularXMotion = f;
				j.xMotion = f;
				// j.yMotion = f;
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

		private void onStep()
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
		}

		private void onStop()
		{
			pos = tgt;
			onStep();
			/*
			rotatingJoint.angularXMotion = savedXMotion;
			for (int i = 0; i < joint.joints.Count; i++)
				ModuleDockRotate.lprint("restored XMotion " + joint.joints[i].angularXMotion);
			*/
		}

		public Quaternion currentRotation(int i)
		{
			Quaternion newRotation = Quaternion.Euler(new Vector3(pos, 0, 0));
			return startRotation[i] * newRotation;
		}

		public bool done() {
			return finished;
		}

		private bool done(float deltat)
		{
			if (finished)
				return true;
			if (Mathf.Abs(vel) < stopMargin * deltat * maxacc
			    && Mathf.Abs(tgt - pos) < stopMargin * deltat * deltat * maxacc / stopMargin)
				finished = true;
			return finished;
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
			guiName = "Role",
			guiActive = true, guiActiveEditor = true
		)]
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

		[KSPField(
			guiActive = false,
			guiActiveEditor = false,
			isPersistant = true
		)]
		public bool debugMode = false;

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
			float a = rotationAngle();
			float f = rotationStep * Mathf.Floor(a / rotationStep);
			if (a - f > rotationStep / 2)
				f += rotationStep;
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

		[KSPEvent(
			guiName = "Dump",
			guiActive = true,
			guiActiveEditor = false
		)]
		public void Dump()
		{
			dumpPart();
		}

		// things to be set up by setup()
		// the active module of the couple is the farthest from the root part
		// the proxy module of the couple is the closest to the root part

		private int vesselPartCount;
		private ModuleDockingNode dockingNode; // will be useful for better rotationAxis()
		private string lastNodeState = "- an impossible state -";
		private ModuleDockRotate activeRotationModule;
		private ModuleDockRotate proxyRotationModule;
		private Vector3 orgNodePos; // FIXME probably should go
		private Vector3 partNodePos; // node position relative to part
		private Vector3 partNodeAxis; // node rotation axis relative to part
		private Vector3 partNodeUp; // node vector for measuring angle relative to part

		private void reset()
		{
			vesselPartCount = -1;
			dockingNode = null;
			lastNodeState = "- another impossible state -";
			activeRotationModule = null;
			proxyRotationModule = null;
			nodeRole = "null";
			orgNodePos = new Vector3(9.9f, 9.9f, 9.9f);
			partNodePos = partNodeAxis = partNodeUp = new Vector3(9.9f, 9.9f, 9.9f);
			// orgNodeRot = Quaternion.Euler(9.9f, 9.9f, 9.9f);
			// orgNodeAxis = new Vector3(9.9f, 9.9f, 9.9f);
		}

		private void setup()
		{
			reset();

			if (part && vessel)
				vesselPartCount = vessel.parts.Count;

			dockingNode = part.FindModuleImplementing<ModuleDockingNode>();
			if (!dockingNode)
				return;

			orgNodePos = Tp(Vector3.zero, T(dockingNode), T(vessel)); // FIXME: use orgPos and orgRot
			partNodePos = Tp(Vector3.zero, T(dockingNode), T(part));
			partNodeAxis = Td(Vector3.forward, T(dockingNode), T(part));
			partNodeUp = Td(Vector3.up, T(dockingNode), T(part));

			if (isActive()) {
				activeRotationModule = this;
				proxyRotationModule = part.parent.FindModuleImplementing<ModuleDockRotate>();
				nodeRole = "Active";
			} else {
				for (int i = 0; i < part.children.Count; i++) {
					Part p = part.children[i];
					ModuleDockRotate dr = p.FindModuleImplementing<ModuleDockRotate>();
					if (dr && dr.isActive()) {
						activeRotationModule = dr;
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
				status = "proxy to " + descPart(activeRotationModule.part);
			}
			lprint("setup(" + descPart(part) + "): " + status);
		}

		private bool isActive() // must be used only in setup()
		{
			if (!part || !part.parent)
				return false;
			ModuleDockingNode parentNode = part.parent.FindModuleImplementing<ModuleDockingNode>();
			ModuleDockRotate parentRotate = part.parent.FindModuleImplementing<ModuleDockRotate>();
			return dockingNode && parentNode && parentRotate
				&& dockingNode.nodeType == parentNode.nodeType
				&& hasGoodState(dockingNode) && hasGoodState(parentNode)
				&& (parentRotate.orgNodePos - orgNodePos).magnitude >= 0.05f;
		}

		bool hasGoodState(ModuleDockingNode node)
		{
			if (!node || node.state == null)
				return false;
			return node.state.StartsWith("Docked") || node.state == "PreAttached";
		}

		private bool setupIfNeeded()
		{
			bool needed = !part || !vessel || vessel.parts.Count != vesselPartCount;
			if (needed)
				setup();
			return needed;
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

		private Vector3 rotationAxis()
		{
			// return (activeRotationModule.part.orgPos - proxyRotationModule.part.orgPos).normalized;
			return (activeRotationModule.orgNodePos - proxyRotationModule.orgNodePos).normalized;
		}

		private float rotationAngle()
		{
			if (!activeRotationModule || !proxyRotationModule)
				return float.NaN;

			Part p = activeRotationModule.part;
			Vector3 v1 = p.orgRot * Vector3.forward;
			Vector3 v2 = p.parent.orgRot * Vector3.forward;
			Vector3 a = rotationAxis();

			/*
			Quaternion activeRot = activeRotationModule.part.orgRot * activeRotationModule.dockingNode.nodeTransform.localRotation;
			Quaternion proxyRot = proxyRotationModule.part.orgRot * proxyRotationModule.dockingNode.nodeTransform.localRotation;
			Vector3 v1 = activeRot * Vector3.forward;
			Vector3 v2 = proxyRot * Vector3.forward;
			Vector3 a = activeRot * Vector3.right;
			*/

			float angle = Vector3.Angle(v1, v2);
			float axisAngle = Vector3.Angle(a, Vector3.Cross(v2, v1));
			return (axisAngle > 10) ? -angle : angle;
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

			rotationSpeed = Mathf.Abs(rotationSpeed);

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
				if (flags.IndexOf('D') >= 0 && !debugMode)
					thisGuiActive = false;

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
						if (flags.IndexOf('R') >= 0 && rotCur != null)
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
			reset();
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			if (v != vessel)
				return;
			// lprint("OnVesselGoOffRails()");
			onRails = false;
			setup();
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);
			if ((state & StartState.Editor) != 0)
				return;
			
			lprint(descPart(part) + ".OnStart(" + state + ")");

			checkGuiActive();
		}

		public override void OnUpdate()
		{
			base.OnUpdate();
			setupIfNeeded();
			checkGuiActive();
			dockingAngle = rotationAngle();
		}

		public void FixedUpdate()
		{
			if (HighLogic.LoadedScene != GameScenes.FLIGHT)
				return;

			setupIfNeeded();

			if (dockingNode && dockingNode.state != lastNodeState) {
				lastNodeState = dockingNode.state;
				lprint(descPart(part) + " state is now " + lastNodeState);
			}

			if (rotCur != null)
				advanceRotation(Time.fixedDeltaTime);
		}

		void enqueueRotation(float angle, float speed)
		{
			if (activeRotationModule != this) {
				lprint("activeRotationModule() called on wrong module, ignoring");
				return;
			}

			lprint(descPart(part) + ": enqueueRotation(" + angle + ", " + speed + ")");

			if (rotCur != null) {
				rotCur.tgt += angle;				
				lprint(descPart(part) + ": rotation updated");
			} else {
				rotCur = new RotationAnimation(0, angle, speed, part.attachJoint);
			}
		}

		private void staticizeRotation(float angle)
		{
			Vector3 axis = rotationAxis();
			lprint("axis length " + axis.magnitude);
			Quaternion rot = Quaternion.AngleAxis(angle, axis);
			lprint("staticize " + rot.eulerAngles);
			PartJoint joint = part.attachJoint;
			for (int i = 0; i < joint.joints.Count; i++) {
				joint.joints[i].secondaryAxis = rot * part.attachJoint.Joint.secondaryAxis;
				joint.joints[i].targetRotation = Quaternion.identity;
			}
			_propagate(part, rot);
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
				lprint(descPart(part) + ": rotation finished");
				staticizeRotation(rotCur.tgt);
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

		/******** Reference change utilities ********/

		private Transform T(Vessel v)
		{
			return v.rootPart.transform;
		}

		private Transform T(Part p)
		{
			return p.transform;
		}

		private Transform T(ModuleDockingNode m)
		{
			return m.nodeTransform;
		}

		private Vector3 Td(Vector3 v, Transform from, Transform to)
		{
			return to.InverseTransformDirection(from.TransformDirection(v));
		}

		private Vector3 Tp(Vector3 v, Transform from, Transform to)
		{
			return to.InverseTransformPoint(from.TransformPoint(v));
		}

		/******** Debugging stuff ********/

		static public void lprint(string msg)
		{
			print("[DockRotate]: " + msg);
		}

		private static string descPart(Part part)
		{
			if (!part)
				return "<null>";
			return part.name + "_" + part.flightID;
		}

		private static string descDrv(JointDrive drive)
		{
			return "drv(maxFrc=" + drive.maximumForce
				+ " posSpring=" + drive.positionSpring
				+ " posDamp=" + drive.positionDamper
				+ ")";
		}

		private static string descLim(SoftJointLimit limit)
		{
			return "lim(lim=" + limit.limit
				+ " bounce=" + limit.bounciness
				+ " cDist=" + limit.contactDistance
				+ ")";
		}

		static void dumpJoint(ConfigurableJoint joint)
		{
			lprint("  autoConf: " + joint.autoConfigureConnectedAnchor);
			lprint("  from: " + joint.gameObject);
			lprint("  to: " + joint.connectedBody);
			lprint("  axis: " + joint.axis);
			lprint("  secAxis: " + joint.secondaryAxis);

			lprint("  AXMot: " + joint.angularXMotion);
			lprint("  LAXLim: " + descLim(joint.lowAngularXLimit));
			lprint("  HAXLim: " + descLim(joint.highAngularXLimit));
			lprint("  AXDrv: " + descDrv(joint.angularXDrive));
			lprint("  TgtRot: " + joint.targetRotation.eulerAngles);

			lprint("  YMot: " + joint.yMotion);
			lprint("  YDrv: " + joint.yDrive);
			lprint("  ZMot: " + joint.zMotion);
			lprint("  ZDrv: " + descDrv(joint.zDrive));
			lprint("  TgtPos: " + joint.targetPosition);
			lprint("  Anchors: " + joint.anchor + " " + joint.connectedAnchor);

			// lprint("Joint YMot:   " + joint.Joint.angularYMotion);
			// lprint("Joint YLim:   " + descLim(joint.Joint.angularYLimit));
			// lprint("Joint aYZDrv: " + descDrv(joint.Joint.angularYZDrive));
			// lprint("Joint RMode:  " + joint.Joint.rotationDriveMode);
		}

		static void dumpJoint(PartJoint joint)
		{
			// lprint("Joint Parent: " + descPart(joint.Parent));
			// lprint("Joint Child:  " + descPart(joint.Child));
			// lprint("Joint Host:   " + descPart(joint.Host));
			// lprint("Joint Target: " + descPart(joint.Target));
			// lprint("Joint Axis:   " + joint.Axis);
			// lprint("Joint Joint:  " + joint.Joint);
			// lprint("secAxis: " + joint.SecAxis);
			for (int i = 0; i < joint.joints.Count; i++) {
				lprint("ConfigurableJoint[" + i + "]:");
				dumpJoint(joint.joints[i]);
			}
		}

		void dumpPart() {
			lprint("--- DUMP " + descPart(part) + " -----------------------");
			/*
			lprint("mass: " + part.mass);
			lprint("parent: " + descPart(part.parent));
			lprint("orgPos: " + part.orgPos);
			lprint("orgRot: " + part.orgRot);
			lprint("orgNodePos: " + orgNodePos);
			*/

			if (dockingNode) {
				lprint("size: " + dockingNode.nodeType);
				lprint("state: " + dockingNode.state);

				lprint("nodeAxisFw: " + Td(partNodeAxis, T(part), T(vessel)));
				lprint("nodeAxisUp: " + Td(partNodeUp, T(part), T(vessel)));
			}

			/*
			if (part.parent) {
				ModuleDockingNode parentNode = part.parent.FindModuleImplementing<ModuleDockingNode>();
				if (parentNode)
					lprint("IDs: " + part.flightID + " " + parentNode.dockedPartUId);
			}
			*/

			/*
			if (activeRotationModule) {
				lprint("rotationAxis(): " + rotationAxis());
				Vector3 posdiff = activeRotationModule.orgNodePos - proxyRotationModule.orgNodePos;
				lprint("posdiff: " + posdiff.normalized + " [" + posdiff.magnitude + "]");
			}
			*/

			// dumpJoint(part.attachJoint);
			lprint("--------------------");
		}
	}
}

