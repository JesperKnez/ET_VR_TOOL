using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// The control-room desk: one set of buttons that steers whichever mirror is selected, like the
    /// optic-selection screens in a real interferometer control room.
    ///
    /// Hook-up for VR buttons (XR Interaction Toolkit): on the button's "Select Entered" (or the
    /// poke button's pressed event) call e.g. <see cref="YawLeft"/>. For hold-to-move buttons call
    /// <see cref="BeginHoldYawLeft"/> on press and <see cref="StopHoldMove"/> on release.
    /// </summary>
    public class MirrorControlPanel : MonoBehaviour
    {
        [Tooltip("The mirrors this panel can steer. The first one is selected at start.")]
        [SerializeField] private MirrorMount[] mirrors = new MirrorMount[0];

        [Tooltip("Index of the mirror selected at start.")]
        [SerializeField, Min(0)] private int startSelection = 0;

        [Header("Events")]
        [Tooltip("Name of the newly selected mirror. Connect to a TextMeshPro text.")]
        [SerializeField] private UnityEvent<string> onSelectionChanged = new UnityEvent<string>();

        [Tooltip("Readout line of the selected mirror whenever it moves. Connect to a TextMeshPro text.")]
        [SerializeField] private UnityEvent<string> onSelectedReadoutChanged = new UnityEvent<string>();

        [Tooltip("Fine mode switched on (true) or off (false) on the selected mirror.")]
        [SerializeField] private UnityEvent<bool> onFineModeChanged = new UnityEvent<bool>();

        private static readonly List<MirrorControlPanel> s_ActivePanels = new List<MirrorControlPanel>();

        [Tooltip("Changes to the same mirror within this many seconds count as one move for UNDO (a burst of taps or one hold).")]
        [SerializeField, Min(0f)] private float undoMergeSeconds = 0.75f;

        [Tooltip("How many moves UNDO remembers.")]
        [SerializeField, Range(1, 200)] private int undoDepth = 64;

        private struct UndoEntry
        {
            public MirrorMount Mirror;
            public Vector2 Angles;
            public float LastChangeTime;
        }

        private readonly List<UndoEntry> m_Undo = new List<UndoEntry>();
        private MirrorMount[] m_Watched = new MirrorMount[0];
        private int m_SelectedIndex = -1;
        private MirrorMount m_Subscribed;

        public MirrorMount SelectedMirror => IsValidIndex(m_SelectedIndex) ? mirrors[m_SelectedIndex] : null;
        public int SelectedIndex => m_SelectedIndex;
        public int MirrorCount => mirrors.Length;

        public UnityEvent<string> OnSelectionChanged => onSelectionChanged;
        public UnityEvent<string> OnSelectedReadoutChanged => onSelectedReadoutChanged;
        public UnityEvent<bool> OnFineModeChanged => onFineModeChanged;

        /// <summary>The first enabled panel in the scene (used by control pads that have no panel assigned).</summary>
        public static MirrorControlPanel FirstActive => s_ActivePanels.Count > 0 ? s_ActivePanels[0] : null;

        // ----- Selection --------------------------------------------------------------------------

        public void SelectMirror(int index)
        {
            if (!IsValidIndex(index))
            {
                return;
            }

            StopHoldMove();
            m_SelectedIndex = index;
            Subscribe(mirrors[index]);
            onSelectionChanged.Invoke(mirrors[index].DisplayName);
            onSelectedReadoutChanged.Invoke(mirrors[index].BuildReadout());
            onFineModeChanged.Invoke(mirrors[index].IsFineMode);
        }

        /// <summary>Number of moves that UNDO can take back.</summary>
        public int UndoCount => m_Undo.Count;

        /// <summary>
        /// Take back the last move (a tap, a burst of taps or one hold on the same mirror, or a reset).
        /// The mirror goes back to its previous angles and becomes the selected mirror.
        /// </summary>
        public bool Undo()
        {
            if (m_Undo.Count == 0)
            {
                return false;
            }

            StopHoldMove();
            UndoEntry entry = m_Undo[m_Undo.Count - 1];
            if (entry.Mirror != null && entry.Mirror.InputLocked)
            {
                return false;   // level completed: the mirrors stay where they are
            }

            m_Undo.RemoveAt(m_Undo.Count - 1);
            if (entry.Mirror == null)
            {
                return false;
            }

            int index = System.Array.IndexOf(mirrors, entry.Mirror);
            if (index >= 0 && index != m_SelectedIndex)
            {
                SelectMirror(index);
            }

            entry.Mirror.RestoreCommandedAngles(entry.Angles);
            return true;
        }

        public void ClearUndo() => m_Undo.Clear();

        /// <summary>Replace the list of steerable mirrors (e.g. when a new level loads) and select one of them.</summary>
        public void SetMirrors(MirrorMount[] newMirrors, int select = 0)
        {
            StopHoldMove();
            Subscribe(null);
            WatchMirrors(false);
            m_Undo.Clear();
            mirrors = newMirrors ?? new MirrorMount[0];
            if (isActiveAndEnabled)
            {
                WatchMirrors(true);
            }

            m_SelectedIndex = -1;
            if (mirrors.Length > 0)
            {
                SelectMirror(Mathf.Clamp(select, 0, mirrors.Length - 1));
            }
        }

        /// <summary>The mirrors this panel steers.</summary>
        public System.Collections.Generic.IReadOnlyList<MirrorMount> Mirrors => mirrors;

        public void SelectNext() => SelectMirror(Wrap(m_SelectedIndex + 1));
        public void SelectPrevious() => SelectMirror(Wrap(m_SelectedIndex - 1));

        // ----- Step buttons -----------------------------------------------------------------------
        //
        // The arrow buttons mean "move the spot": ► = the spot goes right on the camera view of this mirror's target
        // (the dartboard), ▲ = up. Which mirror axis does that, and in which direction, depends on how the mirror is
        // mounted and how many mirrors come after it (each one mirrors the picture). LaserMirrorCamera measures that and
        // stores it per mirror in ArrowMaps. Without a stored map an arrow is simply the mirror's own axis.

        /// <summary>Per mirror: arrow (x = ►, y = ▲) to mirror axes: yaw = x * Right.x + y * Up.x, pitch = x * Right.y + y * Up.y.</summary>
        public struct ArrowMap
        {
            public Vector2 Right;
            public Vector2 Up;
            public static readonly ArrowMap Identity = new ArrowMap { Right = new Vector2(1f, 0f), Up = new Vector2(0f, 1f) };
            public Vector2 ToAxes(Vector2 arrow) => arrow.x * Right + arrow.y * Up;
        }

        public static readonly System.Collections.Generic.Dictionary<MirrorMount, ArrowMap> ArrowMaps = new System.Collections.Generic.Dictionary<MirrorMount, ArrowMap>();

        public static ArrowMap MapFor(MirrorMount mirror) => mirror != null && ArrowMaps.TryGetValue(mirror, out ArrowMap map) ? map : ArrowMap.Identity;

        /// <summary>One press of an arrow (x = ► / ◄, y = ▲ / ▼).</summary>
        public void PressArrow(Vector2Int arrow)
        {
            MirrorMount mirror = SelectedMirror;
            if (mirror == null)
            {
                return;
            }

            Vector2 axes = MapFor(mirror).ToAxes(arrow);
            int yaw = Mathf.RoundToInt(axes.x), pitch = Mathf.RoundToInt(axes.y);
            if (yaw != 0) mirror.NudgeYaw(yaw);
            if (pitch != 0) mirror.NudgePitch(pitch);
        }

        public void YawLeft() => PressArrow(new Vector2Int(-1, 0));
        public void YawRight() => PressArrow(new Vector2Int(1, 0));
        public void PitchUp() => PressArrow(new Vector2Int(0, 1));
        public void PitchDown() => PressArrow(new Vector2Int(0, -1));
        public void NudgeYaw(int steps) => PressArrow(new Vector2Int(steps, 0));
        public void NudgePitch(int steps) => PressArrow(new Vector2Int(0, steps));

        // ----- Hold buttons -----------------------------------------------------------------------

        public void BeginHoldYawLeft() => SetHoldInput(new Vector2(-1f, 0f));
        public void BeginHoldYawRight() => SetHoldInput(new Vector2(1f, 0f));
        public void BeginHoldPitchUp() => SetHoldInput(new Vector2(0f, 1f));
        public void BeginHoldPitchDown() => SetHoldInput(new Vector2(0f, -1f));

        /// <summary>Hold / thumbstick input in arrow directions (x = right, y = up).</summary>
        public void SetHoldInput(Vector2 input)
        {
            MirrorMount mirror = SelectedMirror;
            if (mirror != null) mirror.SetHoldInput(MapFor(mirror).ToAxes(input));
        }

        public void StopHoldMove() => SelectedMirror?.StopHoldMove();

        // ----- Knobs / sliders (0..1) -------------------------------------------------------------

        public void SetSelectedYawNormalized(float value01) => SelectedMirror?.SetYawNormalized(value01);
        public void SetSelectedPitchNormalized(float value01) => SelectedMirror?.SetPitchNormalized(value01);

        // ----- Modes ------------------------------------------------------------------------------

        public void ToggleFineMode()
        {
            MirrorMount mirror = SelectedMirror;
            if (mirror == null)
            {
                return;
            }

            mirror.ToggleFineMode();
            onFineModeChanged.Invoke(mirror.IsFineMode);
        }

        public void SetFineMode(bool fine)
        {
            MirrorMount mirror = SelectedMirror;
            if (mirror == null)
            {
                return;
            }

            mirror.SetFineMode(fine);
            onFineModeChanged.Invoke(fine);
        }

        public void ResetSelectedToStart()
        {
            MirrorMount mirror = SelectedMirror;
            if (mirror != null && !mirror.InputLocked)
            {
                mirror.ResetToStart();
            }
        }

        // ----- Unity messages ---------------------------------------------------------------------

        private void Start()
        {
            if (mirrors.Length > 0)
            {
                SelectMirror(Mathf.Clamp(startSelection, 0, mirrors.Length - 1));
            }
        }

        private void OnDisable()
        {
            WatchMirrors(false);
            s_ActivePanels.Remove(this);
            StopHoldMove();
            Subscribe(null);
        }

        private void OnEnable()
        {
            WatchMirrors(true);
            s_ActivePanels.Add(this);
            if (IsValidIndex(m_SelectedIndex))
            {
                Subscribe(mirrors[m_SelectedIndex]);
            }
        }

        // ----- Internals --------------------------------------------------------------------------

        private void WatchMirrors(bool watch)
        {
            foreach (MirrorMount mirror in m_Watched)
            {
                if (mirror != null)
                {
                    mirror.CommandChanged -= HandleCommandChanged;
                }
            }

            m_Watched = watch ? (MirrorMount[])mirrors.Clone() : new MirrorMount[0];
            foreach (MirrorMount mirror in m_Watched)
            {
                if (mirror != null)
                {
                    mirror.CommandChanged += HandleCommandChanged;
                }
            }
        }

        private void HandleCommandChanged(MirrorMount mirror, Vector2 previousAngles)
        {
            float now = Time.unscaledTime;
            int last = m_Undo.Count - 1;
            if (last >= 0 && m_Undo[last].Mirror == mirror && now - m_Undo[last].LastChangeTime <= undoMergeSeconds)
            {
                UndoEntry merged = m_Undo[last];
                merged.LastChangeTime = now;
                m_Undo[last] = merged;
                return;
            }

            m_Undo.Add(new UndoEntry { Mirror = mirror, Angles = previousAngles, LastChangeTime = now });
            if (m_Undo.Count > undoDepth)
            {
                m_Undo.RemoveAt(0);
            }
        }

        private void Subscribe(MirrorMount mirror)
        {
            if (m_Subscribed != null)
            {
                m_Subscribed.AnglesChanged -= HandleSelectedMirrorMoved;
            }

            m_Subscribed = mirror;

            if (m_Subscribed != null)
            {
                m_Subscribed.AnglesChanged += HandleSelectedMirrorMoved;
            }
        }

        private void HandleSelectedMirrorMoved(MirrorMount mirror)
        {
            onSelectedReadoutChanged.Invoke(mirror.BuildReadout());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            s_ActivePanels.Clear();
        }

        private bool IsValidIndex(int index)
        {
            return mirrors != null && index >= 0 && index < mirrors.Length && mirrors[index] != null;
        }

        private int Wrap(int index)
        {
            if (mirrors.Length == 0)
            {
                return -1;
            }

            return ((index % mirrors.Length) + mirrors.Length) % mirrors.Length;
        }
    }
}
