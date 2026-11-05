using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>Everything the results screens show about one finished level.</summary>
    public struct LaserLevelResult
    {
        public int LevelIndex;
        public int LevelCount;
        public string Title;
        public float Seconds;
        public int Stars;
        public int Incidents;
        public int StrayLight;
        public int Clipping;
        public int ActuatorLimits;
        public int BackReflection;
        public bool HadPreviousBest;
        public float PreviousBestSeconds;
        public int BestStars;
        public bool IsNewBest;
        public bool IsLastLevel;
        public int Completions;

        /// <summary>3-star time limit of this level (seconds); 2 stars: twice this.</summary>
        public float ParTimeSeconds;
        public int ThreeStarMistakeLimit;
        public int TwoStarMistakeLimit;

        /// <summary>The star rules in one sentence (LaserAlignmentObjective.DescribeStarRules).</summary>
        public string StarRules;

        /// <summary>Title of the level that NEXT LEVEL loads (empty after the last level).</summary>
        public string NextTitle;

        /// <summary>Best stars summed over all levels, and the maximum (3 per level).</summary>
        public int TotalBestStars;
        public int MaxTotalStars;
    }

    /// <summary>
    /// Floating card shown when a level is completed: time, stars, safety incidents and personal best
    /// (saved between sessions, see <see cref="LaserBestTimes"/>). Buttons: RETRY, CLOSE and NEXT LEVEL
    /// (PLAY AGAIN after the last level).
    /// Since the levels moved into the hall towers this card floats in the middle of the hall, out of the player's
    /// view. The level manager therefore only shows it when nothing else shows the results; in Room_Scene the
    /// curved monitor does (<see cref="LaserMonitorResults"/>).
    /// </summary>
    public class LaserResultsCard : MonoBehaviour
    {
        [SerializeField] private GameObject screen = null;
        [SerializeField] private Text titleText = null;
        [SerializeField] private Text subtitleText = null;
        [SerializeField] private Text timeText = null;
        [SerializeField] private Text incidentsText = null;
        [SerializeField] private Text bestText = null;
        [SerializeField] private Text bannerText = null;
        [SerializeField] private Text nextButtonText = null;

        [Tooltip("Three star models, left to right.")]
        [SerializeField] private Renderer[] stars = new Renderer[0];
        [SerializeField] private Color starOnColor = new Color(1f, 0.78f, 0.15f, 1f);
        [SerializeField] private Color starOffColor = new Color(0.12f, 0.14f, 0.18f, 1f);

        [Header("Events")]
        [SerializeField] private UnityEvent onRetry = new UnityEvent();
        [SerializeField] private UnityEvent onNext = new UnityEvent();
        [SerializeField] private UnityEvent onClosed = new UnityEvent();

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock m_Block;

        public bool IsOpen => screen != null && screen.activeSelf;
        public LaserLevelResult LastResult { get; private set; }
        public UnityEvent OnRetry => onRetry;
        public UnityEvent OnNext => onNext;
        public UnityEvent OnClosed => onClosed;

        /// <summary>Text lines currently on the card, for tests and the AI tutor.</summary>
        public string Summary => (titleText != null ? titleText.text : "") + " | " + (timeText != null ? timeText.text : "") + " | " +
                                 (incidentsText != null ? incidentsText.text : "") + " | " + (bestText != null ? bestText.text : "") + " | " +
                                 (bannerText != null ? bannerText.text : "");

        public void Show(LaserLevelResult result)
        {
            LastResult = result;
            if (screen != null)
            {
                screen.SetActive(true);
            }

            Set(titleText, result.IsLastLevel ? "ALL LEVELS COMPLETE" : "LEVEL " + (result.LevelIndex + 1) + " COMPLETE");
            Set(subtitleText, "Level " + (result.LevelIndex + 1) + "/" + result.LevelCount + "  |  " + result.Title);
            Set(timeText, LaserBestTimes.FormatTime(result.Seconds));
            Set(incidentsText, result.Incidents + "   " + DescribeIncidents(result));
            if (result.HadPreviousBest && result.IsNewBest)
            {
                Set(bestText, LaserBestTimes.FormatTime(result.Seconds) + "   (was " + LaserBestTimes.FormatTime(result.PreviousBestSeconds) + ")");
            }
            else if (result.HadPreviousBest)
            {
                Set(bestText, LaserBestTimes.FormatTime(result.PreviousBestSeconds) + "   (" + result.Completions + " runs, most stars " + result.BestStars + ")");
            }
            else
            {
                Set(bestText, LaserBestTimes.FormatTime(result.Seconds) + "   (first completion)");
            }

            Set(bannerText, result.IsNewBest ? (result.HadPreviousBest ? "NEW PERSONAL BEST!" : "FIRST COMPLETION: THIS IS YOUR TIME TO BEAT") :
                result.Stars < 3 ? StarTip(result) : "Great run. Can you beat " + LaserBestTimes.FormatTime(result.PreviousBestSeconds) + "?");
            Set(nextButtonText, result.IsLastLevel ? "PLAY AGAIN  ►" : "NEXT LEVEL  ►");

            for (int i = 0; i < stars.Length; i++)
            {
                Tint(stars[i], i < result.Stars ? starOnColor : starOffColor);
            }
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

        /// <summary>Hide without raising events (a new level starts).</summary>
        public void Hide()
        {
            if (screen != null)
            {
                screen.SetActive(false);
            }
        }

        public void Retry()
        {
            Hide();
            onRetry.Invoke();
        }

        public void Next()
        {
            Hide();
            onNext.Invoke();
        }

        /// <summary>"(clean run)" or e.g. "(stray light 1, clipping 2)".</summary>
        public static string DescribeIncidents(LaserLevelResult result)
        {
            if (result.Incidents == 0)
            {
                return "(clean run)";
            }

            var parts = new System.Collections.Generic.List<string>(4);
            if (result.StrayLight > 0) parts.Add("stray light " + result.StrayLight);
            if (result.Clipping > 0) parts.Add("clipping " + result.Clipping);
            if (result.BackReflection > 0) parts.Add("back reflection " + result.BackReflection);
            if (result.ActuatorLimits > 0) parts.Add("actuator limit " + result.ActuatorLimits);
            return "(" + string.Join(", ", parts) + ")";
        }

        /// <summary>What to do better for 3 stars (only meaningful when the run earned fewer than 3).</summary>
        public static string StarTip(LaserLevelResult result)
        {
            int limit = result.ThreeStarMistakeLimit > 0 || result.ParTimeSeconds > 0f ? result.ThreeStarMistakeLimit : 1;
            if (result.Incidents > limit)
            {
                return "3 stars: at most " + limit + (limit == 1 ? " safety incident" : " safety incidents") +
                       ". Move in smaller steps near optics and walls.";
            }

            float par = result.ParTimeSeconds > 0f ? result.ParTimeSeconds : 120f;
            return "3 stars: finish within " + LaserBestTimes.FormatTime(par) +
                   ". Use COARSE steps first, FINE only for the last centimetre.";
        }

        private void Tint(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            m_Block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(m_Block);
            m_Block.SetColor(s_BaseColorId, color);
            m_Block.SetColor(s_ColorId, color);
            m_Block.SetColor(s_EmissionColorId, color * 1.5f);
            renderer.SetPropertyBlock(m_Block);
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
