using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Level briefing on the instruction board TV, shown at the start of every level. Two pages: the objective
    /// (with the star rules) and the controls (buttons, mirror camera, other monitor screens). The player closes it
    /// with the X button or START on the last page (XR controller ray or mouse); HELP on the control pad opens it again.
    /// The texts come from <see cref="LaserBriefingTexts"/> (one source of truth, no scene editing needed). Turn off
    /// "Use Built In Texts" to use the texts in the <see cref="LaserLevel"/> inspector instead.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic - 10)]
    public class LaserBriefingPanel : MonoBehaviour
    {
        public const int PageCount = 2;
        private static readonly string[] s_PageNames = { "OBJECTIVE", "CONTROLS" };

        [Tooltip("The object that is shown and hidden (the canvas with the screen).")]
        [SerializeField] private GameObject screen = null;

        [Header("Texts")]
        [SerializeField] private Text titleText = null;
        [SerializeField] private Text bodyText = null;
        [SerializeField] private Text pageText = null;
        [SerializeField] private Text nextButtonText = null;

        [Tooltip("Tab backgrounds (objective, controls); the open page is highlighted. Extra tabs (the old physics tab) are hidden at runtime.")]
        [SerializeField] private Image[] tabImages = new Image[0];
        [SerializeField] private Color tabColor = new Color(0.1f, 0.16f, 0.23f, 1f);
        [SerializeField] private Color activeTabColor = new Color(0.16f, 0.42f, 0.62f, 1f);

        [Tooltip("On: texts from LaserBriefingTexts (matched to the level by title). Off: the texts in the LaserLevel inspector and Controls Text below.")]
        [SerializeField] private bool useBuiltInTexts = true;

        [Tooltip("Controls page when Use Built In Texts is off, the same for every level. The level adds its own tip below it.")]
        [SerializeField, TextArea(6, 20)] private string controlsText = "";

        [Tooltip("For the stars section on the objective page. Empty = the first LaserAlignmentObjective in the scene.")]
        [SerializeField] private LaserAlignmentObjective objective = null;

        [Tooltip("The body text shrinks down to this font size when a page does not fit.")]
        [SerializeField, Range(10, 40)] private int minBodyFontSize = 19;

        [Header("Events")]
        [SerializeField] private UnityEvent onOpened = new UnityEvent();
        [Tooltip("The player closed the screen (X or START).")]
        [SerializeField] private UnityEvent onClosed = new UnityEvent();

        private LaserLevel m_Level;
        private int m_LevelIndex = -1;
        private string m_Title = "LASER ALIGNMENT";
        private int m_Page;
        private int m_BodyFontSize = -1;

        public bool IsOpen => screen != null && screen.activeSelf;
        public int CurrentPage => m_Page;
        public string CurrentTitle => m_Title;
        public string CurrentBody => BuildPage(m_Page);
        public UnityEvent OnOpened => onOpened;
        public UnityEvent OnClosed => onClosed;

        private void Awake()
        {
            ApplyTabLayout();
        }

        /// <summary>Open the briefing of a level on its first page.</summary>
        public void Show(LaserLevel level, int index, int count)
        {
            m_Level = level;
            m_LevelIndex = index;
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
            bool last = m_Page == PageCount - 1;
            Set(titleText, m_Title);
            Set(bodyText, BuildPage(m_Page));
            FitBody();
            Set(pageText, (m_Page + 1) + " / " + PageCount + "   " + s_PageNames[m_Page]);
            Set(nextButtonText, last ? "START  ►" : "NEXT  ►");
            if (nextButtonText != null && nextButtonText.transform.parent != null)
            {
                nextButtonText.transform.parent.gameObject.SetActive(true);   // the button holds the label
            }

            for (int i = 0; i < tabImages.Length; i++)
            {
                if (tabImages[i] != null)
                {
                    tabImages[i].color = i == m_Page ? activeTabColor : tabColor;
                }
            }
        }

        /// <summary>Next page; on the last page the button reads START and closes the briefing.</summary>
        public void NextPage()
        {
            if (m_Page < PageCount - 1)
            {
                ShowPage(m_Page + 1);
            }
            else
            {
                Close();
            }
        }

        public void PreviousPage() => ShowPage(m_Page - 1);

        /// <summary>
        /// Hides tabs beyond <see cref="PageCount"/> (the old physics tab) and centres the remaining tabs, keeping the
        /// distance between them. Assumes the tab row is centred in its parent, as the demo builder makes it.
        /// Safe to run more than once. Also called by the instruction board editor menu.
        /// </summary>
        public void ApplyTabLayout()
        {
            if (tabImages == null)
            {
                return;
            }

            float spacing = 0f;
            if (tabImages.Length >= 2 && tabImages[0] != null && tabImages[1] != null)
            {
                spacing = Mathf.Abs(tabImages[1].rectTransform.anchoredPosition.x - tabImages[0].rectTransform.anchoredPosition.x);
            }

            int shown = Mathf.Min(PageCount, tabImages.Length);
            for (int i = 0; i < tabImages.Length; i++)
            {
                if (tabImages[i] == null)
                {
                    continue;
                }

                bool used = i < PageCount;
                if (tabImages[i].gameObject.activeSelf != used)
                {
                    tabImages[i].gameObject.SetActive(used);
                }

                if (used && spacing > 1f)
                {
                    RectTransform rect = tabImages[i].rectTransform;
                    rect.anchoredPosition = new Vector2((i - (shown - 1) * 0.5f) * spacing, rect.anchoredPosition.y);
                }
            }
        }

        private string BuildPage(int page)
        {
            return page == 0 ? BuildObjectivePage() : BuildControlsPage();
        }

        private string BuildObjectivePage()
        {
            string text = useBuiltInTexts ? LaserBriefingTexts.Objective(m_Level, m_LevelIndex) : null;
            if (text == null)
            {
                text = m_Level != null ? m_Level.ObjectiveText : string.Empty;
            }

            string stars = BuildStarsSection();
            if (string.IsNullOrEmpty(stars))
            {
                return text;
            }

            return string.IsNullOrEmpty(text) ? stars : text + "\n\n" + stars;
        }

        private string BuildControlsPage()
        {
            string controls = useBuiltInTexts ? LaserBriefingTexts.Controls : controlsText;
            string tip = useBuiltInTexts ? LaserBriefingTexts.ControlsTip(m_Level, m_LevelIndex) : null;
            if (tip == null)
            {
                tip = m_Level != null ? m_Level.ControlsTip : string.Empty;
            }

            return string.IsNullOrEmpty(tip) ? controls : controls + "\n\n<b>This level:</b>  " + tip;
        }

        /// <summary>Star rules from the alignment objective, plus what a safety incident is. Empty without an objective.</summary>
        private string BuildStarsSection()
        {
            if (!useBuiltInTexts)
            {
                return string.Empty;   // the inspector texts carry their own stars section
            }

            if (!objective)
            {
                objective = FindFirstObjectByType<LaserAlignmentObjective>();
            }

            if (!objective)
            {
                return string.Empty;
            }

            string rules = objective.DescribeStarRules();
            if (string.IsNullOrEmpty(rules))
            {
                return string.Empty;
            }

            return LaserBriefingTexts.StarsHeader + "\n" + rules + "\n" + LaserBriefingTexts.SafetyLine;
        }

        /// <summary>Shrinks the body text one point at a time until the page fits its box (never below the minimum).</summary>
        private void FitBody()
        {
            if (bodyText == null)
            {
                return;
            }

            if (m_BodyFontSize < 0)
            {
                m_BodyFontSize = bodyText.fontSize;
            }

            bodyText.fontSize = m_BodyFontSize;
            float height = bodyText.rectTransform.rect.height;
            if (height <= 0f || !bodyText.gameObject.activeInHierarchy)
            {
                return;
            }

            while (bodyText.fontSize > minBodyFontSize && bodyText.preferredHeight > height)
            {
                bodyText.fontSize--;
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
