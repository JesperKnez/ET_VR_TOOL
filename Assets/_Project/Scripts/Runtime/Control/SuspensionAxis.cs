using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// One tilt axis of a suspended mirror, modelled as a damped pendulum (spring-mass-damper):
    /// acceleration = w0^2 (target - angle) - 2 zeta w0 velocity.
    /// Real interferometer mirrors hang from pendulums, so when an actuator pushes them they swing
    /// and settle instead of jumping to the new angle.
    /// </summary>
    public struct SuspensionAxis
    {
        private const float MaxSubStep = 1f / 240f;

        /// <summary>Current angle in degrees.</summary>
        public float Angle;

        /// <summary>Current angular velocity in degrees per second.</summary>
        public float Velocity;

        public SuspensionAxis(float angle)
        {
            Angle = angle;
            Velocity = 0f;
        }

        /// <summary>
        /// Advance the pendulum by <paramref name="deltaTime"/> seconds towards <paramref name="target"/>.
        /// Uses small semi-implicit Euler sub-steps, which stay stable for any frame rate.
        /// </summary>
        public void Step(float target, float resonanceFrequencyHz, float dampingRatio, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            float omega = 2f * Mathf.PI * Mathf.Max(0.01f, resonanceFrequencyHz);
            int steps = Mathf.Max(1, Mathf.CeilToInt(deltaTime / MaxSubStep));
            float h = deltaTime / steps;

            for (int i = 0; i < steps; i++)
            {
                float acceleration = omega * omega * (target - Angle) - 2f * dampingRatio * omega * Velocity;
                Velocity += acceleration * h;
                Angle += Velocity * h;
            }
        }

        /// <summary>Jump straight to an angle and stop moving.</summary>
        public void SnapTo(float angle)
        {
            Angle = angle;
            Velocity = 0f;
        }

        /// <summary>True while the axis is still noticeably moving or away from its target.</summary>
        public bool IsMoving(float target, float angleThreshold = 0.005f, float velocityThreshold = 0.01f)
        {
            return Mathf.Abs(Velocity) > velocityThreshold || Mathf.Abs(target - Angle) > angleThreshold;
        }
    }
}
