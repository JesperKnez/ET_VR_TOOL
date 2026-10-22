using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Which optic's outgoing light is drawn in the highlight colour. All light that leaves this optic, and
    /// everything it hits after that, is highlighted: exactly the part of the beam the player is steering.
    /// A visual aid only; the physics do not change. Set by <see cref="LaserSelectedBeamHighlighter"/>.
    /// </summary>
    public static class LaserBeamHighlight
    {
        /// <summary>The optic collider whose outgoing beam is highlighted (null = none).</summary>
        public static Collider SelectedOptic { get; set; }

        /// <summary>Highlight colour (cyan, like the selection ring).</summary>
        public static Color Color { get; set; } = new Color(0.25f, 0.8f, 1f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            SelectedOptic = null;
            Color = new Color(0.25f, 0.8f, 1f, 1f);
        }
    }
}
