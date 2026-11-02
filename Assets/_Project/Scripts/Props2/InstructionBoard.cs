using EinsteinTelescope.Laser;
using UnityEngine;

namespace EinsteinTelescope.Props
{
    /// <summary>
    /// The wall-mounted instruction board in the control room. It carries Carlo's
    /// <see cref="LaserBriefingPanel"/> (moved onto the screen by Tools > Einstein Telescope > Instructiebord):
    /// while the briefing is open the screen turns dark behind it, while it is closed the board shows a
    /// standby picture and a full-screen invisible button that opens the briefing again.
    /// The level manager keeps opening and closing the briefing exactly as before.
    /// </summary>
    public class InstructionBoard : MonoBehaviour
    {
        [SerializeField] private LaserBriefingPanel briefing = null;

        [Tooltip("Renderer of InstructionBoard_Screen.")]
        [SerializeField] private Renderer screenRenderer = null;

        [Tooltip("Shown while the briefing is closed (ET2_BoardScreen).")]
        [SerializeField] private Material standbyMaterial = null;

        [Tooltip("Dark background behind the open briefing (ET2_BoardScreen_Active).")]
        [SerializeField] private Material activeMaterial = null;

        [Tooltip("Canvas with the invisible 'open briefing' button; hidden while the briefing is open.")]
        [SerializeField] private GameObject standbyCanvas = null;

        private bool m_HasState;
        private bool m_WasOpen;

        public LaserBriefingPanel Briefing => briefing;

        /// <summary>Called by the standby button: open the briefing on the page the player saw last.</summary>
        public void OpenBriefing()
        {
            if (briefing != null && !briefing.IsOpen)
            {
                LaserUiFeedback.NotifyPressed();
                briefing.Open(briefing.CurrentPage);
            }
        }

        private void Update()
        {
            bool open = briefing != null && briefing.IsOpen;
            if (m_HasState && open == m_WasOpen)
            {
                return;
            }

            m_HasState = true;
            m_WasOpen = open;
            if (screenRenderer != null)
            {
                Material m = open ? activeMaterial : standbyMaterial;
                if (m != null)
                {
                    screenRenderer.sharedMaterial = m;
                }
            }

            if (standbyCanvas != null)
            {
                standbyCanvas.SetActive(!open);
            }
        }
    }
}
