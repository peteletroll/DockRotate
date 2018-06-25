* better handling of docking port state changes
	* especially to fix adjusting multiple docking ports

* try using GameEvents.onVesselStandardModification to check
  for vessel structure changes

* experiment with IJointLockState.IsJointUnlocked()
  (found KSP bug)

* refactor ConfigurableJoint operations in a dedicated class
  (in progress, see JointManager class)
