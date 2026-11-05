using UnityEngine;
using UnityEngine.EventSystems;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// A button on the <see cref="LaserBriefingPanel"/> (close, page tabs, back, next/start).
    /// NextPage on the last page reads START and closes the briefing.
    /// Clicked with XR controller rays (XR UI Input Module), the XR Device Simulator or the mouse.
    /// </summary>
    public class LaserBriefingButton : MonoBehaviour, IPointerClickHandler
    {
        public enum BriefingAction
        {
            Close,
            NextPage,
            PreviousPage,
            ShowObjective,
            ShowControls,
            ShowPhysics   // old third tab: the physics page is gone, so it opens the last page
        }

        [SerializeField] private BriefingAction action = BriefingAction.Close;

        [Tooltip("Empty = the LaserBriefingPanel on a parent object.")]
        [SerializeField] private LaserBriefingPanel panel = null;

        public BriefingAction Action => action;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            Click();
        }

        /// <summary>Do the button's action (also usable from other input).</summary>
        public void Click()
        {
            if (panel == null)
            {
                panel = GetComponentInParent<LaserBriefingPanel>();
            }

            if (panel == null)
            {
                return;
            }

            LaserUiFeedback.NotifyPressed();
            switch (action)
            {
                case BriefingAction.Close: panel.Close(); break;
                case BriefingAction.NextPage: panel.NextPage(); break;
                case BriefingAction.PreviousPage: panel.PreviousPage(); break;
                case BriefingAction.ShowObjective: panel.ShowPage(0); break;
                case BriefingAction.ShowControls: panel.ShowPage(1); break;
                case BriefingAction.ShowPhysics: panel.ShowPage(LaserBriefingPanel.PageCount - 1); break;
            }
        }
    }
}
