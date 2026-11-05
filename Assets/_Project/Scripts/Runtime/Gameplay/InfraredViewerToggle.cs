using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Retired: the players always use the visible green 532 nm alignment laser, so there is no
    /// "IR viewer" any more (the IR VIEWER button on the control pad was removed).
    /// This component is kept because it is still on the Laser Game Logic object in the scene; it now only
    /// makes sure the viewer stays off. With the viewer off, infrared beams would not be drawn (they would
    /// still work: detectors still receive them). Turning it on is ignored.
    /// </summary>
    public class InfraredViewerToggle : MonoBehaviour
    {
        [Tooltip("Invoked with false when the viewer is switched off. Use it for a sound, an overlay or a tutor remark.")]
        [SerializeField] private UnityEvent<bool> onViewerChanged = new UnityEvent<bool>();

        /// <summary>Always false in practice: the viewer is retired.</summary>
        public bool IsActive => LaserVisibility.InfraredViewerActive;

        /// <summary>Retired: keeps the viewer off.</summary>
        public void Toggle() => SetViewerActive(false);

        /// <summary>Retired: the viewer can only be switched off.</summary>
        public void SetViewerActive(bool active)
        {
            LaserVisibility.SetInfraredViewer(false);
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
            SetViewerActive(false);
        }

        private void HandleViewerChanged(bool active)
        {
            onViewerChanged.Invoke(active);
        }
    }
}
