using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;

namespace DockRotate
{
	public abstract class ModuleBaseRotate: PartModule, IJointLockState, IStructureChangeListener
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
			JointMotion r = currentRotation();
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

		private JointMotion _rotCur = null;
		protected JointMotion rotCur {
			get { return _rotCur; }
			set {
				bool wasRotating = _rotCur;
				_rotCur = value;
				bool isRotating = _rotCur;
				if (isRotating != wasRotating) {
					// rotation count change
					if (isRotating) {
						// a new rotation is starting
						VesselMotionManager.get(activePart.vessel).changeCount(+1);
					} else {
						// an old rotation is finishing
						VesselMotionManager.get(activePart.vessel).changeCount(-1);
					}
					if (useSmartAutoStruts()) {

					} else {
						lprint(part.desc() + " triggered CycleAllAutoStruts()");
						vessel.CycleAllAutoStrut();
					}
				}
			}
		}

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
			if (!part || !vessel) {
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

		public void OnVesselGoOnRails()
		{
			if (verboseEvents)
				lprint(part.desc() + ": OnVesselGoOnRails()");
			freezeCurrentRotation("go on rails", false);
			setupDone = false;
		}

		public void OnVesselGoOffRails()
		{
			if (verboseEvents)
				lprint(part.desc() + ": OnVesselGoOffRails()");
			setupDone = false;
			// start speed always 0 when going off rails
			frozenRotation[2] = 0f;
			doSetup();
		}

		private void RightBeforeStructureChangeJointUpdate(Vessel v)
		{
			if (v != vessel)
				return;
			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeStructureChangeJointUpdate()");
			RightBeforeStructureChange();
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

		int lastRightBeforeStructureChange = 0;

		public void RightBeforeStructureChange()
		{
			int now = Time.frameCount;
			if (lastRightBeforeStructureChange == now) {
				if (verboseEvents)
					lprint(part.desc() + ": skipping repeated RightBeforeStructureChange()");
				return;
			}
			lastRightBeforeStructureChange = now;

			if (verboseEvents)
				lprint(part.desc() + ": RightBeforeStructureChange()");
			freezeCurrentRotation("structure change", true);
		}

		public void RightAfterStructureChange()
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
			setupDone = false;

			base.OnAwake();
			setEvents(true);
		}

		public virtual void OnDestroy()
		{
			setEvents(false);
		}

		private void setEvents(bool cmd)
		{
			if (cmd) {

				GameEvents.onActiveJointNeedUpdate.Add(RightBeforeStructureChangeJointUpdate);

				GameEvents.onPartCouple.Add(RightBeforeStructureChangeAction);
				GameEvents.onPartDeCouple.Add(RightBeforeStructureChangePart);

				GameEvents.onVesselDocking.Add(RightBeforeStructureChangeIds);
				GameEvents.onPartUndock.Add(RightBeforeStructureChangePart);

				GameEvents.onSameVesselDock.Add(RightAfterSameVesselDock);
				GameEvents.onSameVesselUndock.Add(RightAfterSameVesselUndock);

			} else {

				GameEvents.onActiveJointNeedUpdate.Remove(RightBeforeStructureChangeJointUpdate);

				GameEvents.onPartCouple.Remove(RightBeforeStructureChangeAction);
				GameEvents.onPartDeCouple.Remove(RightBeforeStructureChangePart);

				GameEvents.onVesselDocking.Remove(RightBeforeStructureChangeIds);
				GameEvents.onPartUndock.Remove(RightBeforeStructureChangePart);

				GameEvents.onSameVesselDock.Remove(RightAfterSameVesselDock);
				GameEvents.onSameVesselUndock.Remove(RightAfterSameVesselUndock);

			}
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

			if (vessel) {
				VesselMotionManager.get(vessel); // force creation of VesselMotionManager
			} else if (state != StartState.Editor) {
				lprint(part.desc() + ": OnStart() with no vessel, state " + state);
			}

			setupGuiActive();

			checkGuiActive();
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (MapView.MapIsEnabled)
				return;

			bool guiActive = canStartRotation();
			JointMotion cr = currentRotation();

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
				lprint(part.desc() + ": creating rotation");
				rotCur = new JointMotion(activePart, partNodePos, partNodeAxis, rotatingJoint, 0, angle, speed);
				rotCur.rot0 = rotationAngle(false);
				rotCur.controller = this;
				rotCur.electricityRate = electricityRate;
				rotCur.soundVolume = soundVolume;
				rotCur.vel = startSpeed;
				rotCur.smartAutoStruts = useSmartAutoStruts();
				action = "added";
			}
			if (showlog)
				lprint(String.Format("{0}: enqueueRotation({1}, {2:F4}\u00b0, {3}\u00b0/s, {4}\u00b0/s), {5}",
					activePart.desc(), partNodeAxis.desc(), rotCur.tgt, rotCur.maxvel, rotCur.vel, action));
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
				lprint(part.desc() + ": removing rotation (1)");
				rotCur = null;
				return;
			}

			rotCur.advance(deltat);
		}

		protected void freezeCurrentRotation(string msg, bool keepSpeed)
		{
			if (rotCur) {
				lprint(part.desc() + ": freezeCurrentRotation("
					+ msg + ", " + keepSpeed + ")");
				rotCur.isContinuous();
				float angle = rotCur.tgt - rotCur.pos;
				enqueueFrozenRotation(angle, rotCur.maxvel, keepSpeed ? rotCur.vel : 0f);
				rotCur.abort();
				lprint(part.desc() + ": removing rotation (2)");
				rotCur = null;
			}
		}

		protected abstract JointMotion currentRotation();

		protected void checkFrozenRotation()
		{
			if (!setupDone)
				return;

			if (!Mathf.Approximately(frozenRotation[0], 0f) && !currentRotation()) {
				enqueueRotation(frozenRotation);
				JointMotion cr = currentRotation();
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
			lprint(part.desc() + ": enqueueFrozenRotation(): "
				+ prev.desc() + " -> " + frozenRotation.desc());
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

		protected override JointMotion currentRotation()
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
				JointMotion cr = currentRotation();
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
				lprint(part.desc() + ": removing rotation (3)");
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

		protected override JointMotion currentRotation()
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
			lprint("org: " + part.descOrg());

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
}

