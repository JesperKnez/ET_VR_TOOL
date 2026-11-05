using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// A speech bubble above Lumi that always faces the player.
    ///
    /// While Lumi thinks it shows what it understood (in grey, so a misheard question is obvious), then
    /// Lumi's answer for as long as Lumi talks. The bubble keeps the same apparent size from any distance
    /// and draws on top of the room, so it can always be read.
    /// It doesn't take pointer input, so it never blocks the laser controls.
    /// Put it on the Lumi object next to LumiTutor. The bubble is built in code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LumiTutor))]
    public class LumiSubtitles : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Height of the bubble's bottom edge above the Lumi object (m).")]
        [SerializeField, Range(0.1f, 1f)] private float heightAboveLumi = 0.4f;
        [Tooltip("Width of the bubble when the player is at the reference distance (m).")]
        [SerializeField, Range(0.3f, 1.5f)] private float width = 0.7f;
        [Tooltip("The bubble has its normal size at this distance and grows or shrinks to stay readable (m).")]
        [SerializeField, Range(0.5f, 3f)] private float referenceDistance = 1.5f;
        [Tooltip("The smallest and largest size, as a multiple of the normal size.")]
        [SerializeField] private Vector2 scaleLimits = new Vector2(0.7f, 2.5f);

        [Header("Look")]
        [SerializeField, Range(20, 60)] private int fontSize = 34;
        [SerializeField] private Color answerColor = Color.white;
        [SerializeField] private Color questionColor = new Color(0.65f, 0.75f, 0.8f, 1f);
        [SerializeField] private Color backgroundColor = new Color(0.03f, 0.08f, 0.12f, 0.85f);

        [Header("Timing")]
        [Tooltip("How long the text stays after Lumi finishes (s).")]
        [SerializeField, Range(0f, 5f)] private float lingerSeconds = 1.5f;

        private const float UnitsPerMeter = 1000f; // canvas units are millimetres at the normal size
        private const int OnTopRenderQueue = 4000; // after all of the room's transparent objects
        private const int OnTopSortingOrder = 1000; // in front of other world-space canvases

        private LumiTutor tutor;
        private GameObject bubble;
        private Material onTopMaterial;
        private Text label;
        private float hideTimer;

        private void Awake()
        {
            tutor = GetComponent<LumiTutor>();
            Build();
            bubble.SetActive(false);
        }

        private void OnEnable()
        {
            tutor.OnQuestion.AddListener(ShowQuestion);
            tutor.OnReply.AddListener(ShowAnswer);
        }

        private void OnDisable()
        {
            tutor.OnQuestion.RemoveListener(ShowQuestion);
            tutor.OnReply.RemoveListener(ShowAnswer);
            if (bubble != null) bubble.SetActive(false);
        }

        private void ShowQuestion(string question) => Show("\"" + question + "\"", questionColor, FontStyle.Italic);

        private void ShowAnswer(string answer) => Show(answer, answerColor, FontStyle.Normal);

        private void Show(string text, Color color, FontStyle style)
        {
            label.text = text;
            label.color = color;
            label.fontStyle = style;
            hideTimer = lingerSeconds;
            bubble.SetActive(true);
        }

        private void LateUpdate()
        {
            if (!bubble.activeSelf) return;

            if (!tutor.IsBusy)
            {
                hideTimer -= Time.deltaTime;
                if (hideTimer <= 0f)
                {
                    bubble.SetActive(false);
                    return;
                }
            }

            // World-space placement, so the bubble ignores Lumi's turning and stays steady to read.
            Transform bubbleTransform = bubble.transform;
            bubbleTransform.position = transform.position + Vector3.up * heightAboveLumi;

            Camera player = Camera.main;
            if (player == null) return;
            Vector3 toBubble = bubbleTransform.position - player.transform.position;
            if (toBubble.sqrMagnitude > 0.0001f) bubbleTransform.rotation = Quaternion.LookRotation(toBubble, Vector3.up);

            float scale = Mathf.Clamp(toBubble.magnitude / referenceDistance, scaleLimits.x, scaleLimits.y) / UnitsPerMeter;
            bubbleTransform.localScale = Vector3.one * (scale / Mathf.Max(transform.lossyScale.x, 0.0001f));
        }

        private void Build()
        {
            bubble = new GameObject("Lumi Subtitles", typeof(RectTransform));
            bubble.transform.SetParent(transform, false);
            var canvas = bubble.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = OnTopSortingOrder;
            bubble.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

            // The standard UI material, but ignoring depth: nothing in the room can hide the bubble.
            onTopMaterial = new Material(Graphic.defaultGraphicMaterial) { renderQueue = OnTopRenderQueue };
            onTopMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);

            var canvasRect = (RectTransform)bubble.transform;
            canvasRect.pivot = new Vector2(0.5f, 0f);
            canvasRect.sizeDelta = new Vector2(width * UnitsPerMeter, 100f);

            // The panel is anchored to the bottom edge and grows upwards to fit the text.
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(canvasRect, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.right;
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = Vector2.zero;
            panelRect.anchoredPosition = Vector2.zero;

            var background = panel.AddComponent<Image>();
            background.color = backgroundColor;
            background.material = onTopMaterial;
            background.raycastTarget = false;

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 16, 16);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(panel.transform, false);
            label = textObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.material = onTopMaterial;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = false; // what the student says is shown as plain text, never as markup
            label.raycastTarget = false;
        }

        private void OnDestroy()
        {
            if (onTopMaterial != null) Destroy(onTopMaterial);
        }
    }
}
