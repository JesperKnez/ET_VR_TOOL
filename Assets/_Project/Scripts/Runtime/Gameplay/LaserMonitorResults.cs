using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// "LEVEL COMPLETE" screen on the curved control-room monitor. Creates itself on Play: nothing has to be added
    /// to the scene. It looks for the monitor canvas ("MonitorUI", Screen Space - Camera, rendered to MonitorUI_RT)
    /// and the <see cref="LaserLevelManager"/>, and listens to <see cref="LaserLevelManager.LevelCompleted"/>.
    ///
    /// When a level is completed it covers the whole monitor with: level name, 1-3 stars, time, safety incidents,
    /// personal best, a tip for more stars and two buttons, RETRY and NEXT LEVEL (after the last level: PLAY AGAIN and
    /// RESTART FROM LEVEL 1). The buttons are normal UGUI buttons, clicked with the controller ray through
    /// CurvedScreenPointer. Loading any level (also with the NEXT LEVEL button on the control pad) hides it again.
    ///
    /// Without a "MonitorUI" canvas in the scene (e.g. a test scene) it does nothing, and the level manager falls back
    /// to its floating results card or loads the next level by itself.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic + 50)]
    public class LaserMonitorResults : MonoBehaviour
    {
        private const string CanvasName = "MonitorUI";
        private const string OverlayName = "Level Results (runtime)";
        private const float BindIntervalSeconds = 0.5f;
        private const float ShowDelaySeconds = 0.8f;       // let the player see the spots turn green first
        private const float TowardsCameraMeters = 0.35f;   // in front of the 3D parts of the HUD
        private const float StarFirstDelay = 0.25f;
        private const float StarInterval = 0.3f;
        private const float StarPopSeconds = 0.25f;

        private static readonly Color BackgroundColor = new Color(0.012f, 0.025f, 0.045f, 0.985f);
        private static readonly Color CardColor = new Color(0.045f, 0.08f, 0.125f, 1f);
        private static readonly Color AccentColor = new Color(0.3f, 1f, 0.45f, 1f);
        private static readonly Color GoldColor = new Color(1f, 0.8f, 0.2f, 1f);
        private static readonly Color StarOffColor = new Color(0.16f, 0.2f, 0.27f, 1f);
        private static readonly Color LabelColor = new Color(0.55f, 0.65f, 0.75f, 1f);
        private static readonly Color ValueColor = new Color(0.93f, 0.96f, 1f, 1f);
        private static readonly Color TipColor = new Color(0.55f, 0.85f, 1f, 1f);
        private static readonly Color RetryColor = new Color(0.18f, 0.27f, 0.38f, 1f);
        private static readonly Color NextColor = new Color(0.12f, 0.55f, 0.28f, 1f);

        private static LaserMonitorResults s_Instance;
        private static Sprite s_StarSprite;

        private LaserLevelManager m_Manager;
        private Canvas m_Canvas;
        private float m_NextBindTime;

        private RectTransform m_Root;
        private Font m_Font;
        private int m_Layer;
        private Text m_Title;
        private Text m_Subtitle;
        private readonly Image[] m_Stars = new Image[3];
        private Text m_TimeValue;
        private Text m_TimeDetail;
        private Text m_IncidentsValue;
        private Text m_IncidentsDetail;
        private Text m_BestValue;
        private Text m_BestDetail;
        private Text m_Banner;
        private Text m_Rules;
        private Text m_RetryLabel;
        private Text m_NextLabel;

        private readonly List<Renderer> m_HiddenRenderers = new List<Renderer>();
        private LaserLevelResult m_Result;
        private float m_ShowAt = -1f;
        private float m_ShownAt;
        private bool m_Visible;

        /// <summary>True while the results cover the monitor.</summary>
        public static bool IsShowing => s_Instance != null && s_Instance.m_Visible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (s_Instance != null)
            {
                return;
            }

            var go = new GameObject("Laser Monitor Results");
            DontDestroyOnLoad(go);
            go.AddComponent<LaserMonitorResults>();
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
        }

        private void OnDestroy()
        {
            Unbind();
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        private void Update()
        {
            if (!m_Manager || !m_Canvas)
            {
                TryBind();
            }

            if (m_ShowAt >= 0f && Time.unscaledTime >= m_ShowAt)
            {
                m_ShowAt = -1f;
                Show(m_Result);
            }

            if (m_Visible)
            {
                AnimateStars();
            }
        }

        // ----- Binding --------------------------------------------------------------------------------

        private void TryBind()
        {
            if (Time.unscaledTime < m_NextBindTime)
            {
                return;
            }

            m_NextBindTime = Time.unscaledTime + BindIntervalSeconds;
            Unbind();

            Canvas canvas = FindMonitorCanvas();
            LaserLevelManager manager = canvas != null ? FindAnyObjectByType<LaserLevelManager>() : null;
            if (canvas == null || manager == null)
            {
                return;
            }

            m_Canvas = canvas;
            m_Manager = manager;
            m_Manager.LevelCompleted += HandleLevelCompleted;
            m_Manager.OnLevelStarted.AddListener(HandleLevelStarted);
        }

        private void Unbind()
        {
            Hide();   // gives back any HUD renderers that are still switched off
            if (m_Manager)
            {
                m_Manager.LevelCompleted -= HandleLevelCompleted;
                m_Manager.OnLevelStarted.RemoveListener(HandleLevelStarted);
            }

            m_Manager = null;
            m_Canvas = null;
            m_Root = null;
            m_Visible = false;
            m_ShowAt = -1f;
            m_HiddenRenderers.Clear();
        }

        private static Canvas FindMonitorCanvas()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                if (canvas.name == CanvasName && canvas.transform.parent == null)
                {
                    return canvas;
                }
            }

            foreach (Canvas canvas in canvases)
            {
                if (canvas.name == CanvasName)
                {
                    return canvas;
                }
            }

            return null;
        }

        // ----- Events ---------------------------------------------------------------------------------

        private void HandleLevelCompleted(LaserLevelResult result)
        {
            m_Result = result;
            m_ShowAt = Time.unscaledTime + ShowDelaySeconds;
        }

        private void HandleLevelStarted(int index)
        {
            m_ShowAt = -1f;
            Hide();
        }

        private void Retry()
        {
            if (!m_Visible || !m_Manager)
            {
                return;
            }

            LaserUiFeedback.NotifyPressed();
            Hide();
            m_Manager.RestartLevel();
        }

        private void Next()
        {
            if (!m_Visible || !m_Manager)
            {
                return;
            }

            LaserUiFeedback.NotifyPressed();
            bool last = m_Result.IsLastLevel;
            Hide();
            if (last)
            {
                m_Manager.RestartFromFirstLevel();
            }
            else
            {
                m_Manager.NextLevel();
            }
        }

        // ----- Show / hide ----------------------------------------------------------------------------

        private void Show(LaserLevelResult result)
        {
            if (!m_Canvas || !EnsureBuilt())
            {
                return;
            }

            Fill(result);
            m_Root.SetAsLastSibling();
            PlaceInFrontOfHud();
            m_Root.gameObject.SetActive(true);
            HideHudRenderers();
            m_Visible = true;
            m_ShownAt = Time.unscaledTime;
            AnimateStars();
        }

        private void Hide()
        {
            if (m_Root)
            {
                m_Root.gameObject.SetActive(false);
            }

            foreach (Renderer hidden in m_HiddenRenderers)
            {
                if (hidden)
                {
                    hidden.enabled = true;
                }
            }

            m_HiddenRenderers.Clear();
            m_Visible = false;
        }

        /// <summary>
        /// The HUD has 3D parts (dartboard, quadrant bars, camera views) that the UI camera draws as meshes; they would
        /// shine through the overlay, so they are switched off while it is shown.
        /// </summary>
        private void HideHudRenderers()
        {
            m_HiddenRenderers.Clear();
            foreach (Renderer hudRenderer in m_Canvas.GetComponentsInChildren<Renderer>(true))
            {
                if (hudRenderer.enabled && !hudRenderer.transform.IsChildOf(m_Root))
                {
                    hudRenderer.enabled = false;
                    m_HiddenRenderers.Add(hudRenderer);
                }
            }
        }

        /// <summary>Moves the overlay a little towards the UI camera, so it is drawn over everything else on the canvas.</summary>
        private void PlaceInFrontOfHud()
        {
            float scale = m_Canvas.transform.lossyScale.z;
            Camera uiCamera = m_Canvas.worldCamera;
            float room = uiCamera != null ? (m_Canvas.planeDistance - uiCamera.nearClipPlane) * 0.5f : TowardsCameraMeters;
            float meters = Mathf.Clamp(TowardsCameraMeters, 0f, Mathf.Max(0f, room));
            m_Root.anchoredPosition3D = new Vector3(0f, 0f, scale > 1e-6f ? -meters / scale : 0f);
        }

        private void AnimateStars()
        {
            float age = Time.unscaledTime - m_ShownAt;
            for (int i = 0; i < m_Stars.Length; i++)
            {
                if (m_Stars[i] == null)
                {
                    continue;
                }

                float t = Mathf.Clamp01((age - StarFirstDelay - i * StarInterval) / StarPopSeconds);
                float scale = t <= 0f ? 0f : t >= 1f ? 1f : EaseOutBack(t);
                m_Stars[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ----- Content --------------------------------------------------------------------------------

        private void Fill(LaserLevelResult r)
        {
            int number = r.LevelIndex + 1;
            m_Title.text = r.IsLastLevel ? "ALL LEVELS COMPLETE!" : "LEVEL " + number + " COMPLETE!";
            m_Subtitle.text = "Level " + number + "/" + r.LevelCount + "  |  " + r.Title;

            for (int i = 0; i < m_Stars.Length; i++)
            {
                m_Stars[i].color = i < r.Stars ? GoldColor : StarOffColor;
            }

            m_TimeValue.text = LaserBestTimes.FormatTime(r.Seconds);
            m_TimeDetail.text = r.ParTimeSeconds > 0f ? "3 stars within " + LaserBestTimes.FormatTime(r.ParTimeSeconds) : string.Empty;
            m_TimeDetail.color = r.ParTimeSeconds > 0f && r.Seconds <= r.ParTimeSeconds ? AccentColor : LabelColor;

            m_IncidentsValue.text = r.Incidents.ToString();
            m_IncidentsValue.color = r.Incidents <= r.ThreeStarMistakeLimit ? ValueColor : GoldColor;
            m_IncidentsDetail.text = LaserResultsCard.DescribeIncidents(r);

            if (r.HadPreviousBest && r.IsNewBest)
            {
                m_BestValue.text = LaserBestTimes.FormatTime(r.Seconds);
                m_BestDetail.text = "NEW PERSONAL BEST! (was " + LaserBestTimes.FormatTime(r.PreviousBestSeconds) + ")";
                m_BestDetail.color = GoldColor;
            }
            else if (r.HadPreviousBest)
            {
                m_BestValue.text = LaserBestTimes.FormatTime(r.PreviousBestSeconds);
                m_BestDetail.text = r.Completions + " runs, most stars " + r.BestStars;
                m_BestDetail.color = LabelColor;
            }
            else
            {
                m_BestValue.text = LaserBestTimes.FormatTime(r.Seconds);
                m_BestDetail.text = "First completion: your time to beat";
                m_BestDetail.color = AccentColor;
            }

            string next = !r.IsLastLevel && !string.IsNullOrEmpty(r.NextTitle)
                ? "   Next: level " + (number + 1) + ", " + r.NextTitle + "."
                : string.Empty;
            if (r.IsLastLevel)
            {
                m_Banner.text = "Total stars: " + r.TotalBestStars + " / " + r.MaxTotalStars +
                                ". You aligned an interferometer like the engineers of the Einstein Telescope!";
            }
            else if (r.Stars >= 3)
            {
                m_Banner.text = "Perfect run!" + next;
            }
            else
            {
                m_Banner.text = LaserResultsCard.StarTip(r) + next;
            }

            m_Rules.text = string.IsNullOrEmpty(r.StarRules) ? string.Empty : r.StarRules;
            m_RetryLabel.text = r.IsLastLevel ? "PLAY AGAIN" : "RETRY";
            m_NextLabel.text = r.IsLastLevel ? "RESTART FROM LEVEL 1" : "NEXT LEVEL";
        }

        // ----- Building -------------------------------------------------------------------------------

        private bool EnsureBuilt()
        {
            if (m_Root)
            {
                return true;
            }

            Transform existing = m_Canvas.transform.Find(OverlayName);
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            m_Layer = m_Canvas.gameObject.layer;
            Text anyText = m_Canvas.GetComponentInChildren<Text>(true);
            m_Font = anyText != null && anyText.font != null ? anyText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Full-screen background: also blocks clicks to the panels underneath.
            m_Root = NewRect(OverlayName, m_Canvas.transform);
            m_Root.anchorMin = Vector2.zero;
            m_Root.anchorMax = Vector2.one;
            m_Root.offsetMin = Vector2.zero;
            m_Root.offsetMax = Vector2.zero;
            AddImage(m_Root, BackgroundColor, true);

            RectTransform card = NewRect("Card", m_Root, Vector2.zero, new Vector2(2800f, 1100f));
            AddImage(card, CardColor, true);
            AddImage(NewRect("Accent", card, new Vector2(0f, 543f), new Vector2(2800f, 14f)), AccentColor, false);

            m_Title = NewText("Title", card, new Vector2(0f, 440f), new Vector2(2700f, 150f), 118, AccentColor, FontStyle.Bold);
            m_Subtitle = NewText("Subtitle", card, new Vector2(0f, 335f), new Vector2(2700f, 80f), 54, LabelColor, FontStyle.Normal);

            for (int i = 0; i < m_Stars.Length; i++)
            {
                RectTransform star = NewRect("Star " + (i + 1), card, new Vector2((i - 1) * 240f, 190f), new Vector2(170f, 170f));
                Image image = AddImage(star, StarOffColor, false);
                image.sprite = StarSprite();
                image.preserveAspect = true;
                m_Stars[i] = image;
            }

            BuildColumn(card, -900f, "TIME", out m_TimeValue, out m_TimeDetail);
            BuildColumn(card, 0f, "SAFETY INCIDENTS", out m_IncidentsValue, out m_IncidentsDetail);
            BuildColumn(card, 900f, "PERSONAL BEST", out m_BestValue, out m_BestDetail);

            m_Banner = NewText("Tip", card, new Vector2(0f, -225f), new Vector2(2700f, 70f), 50, TipColor, FontStyle.Normal);
            m_Rules = NewText("Star Rules", card, new Vector2(0f, -295f), new Vector2(2700f, 60f), 38, LabelColor, FontStyle.Italic);

            m_RetryLabel = NewButton("Retry Button", card, new Vector2(-460f, -420f), RetryColor, Retry);
            m_NextLabel = NewButton("Next Button", card, new Vector2(460f, -420f), NextColor, Next);

            m_Root.gameObject.SetActive(false);
            return true;
        }

        private void BuildColumn(RectTransform card, float x, string label, out Text value, out Text detail)
        {
            NewText(label + " Label", card, new Vector2(x, 40f), new Vector2(860f, 60f), 42, LabelColor, FontStyle.Bold).text = label;
            value = NewText(label + " Value", card, new Vector2(x, -40f), new Vector2(860f, 110f), 92, ValueColor, FontStyle.Bold);
            detail = NewText(label + " Detail", card, new Vector2(x, -125f), new Vector2(860f, 60f), 40, LabelColor, FontStyle.Normal);
        }

        private Text NewButton(string name, RectTransform parent, Vector2 position, Color color, UnityEngine.Events.UnityAction onClick)
        {
            RectTransform rect = NewRect(name, parent, position, new Vector2(780f, 150f));
            Image image = AddImage(rect, Color.white, true);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.25f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.3f);
            colors.selectedColor = color;
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(onClick);

            Text label = NewText("Label", rect, Vector2.zero, new Vector2(760f, 140f), 64, Color.white, FontStyle.Bold);
            return label;
        }

        private RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = m_Layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private RectTransform NewRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image AddImage(RectTransform rect, Color color, bool raycastTarget)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private Text NewText(string name, RectTransform parent, Vector2 position, Vector2 size, int fontSize, Color color, FontStyle style)
        {
            RectTransform rect = NewRect(name, parent, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = m_Font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// A five-pointed star drawn in code (white, tinted by the Image colour). The legacy UI font has no reliable
        /// star glyph on every platform, and this needs no texture asset.
        /// </summary>
        private static Sprite StarSprite()
        {
            if (s_StarSprite != null)
            {
                return s_StarSprite;
            }

            const int size = 128;
            const int samples = 4;   // per axis, for smooth edges
            var points = new Vector2[10];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float radius = (i % 2 == 0 ? 0.48f : 0.19f) * size;
                points[i] = new Vector2(size * 0.5f + Mathf.Cos(angle) * radius, size * 0.53f + Mathf.Sin(angle) * radius);
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / samples, y + (sy + 0.5f) / samples);
                            if (InsidePolygon(points, p))
                            {
                                inside++;
                            }
                        }
                    }

                    byte alpha = (byte)(255 * inside / (samples * samples));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Results Star (runtime)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            s_StarSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            s_StarSprite.name = "Results Star (runtime)";
            return s_StarSprite;
        }

        private static bool InsidePolygon(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
