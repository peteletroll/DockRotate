using System.Collections.Generic;
using KSP.Localization;

namespace DockRotate
{
	public class ModuleNodeRotate: ModuleBaseRotate
	{
		[KSPField(isPersistant = true)]
		public string rotatingNodeName = "";

		[KSPField(isPersistant = true)]
		public bool enableJointMotionProxy = true;

		[KSPField(isPersistant = true)]
		public uint otherPartFlightID = 0;

		public AttachNode rotatingNode;

		private bool isSrfAttach()
		{
			return rotatingNodeName == "srfAttach";
		}

		protected override void fillInfo()
		{
			storedModuleDisplayName = Localizer.Format("#DCKROT_node_displayname");
			storedInfo = Localizer.Format("#DCKROT_node_info", rotatingNodeName);
		}

		protected override void doSetup(bool onLaunch)
		{
			base.doSetup(onLaunch);
			// TODO: change groupDisplayName to "NodeRotate <node name>"
		}

		protected override AttachNode findMovingNodeInEditor(out Part otherPart, bool verbose)
		{
			otherPart = null;
			if (rotatingNode == null)
				return null;
			if (verbose)
				log(desc(), ".findMovingNodeInEditor(): rotatingNode = " + rotatingNode.desc());
			otherPart = rotatingNode.attachedPart;
			if (verbose)
				log(desc(), ".findMovingNodeInEditor(): otherPart = " + otherPart.desc());
			if (!otherPart)
				return null;
			if (verbose)
				log(desc(), ".findMovingNodeInEditor(): attachedPart = " + rotatingNode.attachedPart.desc());
			return rotatingNode;
		}

		protected override bool setupLocalAxis(StartState state)
		{
			rotatingNode = part.FindAttachNode(rotatingNodeName);
			if (rotatingNode == null && isSrfAttach())
				rotatingNode = part.srfAttachNode;

			if (rotatingNode == null) {
				log(desc(), ".setupLocalAxis(" + state + "): "
					+ "no node \"" + rotatingNodeName + "\"");

				List<AttachNode> nodes = part.namedAttachNodes();
				for (int i = 0; i < nodes.Count; i++)
					log(desc(), ": node[" + i + "] = " + nodes[i].desc());
				return false;
			}

			partNodePos = rotatingNode.position;
			partNodeAxis = rotatingNode.orientation;
			if (verboseSetup)
				log(desc(), ".setupLocalAxis(" + state + ") done: "
					+ partNodeAxis + "@" + partNodePos);
			return true;
		}

		protected override PartJoint findMovingJoint(bool verbose)
		{
			uint prevOtherPartFlightID = otherPartFlightID;
			otherPartFlightID = 0;

			if (rotatingNode == null || !rotatingNode.owner) {
				if (verbose)
					log(desc(), ".findMovingJoint(): no node");
				return null;
			}

			if (part.FindModuleImplementing<ModuleDockRotate>()) {
				log(desc(), ".findMovingJoint(): has DockRotate, NodeRotate disabled");
				return null;
			}

			Part owner = rotatingNode.owner;
			Part other = rotatingNode.attachedPart;
			if (!other) {
				if (verbose)
					log(desc(), ".findMovingJoint(" + rotatingNode.id + "): attachedPart is null, try by id = "
						+ prevOtherPartFlightID);
				other = findOtherById(prevOtherPartFlightID);
			}
			if (!other) {
				if (verbose)
					log(desc(), ".findMovingJoint(" + rotatingNode.id + "): no attachedPart");
				return null;
			}
			if (verbose && other.flightID != prevOtherPartFlightID)
				log(desc(), ".findMovingJoint(" + rotatingNode.id + "): otherFlightID "
					+ prevOtherPartFlightID + " -> " + other.flightID);
			if (verbose)
				log(desc(), ".findMovingJoint(" + rotatingNode.id + "): attachedPart is " + other.desc());
			other.forcePhysics();
			if (enableJointMotionProxy && HighLogic.LoadedSceneIsFlight)
				JointLockStateProxy.register(other, this);

			if (owner.parent == other) {
				PartJoint ret = owner.attachJoint;
				if (verbose)
					log(desc(), ".findMovingJoint(" + rotatingNode.id + "): child " + ret.desc());
				otherPartFlightID = other.flightID;
				return ret;
			}

			if (other.parent == owner) {
				PartJoint ret = other.attachJoint;
				if (verbose)
					log(desc(), ".findMovingJoint(" + rotatingNode.id + "): parent " + ret.desc());
				otherPartFlightID = other.flightID;
				return ret;
			}

			if (verbose)
				log(desc(), ".findMovingJoint(" + rotatingNode.id + "): nothing");
			return null;
		}

		private Part findOtherById(uint id)
		{
			if (id == 0)
				return null;
			if (part.parent && part.parent.flightID == id)
				return part.parent;
			if (part.children != null)
				for (int i = 0; i < part.children.Count; i++)
					if (part.children[i] && part.children[i].flightID == id)
						return part.children[i];
			return null;
		}

		public override string descPrefix()
		{
			return "MNR";
		}
	}
}

