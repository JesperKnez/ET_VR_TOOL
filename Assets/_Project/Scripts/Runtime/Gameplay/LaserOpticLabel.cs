using UnityEngine;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Floating name tag above an optic, iris or detector that always turns towards the player, so you can
    /// tell the frames apart from behind the control-room glass. When its mirror is the one selected on the
    /// <see cref="MirrorControlPanel"/>, the tag grows and lights up and a glowing ring appears around the mirror.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic + 30)]
    public class LaserOpticLabel : MonoBehaviour
    {
        [Tooltip("The mirror this label belongs to (empty for irises, detectors and the laser).")]
        [SerializeField] private MirrorMount mount = null;

        [Tooltip("Text to show. Empty = the mirror's display name, or else the name of the object this label sits under.")]
        [SerializeField] private string labelText = "";

        [SerializeField] private Text text = null;
        [SerializeField] private Image background = null;

        [Tooltip("Glowing ring around the mirror, shown while it is selected.")]
        [SerializeField] private Renderer selectionRing = null;

        [Header("Look")]
        [SerializeField] private Color normalTextColor = new Color(0.85f, 0.9f, 0.95f, 1f);
        [SerializeField] private Color normalBackground = new Color(0.02f, 0.04f, 0.07f, 0.6f);
        [SerializeField] private Color selectedColor = new Color(0.3f, 0.85f, 1f, 1f);
        [SerializeField] private Color selectedBackground = new Color(0.04f, 0.16f, 0.24f, 0.9f);
        [SerializeField, Min(1f)] private float selectedScale = 1.45f;

        [Tooltip("Turn the label towards the main camera (yaw only).")]
        [SerializeField] private bool faceCamera = true;

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock m_Block;
        private Vector3 m_BaseScale = Vector3.one;
        private int m_Shown = -1;   // -1 unknown, 0 normal, 1 selected

        public MirrorMount Mount => mount;
        public bool IsSelected { get; private set; }

        /// <summary>The text on the tag (in capitals while selected).</summary>
        public string DisplayedText => text != null ? text.text : string.Empty;

        public void SetLabel(string value)
        {
            labelText = value;
            m_Shown = -1;
        }

        private void Awake()
        {
            m_BaseScale = transform.localScale;
            if (selectionRing != null)
            {
                selectionRing.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            MirrorControlPanel panel = MirrorControlPanel.FirstActive;
            IsSelected = mount != null && panel != null && panel.SelectedMirror == mount;
            int state = IsSelected ? 1 : 0;
            if (state != m_Shown)
            {
                m_Shown = state;
                ApplyState();
            }

            if (IsSelected && selectionRing != null)
            {
                float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 4f);
                SetRingColor(selectedColor * pulse);
            }

            if (faceCamera)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 away = transform.position - cam.transform.position;
                    away.y = 0f;
                    if (away.sqrMagnitude > 1e-4f)
                    {
                        transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
                    }
                }
            }
        }

        private void ApplyState()
        {
            string name = ResolveName();
            if (text != null)
            {
                text.text = IsSelected ? "▼ " + name.ToUpperInvariant() : name;
                text.color = IsSelected ? selectedColor : normalTextColor;
                text.fontStyle = IsSelected ? FontStyle.Bold : FontStyle.Normal;
            }

            if (background != null)
            {
                background.color = IsSelected ? selectedBackground : normalBackground;
                RectTransform rect = background.rectTransform;
                if (text != null && rect != null)
                {
                    rect.sizeDelta = new Vector2(text.preferredWidth + text.fontSize * 0.9f, rect.sizeDelta.y);
                }
            }

            transform.localScale = m_BaseScale * (IsSelected ? selectedScale : 1f);
            if (selectionRing != null)
            {
                selectionRing.gameObject.SetActive(IsSelected);
            }
        }

        private string ResolveName()
        {
            if (!string.IsNullOrEmpty(labelText))
            {
                return labelText;
            }

            if (mount != null)
            {
                return mount.DisplayName;
            }

            return transform.parent != null ? transform.parent.name : gameObject.name;
        }

        private void SetRingColor(Color color)
        {
            m_Block ??= new MaterialPropertyBlock();
            selectionRing.GetPropertyBlock(m_Block);
            m_Block.SetColor(s_BaseColorId, color);
            m_Block.SetColor(s_ColorId, color);
            m_Block.SetColor(s_EmissionColorId, color * 2f);
            selectionRing.SetPropertyBlock(m_Block);
        }
    }
}
