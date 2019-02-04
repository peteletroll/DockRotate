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
			advancedTweakable = true,
			guiName = "#DCKROT_reverse_rotation"
		)]
		public bool reverseRotation = false;

		[UI_Toggle()]
		[KSPField(
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			advancedTweakable = true,
			guiName = "#DCKROT_flip_flop_mode"
		)]
		public bool flipFlopMode = false;

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
				if (tgt.flipFlopMode)
					tgt.reverseRotation = !tgt.reverseRotation;
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
			if (canStartRotation()) {
				doRotateClockwise();
				if (flipFlopMode)
					reverseRotation = !reverseRotation;
			}
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
				if (tgt.flipFlopMode)
					tgt.reverseRotation = !tgt.reverseRotation;
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
			if (canStartRotation()) {
				doRotateCounterclockwise();
				if (flipFlopMode)
					reverseRotation = !reverseRotation;
			}
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

		public abstract void doRotateClockwise();

		public abstract void doRotateCounterclockwise();

		public abstract void doRotateToSnap();

		public abstract bool useSmartAutoStruts();

		protected abstract void dumpPart();

		public void doStopRotation()
		{
			JointMotionObj r = currentRotation();
			if (r)
				r.brake();
		}

		protected bool reverseActionRotationKey()
		{
			return GameSettings.MODIFIER_KEY.GetKey();
		}

		public bool IsJointUnlocked()
		{
			bool ret = currentRotation();
			// log(part.desc() + ".IsJointUnlocked() is " + ret);
			return ret;
		}

		protected JointMotionObj rotCur {
			get { return jointMotion && jointMotion.rotCur && jointMotion.rotCur.owner == this ? jointMotion.rotCur : null; }
		}

		protected JointMotion jointMotion;

		public Part activePart;
		public string nodeRole = "Init";

		protected Vector3 partNodePos; // node position, relative to part
		protected Vector3 partNodeAxis; // node rotation axis, relative to part
		protected bool geometryOk;
		protected abstract bool setupGeometry(StartState state);

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
			if (!part || !vessel || !geometryOk) {
				log("WARNING: doSetup() called at a bad time");
				return;
			}

			try {
				setupGuiActive();
				setup();
			} catch (Exception e) {
				string sep = new string('-', 80);
				log(sep);
				log("Exception during setup:\n" + e.StackTrace);
				log(sep);
			}

			setupDone = true;
		}

		public void OnVesselGoOnRails()
		{
			if (verboseEvents)
				log(part.desc() + ": OnVesselGoOnRails()");
			freezeCurrentRotation("go on rails", false);
			setupDone = false;
		}

		public void OnVesselGoOffRails()
		{
			if (verboseEvents)
				log(part.desc() + ": OnVesselGoOffRails()");
			setupDone = false;
			// start speed always 0 when going off rails
			frozenRotation[2] = 0f;
			doSetup();
		}

		public void RightBeforeStructureChange()
		{
			if (verboseEvents)
				log(part.desc() + ": RightBeforeStructureChange()");
			freezeCurrentRotation("structure change", true);
		}

		public void RightAfterStructureChange()
		{
			if (verboseEvents)
				log(part.desc() + ": RightAfterStructureChange()");
			doSetup();
		}

		public override void OnAwake()
		{
			setupDone = false;

			base.OnAwake();
		}

		public virtual void OnDestroy()
		{
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

			// log(part.desc() + ": " + fld.Length + " fields, " + evt.Length + " events");
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
				log(part.desc() + ": OnStart() with no vessel, state " + state);
			}

			geometryOk = setupGeometry(state);

			setupGuiActive();

			checkGuiActive();
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (MapView.MapIsEnabled)
				return;

			bool guiActive = canStartRotation();
			JointMotionObj cr = currentRotation();

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

		protected bool canStartRotation()
		{
			return rotationEnabled
				&& setupDone && jointMotion
				&& vessel && vessel.CurrentControlLevel == Vessel.ControlLevel.FULL;
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

		protected float rotationAngle(bool dynamic)
		{
			return jointMotion ? jointMotion.rotationAngle(dynamic) : float.NaN;
		}

		protected float dynamicDeltaAngle()
		{
			return jointMotion ? jointMotion.dynamicDeltaAngle() : float.NaN;
		}

		protected int countJoints()
		{
			return jointMotion ? jointMotion.joint.joints.Count : 0;
		}

		protected bool enqueueRotation(Vector3 frozen)
		{
			return enqueueRotation(frozen[0], frozen[1], frozen[2]);
		}

		protected virtual bool enqueueRotation(float angle, float speed, float startSpeed = 0f)
		{
			return jointMotion && jointMotion.enqueueRotation(this, angle, speed, startSpeed);
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

		protected void freezeCurrentRotation(string msg, bool keepSpeed)
		{
			if (rotCur) {
				log(part.desc() + ": freezeCurrentRotation("
					+ msg + ", " + keepSpeed + ")");
				rotCur.isContinuous();
				float angle = rotCur.tgt - rotCur.pos;
				enqueueFrozenRotation(angle, rotCur.maxvel, keepSpeed ? rotCur.vel : 0f);
				rotCur.abort();
				log(part.desc() + ": removing rotation (2)");
				jointMotion.rotCur = null;
			}
		}

		protected abstract JointMotionObj currentRotation();

		protected void checkFrozenRotation()
		{
			if (!setupDone)
				return;

			if (!Mathf.Approximately(frozenRotation[0], 0f) && !currentRotation()) {
				enqueueRotation(frozenRotation);
				JointMotionObj cr = currentRotation();
				if (cr)
					cr.controller = controller(frozenRotationControllerID);
			}

			updateFrozenRotation("CHECK");
		}

		public void updateFrozenRotation(string context)
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
				log(part.desc() + ": updateFrozenRotation("
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
			log(part.desc() + ": enqueueFrozenRotation(): "
				+ prev.desc() + " -> " + frozenRotation.desc());
		}

		public void FixedUpdate()
		{
			if (!setupDone || HighLogic.LoadedScene != GameScenes.FLIGHT)
				return;
			checkFrozenRotation();
		}

		/******** Debugging stuff ********/

		public static bool log(string msg)
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

		protected override bool setupGeometry(StartState state)
		{
			rotatingNode = part.FindAttachNode(rotatingNodeName);

			if (rotatingNode == null) {
				log(GetType() + ".setupGeometry(" + state + "): "
					+ "no node \"" + rotatingNodeName + " in " + part.desc());
				AttachNode[] nodes = part.FindAttachNodes("");
				string nodeHelp = part.desc() + " available nodes:";
				for (int i = 0; i < nodes.Length; i++)
					nodeHelp += " \"" + nodes[i].id + "\"";
				log(nodeHelp);
				return false;
			}

			partNodePos = rotatingNode.position;
			partNodeAxis = rotatingNode.orientation;
			log(GetType() + ".setupGeometry(" + state + ") done: "
				+ partNodeAxis + "@" + partNodePos);
			return true;
		}

		protected override void setup()
		{
			PartJoint rotatingJoint = null;

			jointMotion = null;
			activePart = null;

			nodeRole = "None";

			if (part.FindModuleImplementing<ModuleDockRotate>()) {
				log(part.desc() + ": has DockRotate, NodeRotate disabled");
				return;
			}

			if (!part.hasPhysics()) {
				log(part.desc() + ": physicsless, NodeRotate disabled");
				return;
			}

			if (rotatingNode == null) {
				log(GetType() + ".setup(): no rotatingNode");
				return;
			}

			Part other = rotatingNode.attachedPart;
			if (!other)
				return;

			other.forcePhysics();
			if (!other.rb) {
				log(part.desc() + ": other part " + other.desc() + " has no Rigidbody");
				// return;
			}

			if (part.parent == other) {
				nodeRole = "Active";
				activePart = part;
			} else if (other.parent == part) {
				nodeRole = "Proxy";
				activePart = other;
				// FIXME: this should be computed in JointMotion with joint Host/Target info
				partNodePos = partNodePos.STp(part, activePart);
				partNodeAxis = -partNodeAxis.STd(part, activePart);
			}

			if (activePart)
				rotatingJoint = activePart.attachJoint;

			if (verboseEvents && rotatingJoint) {
				log(part.desc()
					+ ": on "
					+ (activePart == part ? "itself" : activePart.desc()));
			}

			if (rotatingJoint) {
				jointMotion = JointMotion.get(rotatingJoint);
				jointMotion.setAxis(activePart, partNodeAxis, partNodePos);
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

		protected override JointMotionObj currentRotation()
		{
			return rotCur;
		}

		protected override void dumpPart()
		{
			log("--- DUMP " + part.desc() + " ---");
			log("rotPart: " + activePart.desc());
			log("rotAxis: " + partNodeAxis.ddesc(activePart));
			log("rotPos: " + partNodePos.pdesc(activePart));
			AttachNode[] nodes = part.FindAttachNodes("");
			for (int i = 0; i < nodes.Length; i++) {
				AttachNode n = nodes[i];
				if (rotatingNode != null && rotatingNode.id != n.id)
					continue;
				log("  node [" + i + "/" + nodes.Length + "] \"" + n.id + "\""
					+ ", size " + n.size
					+ ", type " + n.nodeType
					+ ", method " + n.attachMethod);
				// log("    dirV: " + n.orientation.STd(part, vessel.rootPart).desc());
				_dumpv("dir", n.orientation, n.originalOrientation);
				_dumpv("sec", n.secondaryAxis, n.originalSecondaryAxis);
				_dumpv("pos", n.position, n.originalPosition);
			}
			if (jointMotion) {
				log(jointMotion.joint == part.attachJoint ? "parent joint:" : "not parent joint:");
				jointMotion.joint.dump();
			}

			log("--------------------");
		}

		private void _dumpv(string label, Vector3 v, Vector3 orgv)
		{
			log("    "
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

		protected override bool setupGeometry(StartState state)
		{
			dockingNode = part.FindModuleImplementing<ModuleDockingNode>();

			if (!dockingNode) {
				log(GetType() + ".setupGeometry(" + state + "): no docking node in " + part.desc());
				return false;
			}

			partNodePos = Vector3.zero.Tp(dockingNode.T(), part.T());
			partNodeAxis = Vector3.forward.Td(dockingNode.T(), part.T());
			log(GetType() + ".setupGeometry(" + state + ") done: "
				+ partNodeAxis + "@" + partNodePos);
			return true;
		}

		private void basicSetup()
		{
			int now = Time.frameCount;
			if (lastBasicSetupFrame == now) {
				if (false && verboseEvents)
					log(part.desc() + ": skip repeated basicSetup()");
				return;
			}
			lastBasicSetupFrame = now;

			activePart = null;
			jointMotion = null;
			activeRotationModule = proxyRotationModule = otherRotationModule = null;
			nodeRole = "None";
#if DEBUG
			nodeStatus = "";
#endif
		}

		protected override void setup()
		{
			PartJoint rotatingJoint = null;

			basicSetup();

			if (!dockingNode) {
				log(GetType() + ".setup(): no dockingNode");
				return;
			}

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
			}

			if (activeRotationModule == this) {
				proxyRotationModule.nodeRole = svj ? "ProxySame" : "Proxy";
				proxyRotationModule.activeRotationModule = activeRotationModule;
				proxyRotationModule.activePart = activePart;
				proxyRotationModule.proxyRotationModule = proxyRotationModule;
				otherRotationModule = proxyRotationModule;
				proxyRotationModule.otherRotationModule = activeRotationModule;
				if (verboseEvents)
					log(activeRotationModule.part.desc()
						+ ": on " + proxyRotationModule.part.desc());
			}

			if (dockingNode.snapRotation && dockingNode.snapOffset > 0f
				&& activeRotationModule == this
				&& (rotationEnabled || proxyRotationModule.rotationEnabled)) {
				enqueueFrozenRotation(angleToSnap(dockingNode.snapOffset), rotationSpeed);
			}

			if (rotatingJoint) {
				jointMotion = JointMotion.get(rotatingJoint);
				jointMotion.setAxis(activePart, partNodeAxis, partNodePos);
				if (proxyRotationModule)
					proxyRotationModule.jointMotion = jointMotion;
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

		private bool isDockedToParent(bool verbose) // must be used only after setupGeometry()
		{
			if (verbose)
				log(part.desc() + ": isDockedToParent()");

			if (!part || !part.parent) {
				if (verbose)
					log(part.desc() + ": isDockedToParent() finds no parent");
				return false;
			}

			ModuleDockingNode parentNode = part.parent.FindModuleImplementing<ModuleDockingNode>();
			ModuleDockRotate parentRotate = part.parent.FindModuleImplementing<ModuleDockRotate>();
			if (parentRotate)
				parentRotate.basicSetup();

			if (!dockingNode || !parentNode || !parentRotate) {
				if (verbose)
					log(part.desc() + ": isDockedToParent() has missing modules");
				return false;
			}

			float nodeDist = (partNodePos - parentRotate.partNodePos.Tp(parentRotate.part.T(), part.T())).magnitude;
			float nodeAngle = Vector3.Angle(partNodeAxis, Vector3.back.Td(parentNode.T(), part.parent.T()).STd(part.parent, part));
			if (verbose)
				log(part.desc() + ": isDockedToParent(): dist " + nodeDist + ", angle " + nodeAngle
					+ ", types " + dockingNode.allTypes() + "/" + parentNode.allTypes());

			bool ret = dockingNode.nodeTypes.Overlaps(parentNode.nodeTypes)
				&& nodeDist < 1f && nodeAngle < 5f;

			if (verbose)
				log(part.desc() + ": isDockedToParent() returns " + ret);

			return ret;
		}

		public override bool useSmartAutoStruts()
		{
			return (activeRotationModule && activeRotationModule.smartAutoStruts)
				|| (proxyRotationModule && proxyRotationModule.smartAutoStruts);
		}

		protected override bool enqueueRotation(float angle, float speed, float startSpeed = 0f)
		{
			bool ret = false;
			if (activeRotationModule == this) {
				ret = base.enqueueRotation(angle, speed, startSpeed);
			} else if (activeRotationModule && activeRotationModule.activeRotationModule == activeRotationModule) {
				ret = activeRotationModule.enqueueRotation(angle, speed, startSpeed);
			} else {
				log(part.desc() + ".enqueueRotation() called on wrong module, ignoring, active part "
					+ (activeRotationModule ? activeRotationModule.part.desc() : "null"));
			}
			if (ret) {
				JointMotionObj cr = currentRotation();
				if (cr)
					cr.controller = this;
			}
			return ret;
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

		protected override JointMotionObj currentRotation()
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
				log(part.desc() + ": forwards to " + ret.part.desc());
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
			log("--- DUMP " + part.desc() + " ---");
			log("rotPart: " + activePart.desc());
			log("role: " + nodeRole);
#if DEBUG
			log("status: " + nodeStatus);
#endif
			log("org: " + part.descOrg());

			if (dockingNode) {
				log("state: " + dockingNode.state);

				log("types: " + dockingNode.allTypes());

				ModuleDockingNode other = dockingNode.otherNode();
				log("other: " + (other ? other.part.desc() : "none"));

				log("partNodeAxisV: " + partNodeAxis.STd(part, vessel.rootPart).desc());
				log("GetFwdVector(): " + dockingNode.GetFwdVector().desc());
				log("nodeTransform: " + dockingNode.nodeTransform.desc(8));
			}

			if (jointMotion) {
				log(jointMotion.joint == part.attachJoint ? "parent joint:" : "same vessel joint:");
				jointMotion.joint.dump();
			}

			log("--------------------");
		}
	}
}

