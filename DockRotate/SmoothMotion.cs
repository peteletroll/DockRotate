using UnityEngine;

namespace DockRotate
{
	public abstract class SmoothMotion
	{
		public float pos;
		public float vel;
		public float tgt;

		public const float CONTINUOUS = 999999f;

		public float maxvel = 1f;
		private float maxacc = 1f;

		private bool braking = false;

		private bool started = false, finished = false;

		public float elapsed = 0f;

		private const float accelTime = 2f;
		private const float stopMargin = 1.5f;

		protected abstract void onStart();
		protected abstract void onStep(float deltat);
		protected abstract void onStop();

		public float curBrakingSpace(float deltat = 0f)
		{
			float time = Mathf.Abs(vel) / maxacc + 2f * stopMargin * deltat;
			return vel / 2f * time;
		}

		public void advance(float deltat)
		{
			if (finished)
				return;

			isContinuous(); // normalizes tgt for continuous rotation

			maxacc = Mathf.Clamp(maxvel / accelTime, 1f, 180f);

			bool goingRightWay = (tgt - pos) * vel >= 0f;
			float brakingSpace = Mathf.Abs(curBrakingSpace(deltat));

			float newvel = vel;

			if (goingRightWay && Mathf.Abs(vel) <= maxvel && Mathf.Abs(tgt - pos) > brakingSpace) {
				// driving
				newvel += deltat * Mathf.Sign(tgt - pos) * maxacc;
				newvel = Mathf.Clamp(newvel, -maxvel, maxvel);
			} else {
				// braking
				newvel -= deltat * Mathf.Sign(vel) * maxacc;
			}

			if (!started) {
				onStart();
				started = true;
			}

			vel = newvel;
			pos += deltat * vel;
			elapsed += deltat;

			onStep(deltat);

			if (!finished && checkFinished(deltat))
				onStop();
		}

		public void abort()
		{
			finished = true;
			onStop();
		}

		public void brake()
		{
			tgt = pos + curBrakingSpace();
			braking = true;
		}

		public bool isBraking()
		{
			return braking;
		}

		public bool clampAngle()
		{
			if (pos < -3600f || pos > 3600f) {
				float newzero = 360f * Mathf.Floor(pos / 360f + 0.5f);
				// ModuleBaseRotate.log("clampAngle(): newzero " + newzero + " from pos " + pos);
				tgt -= newzero;
				pos -= newzero;
				return true;
			}
			return false;
		}

		public static bool isContinuous(ref float target)
		{
			if (Mathf.Abs(target) > CONTINUOUS / 2f) {
				target = Mathf.Sign(target) * CONTINUOUS;
				return true;
			}
			return false;
		}

		public bool isContinuous()
		{
			return isContinuous(ref tgt);
		}

		private bool checkFinished(float deltat)
		{
			if (finished)
				return true;
			if (Mathf.Abs(vel) < stopMargin * deltat * maxacc
				&& Mathf.Abs(tgt - pos) < deltat * deltat * maxacc) {
				finished = true;
				pos = tgt;
			}
			return finished;
		}

		public bool done()
		{
			return finished;
		}
	}
}

