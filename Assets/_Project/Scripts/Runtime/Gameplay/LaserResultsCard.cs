using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>Everything the results card shows about one finished level.</summary>
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
    }

    /// <summary>
    /// Floating card shown when a level is completed: time, stars, safety incidents and personal best
    /// (saved between sessions, see <see cref="LaserBestTimes"/>). Buttons: RETRY, CLOSE and NEXT LEVEL
    /// (PLAY AGAIN after the last level).
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

        [Tooltip("Three star models, left to right (floating card).")]
        [SerializeField] private Renderer[] stars = new Renderer[0];

        [Tooltip("Three star images, left to right (card on the curved monitor). A star sprite is made at runtime when the image has none.")]
        [SerializeField] private Image[] starImages = new Image[0];

        [Tooltip("Objects that are hidden while the card is open (the other panels on the monitor), so the card replaces the screen.")]
        [SerializeField] private GameObject[] hideWhileOpen = new GameObject[0];

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
        private readonly System.Collections.Generic.List<GameObject> m_Hidden = new System.Collections.Generic.List<GameObject>();
        private static Sprite s_StarSprite;

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
            HideOthers();
            if (screen != null)
            {
                screen.SetActive(true);
            }

            Set(titleText, result.IsLastLevel ? "ALL LEVELS COMPLETE" : "LEVEL " + (result.LevelIndex + 1) + " COMPLETE");
            Set(subtitleText, "Level " + (result.LevelIndex + 1) + "/" + result.LevelCount + "  |  " + result.Title);
            Set(timeText, LaserBestTimes.FormatTime(result.Seconds));
            Set(incidentsText, result.Incidents + "   " + IncidentBreakdown(result));
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

            for (int i = 0; i < starImages.Length; i++)
            {
                if (starImages[i] == null)
                {
                    continue;
                }

                if (starImages[i].sprite == null)
                {
                    starImages[i].sprite = StarSprite();
                }

                starImages[i].color = i < result.Stars ? starOnColor : starOffColor;
            }
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            screen.SetActive(false);
            ShowOthers();
            onClosed.Invoke();
        }

        /// <summary>Hide without raising events (a new level starts).</summary>
        public void Hide()
        {
            if (screen != null)
            {
                screen.SetActive(false);
            }

            ShowOthers();
        }

        private void HideOthers()
        {
            for (int i = 0; i < hideWhileOpen.Length; i++)
            {
                GameObject other = hideWhileOpen[i];
                if (other != null && other != screen && other.activeSelf)
                {
                    other.SetActive(false);
                    m_Hidden.Add(other);
                }
            }
        }

        private void ShowOthers()
        {
            for (int i = 0; i < m_Hidden.Count; i++)
            {
                if (m_Hidden[i] != null)
                {
                    m_Hidden[i].SetActive(true);
                }
            }

            m_Hidden.Clear();
        }

        /// <summary>A white five-pointed star, drawn once into a small texture (the built-in font has no star glyph).</summary>
        private static Sprite StarSprite()
        {
            if (s_StarSprite != null)
            {
                return s_StarSprite;
            }

            const int size = 128;
            var polygon = new Vector2[10];
            for (int i = 0; i < polygon.Length; i++)
            {
                float radius = i % 2 == 0 ? 0.48f : 0.20f;
                float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                polygon[i] = new Vector2(0.5f + Mathf.Cos(angle) * radius, 0.5f + Mathf.Sin(angle) * radius - 0.02f);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Star (runtime)", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            const int samples = 3;   // 3x3 samples per pixel: smooth edge
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            var p = new Vector2((x + (sx + 0.5f) / samples) / size, (y + (sy + 0.5f) / samples) / size);
                            if (InsidePolygon(polygon, p)) inside++;
                        }
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (samples * samples)));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            s_StarSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s_StarSprite.name = "Star (runtime)";
            return s_StarSprite;
        }

        private static bool InsidePolygon(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
            }

            return inside;
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

        private static string IncidentBreakdown(LaserLevelResult result)
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

        private static string StarTip(LaserLevelResult result)
        {
            if (result.Incidents > 1)
            {
                return "3 stars: at most 1 safety incident. Move in smaller steps near optics and walls.";
            }

            return "3 stars: finish within 2 minutes. Use COARSE steps first, FINE only for the last centimetre.";
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
