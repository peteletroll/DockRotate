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
				// ModuleDockRotate.lprint("activating rotation");
				startRotation = new Quaternion[joint.joints.Count];
				startPosition = new Vector3[joint.joints.Count];
				for (int i = 0; i < joint.joints.Count; i++) {
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
				started = true;
				ModuleDockRotate.lprint("rotation activated (" + pos + ", " + tgt + ")");
				// ModuleDockRotate.lprint("started");
			}

			// ModuleDockRotate.lprint("advancing");

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

			for (int i = 0; i < joint.joints.Count; i++) {
				ConfigurableJoint j = joint.joints[i];
				Quaternion rot = currentRotation(i);
				j.targetRotation = rot;
				// j.targetPosition = rot * j.anchor - j.anchor;
				// lprint("adv " + j.targetRotation.eulerAngles + " " + j.targetPosition);
				// joint.joints[i].anchor = rot * joint.joints[i].anchor;
				// joint.joints[i].connectedAnchor = rot * joint.joints[i].connectedAnchor;
			}
			// ModuleDockRotate.lprint("advanced");

			if (!finished && done(deltat)) {
				// ModuleDockRotate.lprint("finishing");
				pos = tgt;
				/*
				rotatingJoint.angularXMotion = savedXMotion;
				for (int i = 0; i < joint.joints.Count; i++)
					ModuleDockRotate.lprint("restored XMotion " + joint.joints[i].angularXMotion);
				*/
				ModuleDockRotate.lprint("finished");
			}
		}

		public float currentAngle()
		{
			return pos;
		}

		public Quaternion currentRotation(int i)
		{
			Quaternion newRotation = Quaternion.Euler(new Vector3(currentAngle(), 0, 0));
			return startRotation[i] * newRotation;
		}

		public bool done() {
			return finished;
		}

		public bool done(float deltat)
		{
			if (finished)
				return true;
			if (Mathf.Abs(vel) < stopMargin * deltat * maxacc
			    && Mathf.Abs(tgt - pos) < stopMargin * deltat * deltat * maxacc / stopMargin)
				finished = true;
			return finished;
		}
	}

	public class PartSet: Dictionary<string, Part>
	{
		private static string key(Part part)
		{
			return part.name + "_" + part.flightID;
		}

		public void add(Part part)
		{
			Add(key(part), part);
		}

		public bool contains(Part Part)
		{
			return ContainsKey(key(Part));
		}

		public Part[] parts()
		{
			List<Part> ret = new List<Part>();
			foreach (KeyValuePair<string, Part> i in this) {
				ret.Add(i.Value);
			}
			return ret.ToArray();
		}

		public void dump()
		{
			Part[] p = parts();
			for (int i = 0; i < p.Length; i++)
				ModuleDockRotate.lprint("rotPart " + key(p[i]));
		}
	}

	public class ModuleDockRotate: PartModule
	{
		[UI_Toggle()]
		[KSPField(guiName = "Rotation", guiActive = true, guiActiveEditor = true, isPersistant = true)]
		public bool rotationEnabled = false;

		[KSPField(
		          guiName = "Angle", guiUnits = "\u00b0", guiFormat = "0.00", // or "F2"?
		          guiActive = true, guiActiveEditor = true
	         )]
		float dockingAngle;

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

		[UI_Toggle(affectSymCounterparts = UI_Scene.None)]
		[KSPField(guiActive = true, isPersistant = true, guiName = "Reverse Rotation")]
		public bool reverseRotation = false;

		[KSPAction(guiName = "Rotate Clockwise", requireFullControl = true)]
		public void RotateClockwise(KSPActionParam param)
		{
			RotateClockwise();
		}

		[KSPEvent(
			guiName = "Rotate Clockwise",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateClockwise()
		{
			if (canStartRotation())
				activeRotationModule.enqueueRotation(rotationStep, rotationSpeed);
		}

		[KSPAction(guiName = "Rotate Counterclockwise", requireFullControl = true)]
		public void RotateCounterclockwise(KSPActionParam param)
		{
			RotateCounterclockwise();
		}

		[KSPEvent(
			guiName = "Rotate Counterclockwise",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateCounterclockwise()
		{
			if (canStartRotation())
				activeRotationModule.enqueueRotation(-rotationStep, rotationSpeed);
		}

		[KSPEvent(
			guiName = "Rotate to Snap",
			guiActive = false,
			guiActiveEditor = false
		)]
		public void RotateToSnap()
		{
			if (rotCur != null || !canStartRotation ())
				return;
			float a = rotationAngle();
			float f = rotationStep * Mathf.Floor(a / rotationStep);
			if (a - f > rotationStep / 2)
				f += rotationStep;
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

		/* things to be setup by setup() */

		private int vesselPartCount;
		private ModuleDockingNode thisDockingNode;
		private ModuleDockRotate activeRotationModule; // the active module of the couple is the farthest one from the root part

		private void setup()
		{
			vesselPartCount = 0;
			thisDockingNode = null;
			activeRotationModule = null;

			if (part)
				thisDockingNode = part.FindModuleImplementing<ModuleDockingNode>();

			if (part && part.vessel)
				vesselPartCount = part.vessel.parts.Count;

			if (canRotate()) {
				activeRotationModule = this;
			} else {
				for (int i = 0; i < part.children.Count; i++) {
					Part p = part.children[i];
					ModuleDockRotate dr = p.FindModuleImplementing<ModuleDockRotate>();
					if (dr && dr.canRotate()) {
						activeRotationModule = dr;
						break;
					}
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

		private bool canRotate() // must be used only in setup()
		{
			if (!part || !part.parent)
				return false;
			ModuleDockingNode parentNode = part.parent.FindModuleImplementing<ModuleDockingNode>();
			ModuleDockRotate parentRotate = part.parent.FindModuleImplementing<ModuleDockRotate>();
			return part.parent.name.Equals(part.name)
				&& parentRotate
				&& parentNode && parentNode.state != null;
		}

		private RotationAnimation rotCur = null;

		private bool onRails;

		private bool canStartRotation()
		{
			return !onRails
				&& rotationEnabled
				&& activeRotationModule
				&& part.vessel
				&& part.vessel.CurrentControlLevel == Vessel.ControlLevel.FULL;
		}

		private Vector3 rotationAxis()
		{
			return (part.orgPos - part.parent.orgPos).normalized;
		}

		private float rotationAngle()
		{
			ModuleDockRotate module = activeRotationModule;
			if (!module)
				return float.NaN;
			Vector3 v1 = module.part.orgRot * Vector3.forward;
			Vector3 v2 = module.part.parent.orgRot * Vector3.forward;
			Vector3 a = rotationAxis();
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
			"dockingAngle.F",
			"rotationStep.Fe",
			"rotationSpeed.Fe",
			"reverseRotation.Fe",
			"RotateClockwise.E",
			"RotateCounterclockwise.E",
			"RotateToSnap.ER",
			"ToggleAutostrutDisplay.E",
			"Dump.E"
		};

		private void checkGuiActive()
		{
			int i;

			bool newGuiActive = rotationEnabled && canStartRotation();

			for (i = 0; i < guiList.Length; i++) {
				string[] spec = guiList[i].Split(guiListSep);
				if (spec.Length != 2) {
					lprint("bad guiList entry " + guiList[i]);
					continue;
				}

				string name = spec[0];
				string flags = spec[1];

				bool editorGui = flags.IndexOf('e') >= 0;

				if (flags.IndexOf('F') >= 0) {
					BaseField fld = Fields[name];
					if (fld != null) {
						fld.guiActive = newGuiActive;
						fld.guiActiveEditor = newGuiActive && editorGui;
						UI_Control uc = fld.uiControlEditor;
						if (uc != null) {
							uc.scene = (fld.guiActive ? UI_Scene.Flight : 0)
								| (fld.guiActiveEditor ? UI_Scene.Editor : 0);
							// lprint("NEW SCENE " + uc.scene);
						}
					}
				} else if (flags.IndexOf('E') >= 0) {
					BaseEvent ev = Events[name];
					if (ev != null) {
						if (flags.IndexOf('R') >= 0 && rotCur != null)
							newGuiActive = false;
						ev.guiActive = newGuiActive;
						ev.guiActiveEditor = newGuiActive && editorGui;
						if (name == "ToggleAutostrutDisplay") {
							ev.guiName = PhysicsGlobals.AutoStrutDisplay ? "Hide Autostruts" : "Show Autostruts";
						}
					}
				} else {
					lprint("bad guiList flags " + guiList[i]);
					continue;
				}
			}
		}

		public override void OnAwake()
		{
			lprint("OnAwake()");
			GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
			GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
		}

		public override void OnActive()
		{
			lprint("OnActive()");
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
			lprint("OnVesselGoOnRails()");
			onRails = true;
		}

		public void OnVesselGoOffRails (Vessel v)
		{
			if (v != vessel)
				return;
			lprint("OnVesselGoOffRails()");
			onRails = false;
			setup();
		}

		public override void OnStart(StartState state)
		{
			if ((state & StartState.Editor) != 0)
				return;
			
			lprint(descPart(part) + ".OnStart(" + state + ")");

			checkGuiActive();
		}

		public override void OnUpdate()
		{
			checkGuiActive();
			dockingAngle = rotationAngle();
		}

		public void FixedUpdate()
		{
			if (HighLogic.LoadedScene != GameScenes.FLIGHT)
				return;
			advanceRotation(Time.fixedDeltaTime);
		}

		void enqueueRotation(float angle, float speed)
		{
			if (reverseRotation)
				angle = -angle;
			lprint(descPart(part) + ": enqueueRotation(" + angle + ", " + speed + ")");

			// disableAutoStruts();

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
			// lprint("propagating to " + descPart(p));
			Vector3 dp = p.orgPos - part.orgPos;
			Vector3 rdp = rot * dp;
			Vector3 newPos = rdp + part.orgPos;
			// lprint("oldpos " + p.orgPos);
			p.orgPos = newPos;
			// lprint("newpos " + p.orgPos);

			p.orgRot = rot * p.orgRot;

			for (int i = 0; i < p.children.Count; i++)
				_propagate(p.children[i], rot);
		}

		PartSet rotatingPartSet()
		{
			PartSet ret = new PartSet();
			ModuleDockRotate m = activeRotationModule;
			if (m)
				_collect(ret, m.part);
			return ret;
		}

		private void _collect(PartSet s, Part p)
		{
			s.add(p);
			for (int i = 0; i < p.children.Count; i++)
				_collect(s, p.children[i]);
		}

		private void advanceRotation(float deltat)
		{
			if (rotCur == null)
				return;
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

		private void disableAutoStruts() {
			if (!part.vessel)
				return;
			PartSet rp = rotatingPartSet();
			Part[] vp = vessel.parts.ToArray();
			PartJoint[] joints = FindObjectsOfType<PartJoint>();
			lprint("checking joints: " + joints.Length);

			for (int i = 0; i < joints.Length; i++) {
				lprint("j[" + i + "]");
				PartJoint j = joints[i];
				lprint("Vessel: " + j.Host.vessel);
				lprint("  Host:" + descPart(j.Host) + " Target: " + descPart(j.Target));
				lprint("  Parent:" + descPart(j.Parent) + " Child: " + descPart(j.Child));
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
			lprint("mass: " + part.mass);
			lprint("parent: " + descPart(part.parent));

			if (thisDockingNode) {
				lprint("size: " + thisDockingNode.nodeType); 
				ModuleDockingNode otherNode = thisDockingNode.FindOtherNode ();
				if (otherNode)
					lprint ("other: " + descPart(otherNode.part));
			}

			ModuleDockingNode parentNode = part.parent.FindModuleImplementing<ModuleDockingNode>();
			if (parentNode)
				lprint("IDs: " + part.flightID + " " + parentNode.dockedPartUId);

			lprint("orgPos: " + part.orgPos);
			lprint("orgRot: " + part.orgRot);
			lprint("rotationAxis(): " + rotationAxis());
			dumpJoint(part.attachJoint);
			lprint("--------------------");
		}
	}
}

