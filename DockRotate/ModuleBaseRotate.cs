using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using KSP.Localization;
using CompoundParts;

namespace DockRotate
{
	public abstract class ModuleBaseRotate: PartModule,
		IJointLockState, IResourceConsumer
	{
		protected const string GROUPNAME = "DockRotate";
		protected const string GROUPLABEL = "#DCKROT_rotation";
		protected const string DEBUGGROUP = "DockRotateDebug";
#if DEBUG
		protected const bool DEBUGMODE = true;
#else
		protected const bool DEBUGMODE = false;
#endif

		[KSPField(isPersistant = true)]
		public int Revision = -1;

		private static int _revision = -1;
		public int getRevision()
		{
			if (_revision < 0) {
				_revision = 0;
				try {
					_revision = Assembly.GetExecutingAssembly().GetName().Version.Revision;
				} catch (Exception e) {
					string sep = new string('-', 80);
					log(sep);
					log("Exception reading revision:\n" + e.StackTrace);
					log(sep);
				}
			}
			return _revision;
		}

		private void checkRevision()
		{
			int r = getRevision();
			if (Revision != r) {
				log(desc(), ": REVISION " + Revision + " -> " + r);
				Revision = r;
			}
		}

		[KSPField(
			guiName = "#DCKROT_angle",
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiActive = true,
			guiActiveEditor = true
		)]
		public string angleInfo;
		private static string angleInfoNA = Localizer.Format("#DCKROT_n_a");

		[UI_Toggle]
		[KSPField(
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiName = "#DCKROT_rotation",
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true
		)]
		public bool rotationEnabled = false;

		[UI_FloatEdit(
			incrementSlide = 0.5f, incrementSmall = 5f, incrementLarge = 30f,
			sigFigs = 1, unit = "\u00b0",
			minValue = 0f, maxValue = 360f
		)]
		[KSPField(
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			guiName = "#DCKROT_rotation_step",
			guiUnits = "\u00b0"
		)]
		public float rotationStep = 15f;

		[UI_FloatEdit(
			incrementSlide = 1f, incrementSmall = 15f, incrementLarge = 180f,
			sigFigs = 0, unit = "\u00b0/s",
			minValue = 1, maxValue = 8f * 360f
		)]
		[KSPField(
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			guiName = "#DCKROT_rotation_speed",
			guiUnits = "\u00b0/s"
		)]
		public float rotationSpeed = 5f;

		[UI_Toggle(affectSymCounterparts = UI_Scene.None)]
		[KSPField(
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			advancedTweakable = true,
			guiName = "#DCKROT_reverse_rotation"
		)]
		public bool reverseRotation = false;

		[UI_Toggle]
		[KSPField(
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			advancedTweakable = true,
			guiName = "#DCKROT_flip_flop_mode"
		)]
		public bool flipFlopMode = false;

		[KSPField(isPersistant = true)]
		public string soundClip = "DockRotate/DockRotateMotor";

		[KSPField(isPersistant = true)]
		public float soundVolume = 0.5f;

		[KSPField(isPersistant = true)]
		public float soundPitch = 1f;

		[UI_Toggle]
		[KSPField(
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			guiActive = DEBUGMODE,
			guiActiveEditor = DEBUGMODE,
			isPersistant = true,
			advancedTweakable = false,
			guiName = "#DCKROT_smart_autostruts"
		)]
		public bool smartAutoStruts = true;

		[KSPField(
			guiActive = DEBUGMODE,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public float anglePosition;

		private bool needsAlignment;

		[KSPField(
			guiActive = DEBUGMODE,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public float angleVelocity;

		[KSPField(
			guiActive = DEBUGMODE,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public bool angleIsMoving;

#if DEBUG
		[KSPField(
			guiName = "#DCKROT_status",
			guiActive = true,
			guiActiveEditor = false,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public string nodeStatus = "";
#endif

#if DEBUG
		[UI_Toggle]
		[KSPField(
			guiName = "Verbose Setup",
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
#endif
		public bool verboseSetup = false;

#if DEBUG
		[UI_Toggle]
		[KSPField(
			guiName = "Verbose Events",
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
#endif
		public bool verboseEvents = false;

#if DEBUG
		[KSPAxisField(
			guiName = "AxisField",
			guiActive = true,
			guiActiveEditor = true,
			isPersistant = true,
			guiFormat = "F3",
			axisMode = KSPAxisMode.Absolute,
			minValue = -1f,
			maxValue = 1f,
			incrementalSpeed = 0.1f,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public float axisField = 0f;
#endif

		[KSPAction(
			guiName = "#DCKROT_enable_rotation",
			requireFullControl = true
		)]
		public void EnableRotation(KSPActionParam param)
		{
			if (verboseEvents)
				log(desc(), ": action " + param.desc());
			rotationEnabled = true;
		}

		[KSPAction(
			guiName = "#DCKROT_disable_rotation",
			requireFullControl = true
		)]
		public void DisableRotation(KSPActionParam param)
		{
			if (verboseEvents)
				log(desc(), ": action " + param.desc());
			rotationEnabled = false;
		}

		[KSPAction(
			guiName = "#DCKROT_toggle_rotation",
			requireFullControl = true
		)]
		public void ToggleRotation(KSPActionParam param)
		{
			if (verboseEvents)
				log(desc(), ": action " + param.desc());
			rotationEnabled = !rotationEnabled;
		}

		[KSPAction(
			guiName = "#DCKROT_rotate_clockwise",
			requireFullControl = true
		)]
		public void RotateClockwise(KSPActionParam param)
		{
			if (verboseEvents)
				log(desc(), ": action " + param.desc());
			if (reverseActionRotationKey()) {
				doRotateCounterclockwise();
			} else {
				doRotateClockwise();
			}
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_clockwise",
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiActive = false,
			guiActiveEditor = false,
			requireFullControl = true
		)]
		public void RotateClockwise()
		{
			doRotateClockwise();
		}

		[KSPAction(
			guiName = "#DCKROT_rotate_counterclockwise",
			requireFullControl = true
		)]
		public void RotateCounterclockwise(KSPActionParam param)
		{
			if (verboseEvents)
				log(desc(), ": action " + param.desc());
			if (reverseActionRotationKey()) {
				doRotateClockwise();
			} else {
				doRotateCounterclockwise();
			}
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_counterclockwise",
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiActive = false,
			guiActiveEditor = false,
			requireFullControl = true
		)]
		public void RotateCounterclockwise()
		{
			doRotateCounterclockwise();
		}

		[KSPAction(
			guiName = "#DCKROT_rotate_to_snap",
			requireFullControl = true
		)]
		public void RotateToSnap(KSPActionParam param)
		{
			if (verboseEvents)
				log(desc(), ": action " + param.desc());
			doRotateToSnap();
		}

		[KSPEvent(
			guiName = "#DCKROT_rotate_to_snap",
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiActive = false,
			guiActiveEditor = false,
			requireFullControl = true
		)]
		public void RotateToSnap()
		{
			doRotateToSnap();
		}

		[KSPAction(
			guiName = "#DCKROT_stop_rotation",
			requireFullControl = true
		)]
		public void StopRotation(KSPActionParam param)
		{
			if (verboseEvents)
				log(desc(), ": action " + param.desc());
			doStopRotation();
		}

		private BaseEvent StopRotationEvent;
		[KSPEvent(
			guiName = "#DCKROT_stop_rotation",
			groupName = GROUPNAME,
			groupDisplayName = GROUPLABEL,
			groupStartCollapsed = true,
			guiActive = false,
			guiActiveEditor = false,
			requireFullControl = true
		)]
		public void StopRotation()
		{
			doStopRotation();
		}

#if DEBUG
		[UI_Toggle]
#endif
		[KSPField(
			guiName = "autoSnap",
			isPersistant = true,
			guiActive = DEBUGMODE,
			guiActiveEditor = DEBUGMODE,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public bool autoSnap = false;

#if DEBUG
		[UI_Toggle]
#endif
		[KSPField(
			guiName = "hideCommands",
			isPersistant = true,
			guiActive = DEBUGMODE,
			guiActiveEditor = DEBUGMODE,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public bool hideCommands = false;

#if DEBUG
		BaseEvent ToggleAutoStrutDisplayEvent;
		[KSPEvent(
			guiName = "Toggle Autostrut Display",
			guiActive = true,
			guiActiveEditor = true,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void ToggleAutoStrutDisplay()
		{
			PhysicsGlobals.AutoStrutDisplay = !PhysicsGlobals.AutoStrutDisplay;
			if (HighLogic.LoadedSceneIsEditor)
				GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartTweaked, part);
		}

		[KSPEvent(
			guiActive = true,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void DumpToLog()
		{
			string d = desc(true);
			log(d, ": BEGIN DUMP");

			List<AttachNode> nodes = part.allAttachNodes();
			string nodeHelp = ": available nodes:";
			for (int i = 0; i < nodes.Count; i++)
				if (nodes[i] != null)
					nodeHelp += " \"" + nodes[i].id + "\"";
			log(d, nodeHelp);

			dumpExtra();

			if (hasJointMotion && jointMotion.joint)
				jointMotion.joint.dump();
			else
				log(d, ": no jointMotion");

			List<PartJoint> autoStruts = part.autoStruts();
			if (autoStruts != null) {
				for (int i = 0; i < autoStruts.Count; i++)
					log(d, ": autostrut " + autoStruts[i].desc());
			}

			log(d, ": END DUMP");
		}

		public virtual void dumpExtra()
		{
		}

		[KSPEvent(
			guiName = "Cycle Autostruts",
			guiActive = true,
			guiActiveEditor = true,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void CycleAutoStruts()
		{
			if (vessel)
				vessel.CycleAllAutoStrut();
		}

		private BaseEvent ToggleTraceEventsEvent;
		[KSPEvent(
			guiName = "Toggle Trace Events",
			guiActive = true,
			guiActiveEditor = true,
			groupName = DEBUGGROUP,
			groupDisplayName = DEBUGGROUP,
			groupStartCollapsed = true
		)]
		public void ToggleTraceEvents()
		{
			GameEvents.debugEvents = !GameEvents.debugEvents;
		}
#endif

		public void doRotateClockwise()
		{
			if (!canStartRotation(true))
				return;
			if (!enqueueRotation(step(), speed()))
				return;
			if (flipFlopMode)
				reverseRotation = !reverseRotation;
		}

		public void doRotateCounterclockwise()
		{
			if (!canStartRotation(true))
				return;
			if (!enqueueRotation(-step(), speed()))
				return;
			if (flipFlopMode)
				reverseRotation = !reverseRotation;
		}

		public void doRotateToSnap()
		{
			if (!canStartRotation(true, true))
				return;
			enqueueRotationToSnap(rotationStep, speed());
		}

		public void doStopRotation()
		{
			JointMotionObj cr = currentRotation();
			if (cr)
				cr.brake();
		}

		protected bool reverseActionRotationKey()
		{
			return GameSettings.MODIFIER_KEY.GetKey();
		}

		public bool IsJointUnlocked()
		{
			bool ret = currentRotation();
			if (verboseEvents || ret)
				log(desc(), ".IsJointUnlocked() is " + ret);
			return ret;
		}

		private static List<PartResourceDefinition> cached_GetConsumedResources = null;

		public List<PartResourceDefinition> GetConsumedResources()
		{
			// log(desc(), ".GetConsumedResource() called");
			if (cached_GetConsumedResources == null) {
				cached_GetConsumedResources = new List<PartResourceDefinition>();
				PartResourceDefinition ec = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
				if (ec != null)
					cached_GetConsumedResources.Add(ec);
			}
			return cached_GetConsumedResources;
		}

		protected bool setupLocalAxisDone;
		protected abstract bool setupLocalAxis(StartState state);
		protected abstract AttachNode findMovingNodeInEditor(out Part otherPart, bool verbose);

		protected JointMotion jointMotion;
		protected bool hasJointMotion;
		protected abstract PartJoint findMovingJoint(bool verbose);

		public string nodeRole = "Init";

		protected Vector3 partNodePos; // node position, relative to part
		protected Vector3 partNodeAxis; // node rotation axis, relative to part

		// localized info cache
		protected string storedModuleDisplayName = "";
		protected string storedInfo = "";
		protected abstract void fillInfo();

		public override string GetModuleDisplayName()
		{
			if (storedModuleDisplayName == "")
				fillInfo();
			return storedModuleDisplayName;
		}

		public override string GetInfo()
		{
			if (storedInfo == "")
				fillInfo();
			return storedInfo;
		}

		private ModuleBaseRotate parentBaseRotate = null;

		private void fillParentBaseRotate()
		{
			parentBaseRotate = null;
			for (Part p = part.parent; p; p = p.parent) {
				parentBaseRotate = p.FindModuleImplementing<ModuleBaseRotate>();
				if (parentBaseRotate)
					break;
			}
		}

		private List<CModuleStrut> crossStruts = new List<CModuleStrut>();

		private void fillCrossStruts()
		{
			crossStruts.Clear();
			List<CModuleStrut> allStruts = vessel.FindPartModulesImplementing<CModuleStrut>();
			if (allStruts == null)
				return;
			PartSet rotParts = PartSet.allPartsFromHere(part);
			for (int i = 0; i < allStruts.Count; i++) {
				PartJoint sj = allStruts[i] ? allStruts[i].strutJoint : null;
				if (sj && sj.Host && sj.Target && rotParts.contains(sj.Host) != rotParts.contains(sj.Target))
					crossStruts.Add(allStruts[i]);
			}
		}

		[KSPField(isPersistant = true)]
		public Vector3 frozenRotation = Vector3.zero;

		public bool frozenFlag {
			get => !frozenAngle.isZero();
		}

		public float frozenAngle {
			get => frozenRotation[0];
			set => frozenRotation[0] = value;
		}

		public float frozenSpeed {
			get => frozenRotation[1];
			set => frozenRotation[1] = value;
		}

		public float frozenStartSpeed {
			get => frozenRotation[2];
			set => frozenRotation[2] = value;
		}

		[KSPField(isPersistant = true)]
		public float electricityRate = 1f;

		public Part getPart()
		{
			return part;
		}

		protected int setupDoneAt = 0;
		protected bool setupDone {
			get => setupDoneAt != 0;
		}

		public void onFieldChange(string name, Callback<BaseField, object> fun)
		{
			BaseField fld = Fields[name];
			if (fld == null) {
				log(desc(), ".onFieldChange(\"" + name + "\") can't find field");
				return;
			}

			if (fld.uiControlEditor != null)
				fld.uiControlEditor.onFieldChanged = fun;
			if (fld.uiControlFlight != null)
				fld.uiControlFlight.onFieldChanged = fun;
		}

		public void testCallback(BaseField fld, object oldValue)
		{
			string name = fld != null ? fld.name : "<null>";
			object newValue = fld.GetValue(this);
			log(desc(), ": CHANGED " + name + ": " + oldValue + " -> " + newValue);
		}

		protected virtual void doSetup(bool onLaunch)
		{
			if (hasJointMotion && jointMotion.rotCur) {
				log(desc(), ": skipping, is rotating");
				return;
			}

			jointMotion = null;
			hasJointMotion = false;
			nodeRole = "None";
			anglePosition = rotationAngle();
			angleVelocity = 0f;
			angleIsMoving = false;
			needsAlignment = false;
			enabled = false;
			stagingEnabled = false;

			if (!part || !vessel || !setupLocalAxisDone) {
				log("" + GetType(), ": *** WARNING *** doSetup() called at a bad time");
				return;
			}

			if (!part.hasPhysics()) {
				log(desc(), ".doSetup(): physicsless part, disabled");
				return;
			}

			try {
				if (onLaunch)
					log(part.desc(), ".doSetup(): at launch");

				fillParentBaseRotate();
				fillCrossStruts();
				setupGuiActive();
				PartJoint rotatingJoint = findMovingJoint(verboseSetup);

				if (rotatingJoint && !rotatingJoint.safetyCheck()) {
					log(part.desc(), ": joint safety check failed for "
						+ rotatingJoint.desc());
					rotatingJoint = null;
				}

				if (rotatingJoint) {
					jointMotion = JointMotion.get(rotatingJoint);
					hasJointMotion = jointMotion;
					if (!jointMotion.hasController())
						jointMotion.controller = this;
					jointMotion.updateOrgRot();
					anglePosition = rotationAngle();
				}

				onFieldChange(nameof(rotationEnabled), testCallback);
				onFieldChange(nameof(rotationStep), testCallback);
				onFieldChange(nameof(rotationSpeed), testCallback);
			} catch (Exception e) {
				string sep = new string('-', 80);
				log(sep);
				log("Exception during setup:\n" + e.StackTrace);
				log(sep);
			}

			if (hasJointMotion) {
				nodeRole = part == jointMotion.joint.Host ? "Host"
					: part == jointMotion.joint.Target ? "Target"
					: "Unknown";
				if (jointMotion.joint.isOffTree())
					nodeRole += "OT";
			}

			log(desc(), ".doSetup(): joint " + (hasJointMotion ? jointMotion.joint.desc() : "null"));

			setupGroup();

			setupDoneAt = Time.frameCount;

			enabled = hasJointMotion;
		}

		public IEnumerator doSetupDelayed(bool onLaunch)
		{
			yield return new WaitForFixedUpdate();
			doSetup(onLaunch);
		}

		private bool care(Vessel v)
		{
			return v == vessel;
		}

		private bool care(Part p)
		{
			return p && p.vessel == vessel;
		}

		private bool care(GameEvents.FromToAction<Part, Part> action)
		{
			return care(action.from) || care(action.to);
		}

		private bool care(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			// special case for same vessel dock/undock
			return action.from.part == part || action.to.part == part;
		}

		private bool care(uint id1, uint id2)
		{
			return vessel && (vessel.persistentId == id1 || vessel.persistentId == id2);
		}

		protected void scheduleDockingStatesCheck(bool verbose)
		{
			VesselMotionManager vmm = VesselMotionManager.get(vessel);
			if (vmm)
				vmm.scheduleDockingStatesCheck(verbose);
		}

		public void OnVesselGoOnRails(Vessel v)
		{
			bool c = care(v);
			evlog(nameof(OnVesselGoOnRails), v, c);
			if (!c) return;
			freezeCurrentRotation("go on rails", false);
			setupDoneAt = 0;
			VesselMotionManager vmm = VesselMotionManager.get(vessel);
			if (vmm)
				vmm.resetRotCount();
		}

		public void OnVesselGoOffRails(Vessel v)
		{
			bool c = care(v);
			evlog(nameof(OnVesselGoOffRails), v, c);
			if (!c) return;

			// start speed always 0 when going off rails
			frozenStartSpeed = 0f;

			setupDoneAt = 0;
			doSetup(justLaunched);
			justLaunched = false;

			scheduleDockingStatesCheck(false);
		}

		public void RightBeforeStructureChange_Action(GameEvents.FromToAction<Part, Part> action)
		{
			bool c = care(action);
			evlog(nameof(RightBeforeStructureChange_Action), action, c);
			if (!c) return;
			RightBeforeStructureChange();
		}

		public void RightBeforeStructureChange_Ids(uint id1, uint id2)
		{
			bool c = care(id1, id2);
			evlog(nameof(RightBeforeStructureChange_Ids), id1, id2, c);
			if (!c) return;
			RightBeforeStructureChange();
		}

		private void RightBeforeStructureChange_JointUpdate(Vessel v)
		{
			bool c = care(v);
			evlog(nameof(RightBeforeStructureChange_JointUpdate), v, c);
			if (!c) return;
			RightBeforeStructureChange();
		}

		public void RightBeforeStructureChange_Part(Part p)
		{
			bool c = care(p);
			evlog(nameof(RightBeforeStructureChange_Part), p, c);
			if (!c) return;
			RightBeforeStructureChange();
		}

		public void RightAfterStructureChange_Action(GameEvents.FromToAction<Part, Part> action)
		{
			bool c = care(action);
			evlog(nameof(RightAfterStructureChange_Action), action, c);
			if (!c) return;
			RightAfterStructureChange();
		}

		public void RightAfterStructureChange_Part(Part p)
		{
			bool c = !setupDone || care(p);
			evlog(nameof(RightAfterStructureChange_Part), p, c);
			if (!c)
				log(desc(), ": setupDoneAt=" + setupDoneAt);
			if (!c) return;
			RightAfterStructureChange();
		}

		public void RightAfterSameVesselDock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			bool c = care(action);
			evlog(nameof(RightAfterSameVesselDock), action, c);
			if (!c) return;
			RightAfterStructureChangeDelayed();
			scheduleDockingStatesCheck(false);
		}

		public void RightAfterSameVesselUndock(GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action)
		{
			bool c = care(action);
			evlog(nameof(RightAfterSameVesselUndock), action, c);
			if (!c) return;
			RightAfterStructureChangeDelayed();
			scheduleDockingStatesCheck(false);
		}

		public void RightAfterEditorChange_ShipModified(ShipConstruct ship)
		{
			RightAfterEditorChange("MODIFIED");
		}

		public void RightAfterEditorChange_Event(ConstructionEventType type, Part part)
		{
			if (type == ConstructionEventType.PartDragging
				|| type == ConstructionEventType.PartOffsetting
				|| type == ConstructionEventType.PartRotating)
				return;
			RightAfterEditorChange("EVENT " + type);
		}

		public void RightAfterEditorChange(string msg)
		{
			if (verboseEvents)
				log(desc(), ".RightAfterEditorChange(" + msg + ")"
					+ " > [" + part.children.Count + "]"
					+ " < " + part.parent.desc() + " " + part.parent.descOrg());

			float angle = rotationAngle();
			if (float.IsNaN(angle)) {
				angleInfo = angleInfoNA;
			} else {
				angleInfo = String.Format("{0:+0.00;-0.00;0.00}\u00b0", angle);
			}

			checkGuiActive();
		}

		public void RightBeforeStructureChange()
		{
			freezeCurrentRotation("structure change", true);
			setupDoneAt = 0;
		}

		public void RightAfterStructureChange()
		{
			doSetup(false);
		}

		public void RightAfterStructureChangeDelayed()
		{
			StartCoroutine(doSetupDelayed(false));
		}

		private bool eventState = false;

		private void setEvents(bool cmd)
		{
			if (cmd == eventState) {
				if (verboseSetup)
					log(desc(), ".setEvents(" + cmd + ") repeated");
				return;
			}

			if (verboseSetup)
				log(desc(), ".setEvents(" + cmd + ")");

			if (cmd) {
				GameEvents.onActiveJointNeedUpdate.Add(RightBeforeStructureChange_JointUpdate);

				GameEvents.onEditorShipModified.Add(RightAfterEditorChange_ShipModified);
				GameEvents.onEditorPartEvent.Add(RightAfterEditorChange_Event);

				GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
				GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);

				GameEvents.onPartCouple.Add(RightBeforeStructureChange_Action);
				GameEvents.onPartCoupleComplete.Add(RightAfterStructureChange_Action);
				GameEvents.onPartDeCouple.Add(RightBeforeStructureChange_Part);
				GameEvents.onPartDeCoupleComplete.Add(RightAfterStructureChange_Part);

				GameEvents.onVesselDocking.Add(RightBeforeStructureChange_Ids);
				GameEvents.onDockingComplete.Add(RightAfterStructureChange_Action);
				GameEvents.onPartUndock.Add(RightBeforeStructureChange_Part);
				GameEvents.onPartUndockComplete.Add(RightAfterStructureChange_Part);

				GameEvents.onSameVesselDock.Add(RightAfterSameVesselDock);
				GameEvents.onSameVesselUndock.Add(RightAfterSameVesselUndock);
			} else {
				GameEvents.onActiveJointNeedUpdate.Remove(RightBeforeStructureChange_JointUpdate);

				GameEvents.onEditorShipModified.Remove(RightAfterEditorChange_ShipModified);
				GameEvents.onEditorPartEvent.Remove(RightAfterEditorChange_Event);

				GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
				GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);

				GameEvents.onPartCouple.Remove(RightBeforeStructureChange_Action);
				GameEvents.onPartCoupleComplete.Remove(RightAfterStructureChange_Action);
				GameEvents.onPartDeCouple.Remove(RightBeforeStructureChange_Part);
				GameEvents.onPartDeCoupleComplete.Remove(RightAfterStructureChange_Part);

				GameEvents.onVesselDocking.Remove(RightBeforeStructureChange_Ids);
				GameEvents.onDockingComplete.Remove(RightAfterStructureChange_Action);
				GameEvents.onPartUndock.Remove(RightBeforeStructureChange_Part);
				GameEvents.onPartUndockComplete.Remove(RightAfterStructureChange_Part);

				GameEvents.onSameVesselDock.Remove(RightAfterSameVesselDock);
				GameEvents.onSameVesselUndock.Remove(RightAfterSameVesselUndock);
			}

			eventState = cmd;
		}

		private static readonly string[,] guiList = {
			// flags:
			// S: is a setting
			// C: is a command
			// D: is a debug display
			// A: show with rotation disabled
			// F: show when needsAlignment
			{ "nodeRole", "S" },
			{ "rotationStep", "S" },
			{ "rotationSpeed", "S" },
			{ "reverseRotation", "S" },
			{ "flipFlopMode", "S" },
			{ "smartAutoStruts", "SD" },
			{ "RotateClockwise", "C" },
			{ "RotateCounterclockwise", "C" },
			{ "RotateToSnap", "CF" },
			{ "autoSnap", "D" },
			{ "hideCommands", "D" }
		};

		private struct GuiInfo {
			public string name;
			public string flags;
			public BaseField fld;
			public BaseEvent evt;
		}

		private GuiInfo[] guiInfo;

		protected void setupGuiActive()
		{
			int l = guiList.GetLength(0);

			guiInfo = new GuiInfo[l];

			for (int i = 0; i < l; i++) {
				string n = guiList[i, 0];
				string f = guiList[i, 1];
				ref GuiInfo ii = ref guiInfo[i];
				ii.name = n;
				ii.flags = f;
				ii.fld = Fields[n];
				ii.evt = Events[n];
			}

			StopRotationEvent = Events["StopRotation"];
#if DEBUG
			ToggleAutoStrutDisplayEvent = Events["ToggleAutoStrutDisplay"];
			ToggleTraceEventsEvent = Events["ToggleTraceEvents"];
#endif
		}

		private void checkGuiActive()
		{
			if (guiInfo != null) {
				bool csr = canStartRotation(false);
				bool csra = canStartRotation(false, true);
				for (int i = 0; i < guiInfo.Length; i++) {
					ref GuiInfo ii = ref guiInfo[i];
					bool flagsCheck = !(hideCommands && ii.flags.IndexOf('C') >= 0)
						&& (DEBUGMODE || ii.flags.IndexOf('D') < 0);
					if (ii.fld != null)
						ii.fld.guiActive = ii.fld.guiActiveEditor = rotationEnabled && flagsCheck;
					if (ii.evt != null)
						ii.evt.guiActive = ii.evt.guiActiveEditor = flagsCheck
							&& (ii.flags.IndexOf('A') >= 0 ? csra : csr);
					if (needsAlignment && ii.flags.IndexOf('F') >= 0)
						ii.evt.guiActive = true;
				}
			}

			if (StopRotationEvent != null)
				StopRotationEvent.guiActive = currentRotation();

#if DEBUG
			if (ToggleAutoStrutDisplayEvent != null)
				ToggleAutoStrutDisplayEvent.guiName = PhysicsGlobals.AutoStrutDisplay ?
					"Hide Autostruts" : "Show Autostruts";

			if (ToggleTraceEventsEvent != null)
				ToggleTraceEventsEvent.guiName = GameEvents.debugEvents ?
					"Stop Event Trace" : "Start Event Trace";
#endif

			if (part.PartActionWindow != null)
				setupGroup();
		}

		private void setupGroup()
		{
			bool expanded = hasJointMotion && (rotationEnabled || needsAlignment);
			List<BasePAWGroup> l = allGroups(GROUPNAME);
			for (int i = 0; i < l.Count; i++)
				l[i].startCollapsed = !expanded;
		}

		private List<BasePAWGroup> cached_allGroups = null;

		private List<BasePAWGroup> allGroups(string name)
		{
			if (cached_allGroups == null) {
				cached_allGroups = new List<BasePAWGroup>();
				for (int i = 0; i < Fields.Count; i++)
					if (Fields[i] != null && Fields[i].group != null && Fields[i].group.name == name)
						cached_allGroups.Add(Fields[i].group);
				for (int i = 0; i < Events.Count; i++)
					if (Events[i] != null && Events[i].group != null && Events[i].group.name == name)
						cached_allGroups.Add(Events[i].group);
			}
			return cached_allGroups;
		}

		public override void OnAwake()
		{
			base.OnAwake();
			setupDoneAt = 0;
		}

		private bool justLaunched = false;

		public override void OnStart(StartState state)
		{
#if !DEBUG
			verboseSetup = verboseEvents = false;
#endif
			justLaunched = state == StartState.PreLaunch;

			if (verboseEvents)
				log(desc(), ".OnStart(" + state + ")");

			base.OnStart(state);

			checkRevision();

			setupLocalAxisDone = setupLocalAxis(state);

			setupGuiActive();

			setEvents(true);
			if (state == StartState.Editor) {
				RightAfterEditorChange("START");
				return;
			}

			if (vessel) {
				VesselMotionManager.get(vessel); // force creation of VesselMotionManager
			} else if (state != StartState.Editor) {
				log(desc(), ".OnStart(" + state + ") with no vessel");
			}

			checkGuiActive();
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			JointMotionObj cr = currentRotation();

			anglePosition = rotationAngle();
			angleVelocity = cr ? cr.vel : 0f;
			angleIsMoving = cr;

			needsAlignment = hasJointMotion && !angleIsMoving
				&& Mathf.Abs(jointMotion.angleToSnap(rotationStep)) >= .5e-4f;

			if (MapView.MapIsEnabled || !part.PartActionWindow)
				return;

			bool updfrm = ((Time.frameCount + part.flightID) & 3) == 0;
			if (updfrm || cr)
				updateStatus(cr);
			if (updfrm)
				checkGuiActive();
		}

		public virtual void OnDestroy()
		{
			setEvents(false);
		}

		protected virtual void updateStatus(JointMotionObj cr)
		{
			if (cr) {
				angleInfo = String.Format(
					"{0:+0.00;-0.00;0.00}\u00b0 > {1:+0.00;-0.00;0.00}\u00b0 ({2:+0.00;-0.00;0.00}\u00b0/s){3}",
					anglePosition, jointMotion.rotationTarget(),
					cr.vel, (jointMotion.controller == this ? " CTL" : ""));
			} else {
				if (float.IsNaN(anglePosition)) {
					angleInfo = angleInfoNA;
				} else {
					angleInfo = String.Format(
						"{0:+0.00;-0.00;0.00}\u00b0 ({1:+0.0000;-0.0000;0.0000}\u00b0\u0394)",
						anglePosition, dynamicDeltaAngle());
				}
			}
#if DEBUG
			int nJoints = hasJointMotion ? jointMotion.joint.joints.Count : 0;
			nodeStatus = part.flightID + ":" + nodeRole + "[" + nJoints + "]";
			if (frozenFlag)
				nodeStatus += " [F]";
			if (cr)
				nodeStatus += " " + cr.pos + "\u00b0 -> " + cr.tgt + "\u00b0";
#endif
		}

		protected virtual bool canStartRotation(bool verbose, bool ignoreDisabled = false)
		{
			string failMsg = "";

			if (HighLogic.LoadedSceneIsEditor) {
				if (!rotationEnabled) {
					failMsg = "rotation disabled";
				} else if (!findHostPartInEditor(verbose)) {
					failMsg = "can't find host part";
				}
			} else {
				if (!rotationEnabled && !ignoreDisabled) {
					failMsg = "rotation disabled";
				} else if (!setupDone) {
					failMsg = "not set up";
				} else if (!hasJointMotion) {
					failMsg = "no joint motion";
				} else if (!vessel) {
					failMsg = "no vessel";
				} else if (vessel.CurrentControlLevel != Vessel.ControlLevel.FULL) {
					failMsg = "uncontrolled vessel";
				}
			}

			if (verbose && failMsg != "")
				log(desc(), ".canStartRotation(): " + failMsg);

			return failMsg == "";
		}

		public float step()
		{
			float s = Mathf.Abs(rotationStep);
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

		private Part findHostPartInEditor(bool verbose)
		{
			AttachNode node = findMovingNodeInEditor(out Part other, verbose);
			if (node == null || other == null)
				return null;
			return other.parent == part ? other :
				part.parent == other ? part :
				null;
		}

		protected float rotationAngle()
		{
			if (HighLogic.LoadedSceneIsEditor) {
				Part host = findHostPartInEditor(false);
				if (!host || !host.parent)
					return float.NaN;
				Part target = host.parent;
				Vector3 axis = host == part ? partNodeAxis : -partNodeAxis;
				Vector3 hostNodeAxis = axis.Td(part.T(), host.T());
				return hostNodeAxis.axisSignedAngle(hostNodeAxis.findUp(),
					hostNodeAxis.Td(host.T(), target.T()).findUp().Td(target.T(), host.T()));
			}

			return hasJointMotion ? jointMotion.rotationAngle() : float.NaN;
		}

		protected float dynamicDeltaAngle()
		{
			if (!HighLogic.LoadedSceneIsFlight)
				return 0f;
			return hasJointMotion ? jointMotion.dynamicDeltaAngle() : float.NaN;
		}

		public void putAxis(JointMotion jm)
		{
			jm.setAxis(part, partNodeAxis, partNodePos);
		}

		protected bool enqueueRotation(Vector3 frozen)
		{
			return enqueueRotation(frozen[0], frozen[1], frozen[2]);
		}

		protected bool enqueueRotation(float angle, float speed, float startSpeed = 0f)
		{
			if (HighLogic.LoadedSceneIsEditor) {
				log(desc(), ".enqueueRotation(): " + angle + "\u00b0 in editor");

				Part host = findHostPartInEditor(false);
				if (!host || !host.parent)
					return false;

				Vector3 axis = host == part ? -partNodeAxis : partNodeAxis;
				axis = axis.Td(part.T(), null);
				Vector3 pos = partNodePos.Tp(part.T(), null);
				Quaternion rot = axis.rotation(angle);

				Transform t = host.transform;
				t.SetPositionAndRotation(rot * (t.position - pos) + pos,
					rot * t.rotation);

				GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartRotated, host);
				GameEvents.onEditorPartEvent.Fire(ConstructionEventType.PartTweaked, host);
				return true;
			}

			if (!hasJointMotion) {
				log(desc(), ".enqueueRotation(): no rotating joint, skipped");
				return false;
			}
			enabled = true;
			return jointMotion.enqueueRotation(this, angle, speed, startSpeed);
		}

		protected void enqueueRotationToSnap(float snap, float speed)
		{
			if (snap < 0.1f)
				snap = 15f;

			if (HighLogic.LoadedSceneIsEditor) {
				float a = rotationAngle();
				if (float.IsNaN(a))
					return;
				float f = snap * Mathf.Floor(a / snap + 0.5f);
				enqueueRotation(f - a, speed);
				return;
			}

			if (!hasJointMotion)
				return;
			enqueueRotation(jointMotion.angleToSnap(snap), speed);
		}

		protected void freezeCurrentRotation(string msg, bool keepSpeed)
		{
			JointMotionObj cr = currentRotation();
			if (!cr)
				return;
			if (cr.controller != this) {
				log(desc(), ".freezeCurrentRotation(): skipping, not controller");
				return;
			}
			log(desc(), ".freezeCurrentRotation("
				+ msg + ", " + keepSpeed + ")");
			cr.isContinuous();
			float angle = cr.tgt - cr.pos;
			enqueueFrozenRotation(angle, cr.maxvel, keepSpeed ? cr.vel : 0f);
			cr.abort();
			log(desc(), ": removing rotation (freeze)");
			jointMotion.rotCur = null;
		}

		public bool isRotating()
		{
			return currentRotation();
		}

		protected JointMotionObj currentRotation()
		{
			return hasJointMotion ? jointMotion.rotCur : null;
		}

		protected void checkFrozenRotation()
		{
			if (!setupDone)
				return;

			if (frozenFlag) {
				/* // logging disabled, it always happens during continuous rotation
				log(desc(), ": thaw frozen rotation " + frozenRotation.desc()
					+ "@" + frozenRotationControllerID);
				*/
				enqueueRotation(frozenRotation);
			}

			updateFrozenRotation("CHECK");
		}

		public void updateFrozenRotation(string context)
		{
			Vector3 prevRot = frozenRotation;

			JointMotionObj cr = currentRotation();
			if (cr && cr.isContinuous() && jointMotion.controller == this) {
				frozenRotation.Set(cr.tgt, cr.maxvel, 0f);
			} else {
				frozenRotation = Vector3.zero;
			}

			if (frozenRotation != prevRot)
				log(desc(), ".updateFrozenRotation("
					+ context + "): " + prevRot + " -> " + frozenRotation);
		}

		protected void enqueueFrozenRotation(float angle, float speed, float startSpeed = 0f)
		{
			Vector3 prev = frozenRotation;
			angle += frozenAngle;
			SmoothMotion.isContinuous(ref angle);
			frozenRotation.Set(angle, speed, startSpeed);
			log(desc(), ".enqueueFrozenRotation(): "
				+ prev.desc() + " -> " + frozenRotation.desc());
		}

		private int lastUsefulFixedUpdate = 0;

		public void FixedUpdate()
		{
			if (setupDone && HighLogic.LoadedSceneIsFlight)
				checkFrozenRotation();

			if (lastUsefulFixedUpdate < setupDoneAt) {
				lastUsefulFixedUpdate = setupDoneAt;
			} else if (frozenFlag || currentRotation() != null) {
				lastUsefulFixedUpdate = Time.frameCount;
			} else if (Time.frameCount - lastUsefulFixedUpdate > 10) {
				// log(part.desc(), ": disabling useless MonoBehaviour updates");
				enabled = false;
			}
		}

		public string desc(bool bare = false)
		{
			return (bare ? "" : descPrefix() + ":") + part.desc(true);
		}

		public abstract string descPrefix();

		protected void evlog(string name, Vessel v, bool care)
		{
			evlog(name + "(" + v.desc() + ")", care);
		}

		protected void evlog(string name, Part p, bool care)
		{
			evlog(name + "(" + p.desc() + ")", care);
		}

		protected void evlog(string name, GameEvents.FromToAction<Part, Part> action, bool care)
		{
			evlog(name + "(" + action.from.desc() + ", " + action.to.desc() + ")", care);
		}

		protected void evlog(string name, GameEvents.FromToAction<ModuleDockingNode, ModuleDockingNode> action, bool care)
		{
			evlog(name + "(" + action.from.part.desc() + ", " + action.to.part.desc() + ")", care);
		}

		protected void evlog(string name, uint id1, uint id2, bool care)
		{
			evlog(name + "(" + id1 + ", " + id2 + ")", care);
		}

		protected void evlog(string name, bool care)
		{
			if (verboseEvents)
				log(desc(), ": *** EVENT *** " + name + ", " + (care ? "care" : "don't care"));
		}

		protected static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}

