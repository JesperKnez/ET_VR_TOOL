using UnityEngine;
using UnityEngine.EventSystems;

namespace EinsteinTelescope.Laser
{
    /// <summary>A button on the <see cref="LaserResultsCard"/>: RETRY, CLOSE or NEXT LEVEL.</summary>
    public class LaserResultsButton : MonoBehaviour, IPointerClickHandler
    {
        public enum ResultsAction
        {
            Retry,
            Close,
            Next
        }

        [SerializeField] private ResultsAction action = ResultsAction.Next;
        [SerializeField] private LaserResultsCard card = null;

        public ResultsAction Action => action;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            Click();
        }

        public void Click()
        {
            if (card == null)
            {
                card = GetComponentInParent<LaserResultsCard>();
            }

            if (card == null)
            {
                return;
            }

            LaserUiFeedback.NotifyPressed();
            switch (action)
            {
                case ResultsAction.Retry: card.Retry(); break;
                case ResultsAction.Close: card.Close(); break;
                case ResultsAction.Next: card.Next(); break;
            }
        }
    }
}
