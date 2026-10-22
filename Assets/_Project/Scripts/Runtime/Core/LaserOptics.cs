using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Pure optics maths used by the laser system. No scene access, so it is easy to test.
    /// All lengths are in metres, all angles in radians unless the name says otherwise.
    /// </summary>
    public static class LaserOptics
    {
        /// <summary>Visible range of the human eye in nanometres.</summary>
        public const float MinVisibleWavelengthNm = 380f;
        public const float MaxVisibleWavelengthNm = 750f;

        // Equal-power rings of a unit Gaussian beam (see GaussianPowerInsideCircle).
        private const int RingCount = 128;
        private static readonly float[] s_RingRadii = BuildEqualPowerRingRadii();

        /// <summary>
        /// Law of reflection: angle of incidence equals angle of reflection.
        /// r = d - 2 (d . n) n. Works for both sides of the surface.
        /// </summary>
        public static Vector3 Reflect(Vector3 direction, Vector3 surfaceNormal)
        {
            Vector3 n = surfaceNormal.normalized;
            return (direction - 2f * Vector3.Dot(direction, n) * n).normalized;
        }

        /// <summary>
        /// Snell's law: n1 sin(theta1) = n2 sin(theta2).
        /// <paramref name="normalTowardsIncomingSide"/> must point back towards where the ray comes from.
        /// Returns false on total internal reflection (then <paramref name="refracted"/> is the reflection).
        /// </summary>
        public static bool TryRefract(Vector3 direction, Vector3 normalTowardsIncomingSide, float n1, float n2, out Vector3 refracted)
        {
            Vector3 d = direction.normalized;
            Vector3 n = normalTowardsIncomingSide.normalized;
            float eta = n1 / n2;
            float cosIncidence = -Vector3.Dot(n, d);
            float sin2Transmitted = eta * eta * (1f - cosIncidence * cosIncidence);

            if (sin2Transmitted > 1f)
            {
                refracted = Reflect(d, n);
                return false;
            }

            float cosTransmitted = Mathf.Sqrt(1f - sin2Transmitted);
            refracted = (eta * d + (eta * cosIncidence - cosTransmitted) * n).normalized;
            return true;
        }

        /// <summary>
        /// Where a ray leaves a flat glass plate (like a beam splitter substrate) after refracting
        /// into it at <paramref name="entryPoint"/>. The ray leaves parallel to how it came in,
        /// but shifted sideways. <paramref name="entryNormal"/> points towards the incoming side.
        /// </summary>
        public static Vector3 PlateExitPoint(Vector3 entryPoint, Vector3 direction, Vector3 entryNormal, float thickness, float refractiveIndex)
        {
            if (thickness <= 0f || refractiveIndex <= 0f)
            {
                return entryPoint;
            }

            TryRefract(direction, entryNormal, 1f, refractiveIndex, out Vector3 inside);
            float cosInside = Mathf.Max(1e-4f, -Vector3.Dot(inside, entryNormal.normalized));
            return entryPoint + inside * (thickness / cosInside);
        }

        /// <summary>
        /// Where a ray leaves a plate whose reflective coating is on the FAR side: it refracts in at
        /// <paramref name="entryPoint"/>, reflects off the coating from inside, and comes back out of the
        /// same face. It leaves parallel to a plain reflection, shifted sideways.
        /// <paramref name="entryNormal"/> points towards the incoming side.
        /// </summary>
        public static Vector3 InternalReflectionExitPoint(Vector3 entryPoint, Vector3 direction, Vector3 entryNormal, float thickness, float refractiveIndex)
        {
            if (thickness <= 0f || refractiveIndex <= 0f)
            {
                return entryPoint;
            }

            Vector3 n = entryNormal.normalized;
            TryRefract(direction, n, 1f, refractiveIndex, out Vector3 inside);
            float cosInside = Mathf.Max(1e-4f, -Vector3.Dot(inside, n));
            float length = thickness / cosInside;
            Vector3 atCoating = entryPoint + inside * length;
            Vector3 back = Reflect(inside, n);
            return atCoating + back * length;
        }

        /// <summary>
        /// Textbook sideways shift of a ray through a plate: d = t sin(i) [1 - cos(i) / sqrt(n^2 - sin^2(i))].
        /// </summary>
        public static float PlateLateralShift(float thickness, float incidenceAngleRad, float refractiveIndex)
        {
            float sinI = Mathf.Sin(incidenceAngleRad);
            float cosI = Mathf.Cos(incidenceAngleRad);
            float root = Mathf.Sqrt(Mathf.Max(1e-6f, refractiveIndex * refractiveIndex - sinI * sinI));
            return thickness * sinI * (1f - cosI / root);
        }

        /// <summary>
        /// Radius (1/e^2 intensity) of a Gaussian beam after travelling <paramref name="distance"/>.
        /// Simplified far-field form: w(z) = sqrt(w0^2 + (theta z)^2).
        /// </summary>
        public static float BeamRadiusAt(float waistRadius, float divergenceHalfAngleRad, float distance)
        {
            float spread = divergenceHalfAngleRad * distance;
            return Mathf.Sqrt(waistRadius * waistRadius + spread * spread);
        }

        /// <summary>
        /// Exact power fraction of a centred Gaussian beam inside a circle: 1 - exp(-2 a^2 / w^2).
        /// </summary>
        public static float GaussianPowerInsideCircleCentered(float beamRadius, float apertureRadius)
        {
            if (beamRadius <= 1e-6f)
            {
                return apertureRadius > 0f ? 1f : 0f;
            }

            return 1f - Mathf.Exp(-2f * apertureRadius * apertureRadius / (beamRadius * beamRadius));
        }

        /// <summary>
        /// Fraction of a Gaussian beam (radius <paramref name="beamRadius"/>) that lands inside a round optic
        /// of radius <paramref name="apertureRadius"/> when the beam centre is <paramref name="offset"/> metres
        /// from the optic centre. The rest is "clipped": in a real detector it becomes scattered light.
        /// Integrates over equal-power rings with an exact angular term; accurate to about 0.3%.
        /// </summary>
        public static float GaussianPowerInsideCircle(float beamRadius, float apertureRadius, float offset)
        {
            if (apertureRadius <= 0f)
            {
                return 0f;
            }

            offset = Mathf.Abs(offset);
            if (beamRadius <= 1e-6f)
            {
                return offset <= apertureRadius ? 1f : 0f;
            }

            // Quick exits: beam far inside or far outside the optic.
            if (offset + 2.5f * beamRadius <= apertureRadius)
            {
                return GaussianPowerInsideCircleCentered(beamRadius, apertureRadius - offset);
            }

            if (offset - 2.5f * beamRadius >= apertureRadius)
            {
                return 0f;
            }

            // Split the beam into rings that each carry the same power. For every ring, the part of the
            // ring that lies inside the optic follows from the law of cosines:
            // |r e^(i phi) + d| <= a  <=>  cos(phi) <= (a^2 - r^2 - d^2) / (2 r d).
            float apertureSquared = apertureRadius * apertureRadius;
            float offsetSquared = offset * offset;
            float sum = 0f;
            for (int i = 0; i < s_RingRadii.Length; i++)
            {
                float r = s_RingRadii[i] * beamRadius;
                float denominator = 2f * r * offset;
                if (denominator <= 1e-12f)
                {
                    sum += r <= apertureRadius ? 1f : 0f;
                    continue;
                }

                float cosLimit = (apertureSquared - r * r - offsetSquared) / denominator;
                if (cosLimit >= 1f)
                {
                    sum += 1f;
                }
                else if (cosLimit > -1f)
                {
                    sum += 1f - Mathf.Acos(cosLimit) / Mathf.PI;
                }
            }

            return sum / s_RingRadii.Length;
        }

        /// <summary>
        /// Normalised quadrant-photodiode difference signal for a Gaussian spot that is
        /// <paramref name="offset"/> metres off-centre: erf(sqrt(2) x / w). Range -1..1, 0 = centred.
        /// This is the signal real interferometers use to keep beams centred.
        /// </summary>
        public static float QuadrantSignal(float offset, float beamRadius)
        {
            if (beamRadius <= 1e-6f)
            {
                return Mathf.Sign(offset) * (Mathf.Abs(offset) > 0f ? 1f : 0f);
            }

            return Erf(1.41421356f * offset / beamRadius);
        }

        /// <summary>Error function, Abramowitz &amp; Stegun 7.1.26 (max error 1.5e-7).</summary>
        public static float Erf(float x)
        {
            float sign = Mathf.Sign(x);
            x = Mathf.Abs(x);
            float t = 1f / (1f + 0.3275911f * x);
            float y = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
            return sign * y;
        }

        /// <summary>True when a human can see this wavelength.</summary>
        public static bool IsHumanVisible(float wavelengthNm)
        {
            return wavelengthNm >= MinVisibleWavelengthNm && wavelengthNm <= MaxVisibleWavelengthNm;
        }

        /// <summary>
        /// Approximate display colour of a wavelength (Bruton's piecewise model).
        /// 532 nm gives the green of an alignment laser. Invisible wavelengths return black.
        /// </summary>
        public static Color WavelengthToColor(float wavelengthNm)
        {
            float w = wavelengthNm;
            float r, g, b;

            if (w >= 380f && w < 440f) { r = -(w - 440f) / 60f; g = 0f; b = 1f; }
            else if (w >= 440f && w < 490f) { r = 0f; g = (w - 440f) / 50f; b = 1f; }
            else if (w >= 490f && w < 510f) { r = 0f; g = 1f; b = -(w - 510f) / 20f; }
            else if (w >= 510f && w < 580f) { r = (w - 510f) / 70f; g = 1f; b = 0f; }
            else if (w >= 580f && w < 645f) { r = 1f; g = -(w - 645f) / 65f; b = 0f; }
            else if (w >= 645f && w <= 750f) { r = 1f; g = 0f; b = 0f; }
            else { return Color.black; }

            // Dim the edges of the visible range, where the eye is less sensitive.
            float factor = 1f;
            if (w < 420f) factor = 0.3f + 0.7f * (w - 380f) / 40f;
            else if (w > 700f) factor = 0.3f + 0.7f * (750f - w) / 50f;

            return new Color(r * factor, g * factor, b * factor, 1f);
        }

        /// <summary>
        /// Converts a mirror tilt in the (small) scene to the tilt that would give the same
        /// beam displacement in the real detector: theta_real = theta_scene * L_scene / L_real.
        /// </summary>
        public static float EquivalentRealAngleMicroradians(float sceneAngleDegrees, float sceneLeverArmMeters, float realLeverArmMeters)
        {
            if (realLeverArmMeters <= 0f)
            {
                return 0f;
            }

            float sceneRadians = sceneAngleDegrees * Mathf.Deg2Rad;
            return sceneRadians * (sceneLeverArmMeters / realLeverArmMeters) * 1e6f;
        }

        /// <summary>
        /// Radii of rings that each carry the same share of a unit Gaussian beam (w = 1).
        /// Power inside radius r is 1 - exp(-2 r^2), so the ring at power level u has r = sqrt(-ln(1 - u) / 2).
        /// </summary>
        private static float[] BuildEqualPowerRingRadii()
        {
            var radii = new float[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                float u = (i + 0.5f) / RingCount;
                radii[i] = Mathf.Sqrt(-Mathf.Log(1f - u) * 0.5f);
            }

            return radii;
        }
    }
}
