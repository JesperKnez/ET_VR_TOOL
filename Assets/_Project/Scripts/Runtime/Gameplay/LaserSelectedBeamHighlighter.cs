using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Draws the light coming off the selected mirror (and everything downstream of it) in a different colour,
    /// so the player sees which part of the beam a button press will move.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.MirrorMount - 10)]
    public class LaserSelectedBeamHighlighter : MonoBehaviour
    {
        [Tooltip("Empty = the first MirrorControlPanel in the scene.")]
        [SerializeField] private MirrorControlPanel controlPanel = null;

        [SerializeField] private bool highlightSelectedBeam = true;
        [SerializeField] private Color highlightColor = new Color(0.25f, 0.8f, 1f, 1f);

        public bool IsOn => highlightSelectedBeam;

        public void SetHighlight(bool on) => highlightSelectedBeam = on;
        public void ToggleHighlight() => highlightSelectedBeam = !highlightSelectedBeam;

        private void Update()
        {
            LaserBeamHighlight.Color = highlightColor;
            MirrorControlPanel panel = controlPanel != null ? controlPanel : MirrorControlPanel.FirstActive;
            MirrorMount mirror = highlightSelectedBeam && panel != null ? panel.SelectedMirror : null;
            LaserBeamHighlight.SelectedOptic = mirror != null ? mirror.GetComponentInChildren<Collider>() : null;
        }

        private void OnDisable()
        {
            LaserBeamHighlight.SelectedOptic = null;
        }
    }
}
