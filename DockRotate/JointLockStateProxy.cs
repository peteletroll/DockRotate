using System;
using System.Collections.Generic;
using UnityEngine;

namespace DockRotate
{
	public class JointLockStateProxy: MonoBehaviour, IJointLockState
	{
		private bool verboseEvents = false;
		private Part part;

		private List<IJointLockState> tgt;

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

		public void add(IJointLockState jls)
		{
			if (tgt == null)
				tgt = new List<IJointLockState>();
			if (tgt.Contains(jls)) {
				log(desc(), ".add(): skip adding duplicate");
				return;
			}
			tgt.Add(jls);
		}

		public void OnDestroy()
		{
			log(desc(), ".OnDestroy()");
		}

		public bool IsJointUnlocked()
		{
			if (verboseEvents)
				log(desc(), ".IsJointUnLocked()");
			if (tgt == null)
				return false;
			for (int i = 0; i < tgt.Count; i++)
				if (tgt[i] != null && tgt[i].IsJointUnlocked())
					return true;
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

