using UnityEngine;
using UnityEngine.EventSystems;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// One button of the <see cref="LaserControlPad"/>. Works with anything that sends UI pointer
    /// events: XR controller rays (XR UI Input Module), the XR Device Simulator and the mouse.
    /// Tap = one step. Holding a direction button for a moment keeps the mirror moving.
    /// Put it next to a UI Button (for the hover/press colours) on a world-space canvas.
    /// </summary>
    public class LaserPadButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private LaserPadAction action = LaserPadAction.YawLeft;

        [Tooltip("Empty = the LaserControlPad on a parent object.")]
        [SerializeField] private LaserControlPad pad = null;

        [Tooltip("How long a direction button must be held before the mirror keeps moving (seconds).")]
        [SerializeField, Min(0f)] private float holdDelaySeconds = 0.35f;

        private bool m_Pressed;
        private bool m_Holding;
        private float m_PressTime;

        public LaserPadAction Action => action;
        public bool IsPressed => m_Pressed;

        private LaserControlPad Pad
        {
            get
            {
                if (pad == null)
                {
                    pad = GetComponentInParent<LaserControlPad>();
                }

                return pad;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            Press();
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        public void OnPointerExit(PointerEventData eventData) => Release();

        /// <summary>Press the button (also usable from other input, e.g. an XR interactable's Select Entered).</summary>
        public void Press()
        {
            if (m_Pressed || Pad == null)
            {
                return;
            }

            m_Pressed = true;
            m_Holding = false;
            m_PressTime = Time.unscaledTime;
            LaserUiFeedback.NotifyPressed();
            Pad.Execute(action);
        }

        /// <summary>Release the button.</summary>
        public void Release()
        {
            if (m_Holding && Pad != null)
            {
                Pad.EndHold();
            }

            m_Pressed = false;
            m_Holding = false;
        }

        private void Update()
        {
            if (!m_Pressed || m_Holding || LaserControlPad.DirectionOf(action) == Vector2.zero)
            {
                return;
            }

            if (Time.unscaledTime - m_PressTime >= holdDelaySeconds)
            {
                m_Holding = true;
                Pad.BeginHold(action);
            }
        }

        private void OnDisable()
        {
            Release();
        }
    }
}
