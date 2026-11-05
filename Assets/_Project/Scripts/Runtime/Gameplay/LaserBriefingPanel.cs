using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Big floating briefing screen shown at the start of every level. Three pages:
    /// the objective, the controls and the physics (the maths kept simple). The player closes it with
    /// the X button (XR controller ray or mouse); HELP on the control pad or H opens it again.
    /// The last page has no NEXT button.
    /// The texts come from the level (<see cref="LaserLevel"/>) and can be edited in the inspector.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic - 10)]
    public class LaserBriefingPanel : MonoBehaviour
    {
        public const int PageCount = 3;
        private static readonly string[] s_PageNames = { "OBJECTIVE", "CONTROLS", "THE PHYSICS" };

        [Tooltip("The object that is shown and hidden (the canvas with the screen).")]
        [SerializeField] private GameObject screen = null;

        [Header("Texts")]
        [SerializeField] private Text titleText = null;
        [SerializeField] private Text bodyText = null;
        [SerializeField] private Text pageText = null;
        [SerializeField] private Text nextButtonText = null;

        [Tooltip("Tab backgrounds (objective, controls, physics); the open page is highlighted.")]
        [SerializeField] private Image[] tabImages = new Image[0];
        [SerializeField] private Color tabColor = new Color(0.1f, 0.16f, 0.23f, 1f);
        [SerializeField] private Color activeTabColor = new Color(0.16f, 0.42f, 0.62f, 1f);

        [Tooltip("Controls page, the same for every level. The level adds its own tip below it.")]
        [SerializeField, TextArea(6, 20)] private string controlsText = "";

        [Header("Events")]
        [SerializeField] private UnityEvent onOpened = new UnityEvent();
        [Tooltip("The player closed the screen (X).")]
        [SerializeField] private UnityEvent onClosed = new UnityEvent();

        private LaserLevel m_Level;
        private string m_Title = "LASER ALIGNMENT";
        private int m_Page;

        public bool IsOpen => screen != null && screen.activeSelf;
        public int CurrentPage => m_Page;
        public string CurrentTitle => m_Title;
        public string CurrentBody => BuildPage(m_Page);
        public UnityEvent OnOpened => onOpened;
        public UnityEvent OnClosed => onClosed;

        /// <summary>Open the briefing of a level on its first page.</summary>
        public void Show(LaserLevel level, int index, int count)
        {
            m_Level = level;
            m_Title = level != null ? "LEVEL " + (index + 1) + "/" + count + "  |  " + level.Title.ToUpperInvariant() : "LASER ALIGNMENT";
            Open(0);
        }

        /// <summary>Open the briefing again (HELP button) on the given page.</summary>
        public void Open(int page)
        {
            if (screen != null && !screen.activeSelf)
            {
                screen.SetActive(true);
            }

            ShowPage(page);
            onOpened.Invoke();
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            screen.SetActive(false);
            onClosed.Invoke();
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open(m_Page);
            }
        }

        public void ShowPage(int page)
        {
            m_Page = Mathf.Clamp(page, 0, PageCount - 1);
            Set(titleText, m_Title);
            Set(bodyText, BuildPage(m_Page));
            Set(pageText, (m_Page + 1) + " / " + PageCount + "   " + s_PageNames[m_Page]);
            Set(nextButtonText, "NEXT  ►");
            if (nextButtonText != null && nextButtonText.transform.parent != null)
            {
                nextButtonText.transform.parent.gameObject.SetActive(m_Page < PageCount - 1);   // the button holds the label
            }
            for (int i = 0; i < tabImages.Length; i++)
            {
                if (tabImages[i] != null)
                {
                    tabImages[i].color = i == m_Page ? activeTabColor : tabColor;
                }
            }
        }

        /// <summary>Next page; does nothing on the last page (close with X).</summary>
        public void NextPage()
        {
            if (m_Page < PageCount - 1)
            {
                ShowPage(m_Page + 1);
            }
        }

        public void PreviousPage() => ShowPage(m_Page - 1);

        private string BuildPage(int page)
        {
            switch (page)
            {
                case 0:
                    return m_Level != null ? m_Level.ObjectiveText : string.Empty;
                case 1:
                    string tip = m_Level != null ? m_Level.ControlsTip : string.Empty;
                    return string.IsNullOrEmpty(tip) ? controlsText : controlsText + "\n\n<b>This level:</b> " + tip;
                default:
                    return m_Level != null ? m_Level.PhysicsText : string.Empty;
            }
        }

        private static void Set(Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }
    }
}
