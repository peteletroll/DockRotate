using System;
using UnityEngine;

namespace DockRotate
{
	public class JointLockStateProxy: MonoBehaviour, IJointLockState
	{
		private bool verboseEvents = false;
		private Part part;

		public static JointLockStateProxy get(Part p)
		{
			if (!p)
				return null;

			JointLockStateProxy jlsp = p.gameObject.GetComponent<JointLockStateProxy>();
			if (!jlsp) {
				jlsp = p.gameObject.AddComponent<JointLockStateProxy>();
				jlsp.part = p;
				log(nameof(JointLockStateProxy), ".get(" + p.desc() + ") created " + jlsp.desc());
			}

			return jlsp;
		}


		public void Awake()
		{
		}

		public void Start()
		{
		}

		public void OnDestroy()
		{
		}

		public bool IsJointUnlocked()
		{
			if (verboseEvents)
				log(desc(), ".IsJointUnLocked()");
			return false;
		}

		public string desc(bool bare = false)
		{
			return (bare ? "" : "JLSP:") + part.desc(true);
		}

		protected static bool log(string msg1, string msg2 = "")
		{
			return Extensions.log(msg1, msg2);
		}
	}
}

