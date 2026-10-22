using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Switches the "IR viewer" on and off. The main lasers of LIGO, Virgo and the Einstein
    /// Telescope are infrared (1064 nm / 1550 nm) and invisible to the eye; technicians use
    /// IR viewers and IR cards to see them. With this off, infrared beams are not drawn
    /// (they still work: detectors still receive them).
    /// </summary>
    public class InfraredViewerToggle : MonoBehaviour
    {
        [SerializeField] private bool activeOnStart = false;

        [Tooltip("Viewer switched on (true) or off (false). Use it for a sound, an overlay or a tutor remark.")]
        [SerializeField] private UnityEvent<bool> onViewerChanged = new UnityEvent<bool>();

        public bool IsActive => LaserVisibility.InfraredViewerActive;

        public void Toggle() => SetViewerActive(!LaserVisibility.InfraredViewerActive);

        public void SetViewerActive(bool active)
        {
            LaserVisibility.SetInfraredViewer(active);
        }

        private void OnEnable()
        {
            LaserVisibility.InfraredViewerChanged += HandleViewerChanged;
        }

        private void OnDisable()
        {
            LaserVisibility.InfraredViewerChanged -= HandleViewerChanged;
        }

        private void Start()
        {
            SetViewerActive(activeOnStart);
        }

        private void HandleViewerChanged(bool active)
        {
            onViewerChanged.Invoke(active);
        }
    }
}
