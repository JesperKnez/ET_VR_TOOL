using UnityEngine;
using UnityEngine.UI;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Text read-out of the control-room monitor: what the operator needs to know at a glance.
    /// Status line (no light / one beam / centre the spots / aligned), a hint that says what to do next,
    /// laser state and wavelength, received power and the position of every beam in millimetres.
    /// Works next to <see cref="LaserTargetDisplay"/>, which draws the spots and the quadrant bars.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic + 10)]
    public class LaserMonitorReadout : MonoBehaviour
    {
        public enum ReadoutState
        {
            LaserOff,
            NoLight,
            MissingBeam,
            NotCentred,
            Holding,
            Completed,
            Checkpoint,
            Briefing
        }

        [Header("Sources")]
        [SerializeField] private LaserTarget target = null;
        [Tooltip("Empty = the first active laser in the scene.")]
        [SerializeField] private LaserEmitter laser = null;
        [Tooltip("Optional: names the surface the stray light hits.")]
        [SerializeField] private LaserSafetyMonitor safetyMonitor = null;
        [Tooltip("Optional: hold progress, time and stars.")]
        [SerializeField] private LaserAlignmentObjective objective = null;

        [Tooltip("Optional: shows the countdown to the next level.")]
        [SerializeField] private LaserLevelManager levelManager = null;

        [Header("Text fields (all optional)")]
        [SerializeField] private Text headerText = null;
        [SerializeField] private Text statusText = null;
        [SerializeField] private Text hintText = null;
        [SerializeField] private Text laserText = null;
        [SerializeField] private Text powerText = null;
        [SerializeField] private Text beamsText = null;

        [Header("Look")]
        [SerializeField] private Color neutralColor = new Color(0.62f, 0.7f, 0.78f, 1f);
        [SerializeField] private Color warningColor = new Color(1f, 0.6f, 0.15f, 1f);
        [SerializeField] private Color goodColor = new Color(0.3f, 1f, 0.45f, 1f);
        [SerializeField] private Color laserOnColor = new Color(1f, 0.3f, 0.2f, 1f);

        [Tooltip("Text updates per second (the spots and bars on the screen still move every frame).")]
        [SerializeField, Min(1f)] private float refreshRate = 12f;

        [Tooltip("Seconds the level briefing stays in the hint line after a level starts.")]
        [SerializeField, Min(0f)] private float briefingSeconds = 8f;

        private float m_NextRefresh;
        private LaserLevel m_Level;
        private string m_Header = "OUTPUT PORT DETECTOR";
        private float m_LevelStartTime = float.NegativeInfinity;

        public ReadoutState State { get; private set; }
        public string StatusLine { get; private set; } = string.Empty;
        public string HintLine { get; private set; } = string.Empty;
        public string LaserLine { get; private set; } = string.Empty;
        public string PowerLine { get; private set; } = string.Empty;
        public string BeamsLine { get; private set; } = string.Empty;
        public string HeaderLine => m_Header;

        /// <summary>Show a new level: its detector, laser, irises and title (called by <see cref="LaserLevelManager"/>).</summary>
        public void SetLevel(LaserLevel level, int index, int count)
        {
            m_Level = level;
            if (level != null)
            {
                target = level.Detector;
                laser = level.Laser;
                m_Header = "LEVEL " + (index + 1) + "/" + count + "  |  " + level.Title.ToUpperInvariant();
            }

            m_LevelStartTime = Time.time;
            m_NextRefresh = 0f;
            Refresh();
        }

        private LaserEmitter Laser
        {
            get
            {
                if (laser == null && LaserEmitter.ActiveEmitters.Count > 0)
                {
                    return LaserEmitter.ActiveEmitters[0];
                }

                return laser;
            }
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < m_NextRefresh)
            {
                return;
            }

            m_NextRefresh = Time.unscaledTime + 1f / refreshRate;
            Refresh();
        }

        /// <summary>Rebuilds all lines now. Called automatically a few times per second.</summary>
        public void Refresh()
        {
            LaserEmitter emitter = Laser;
            bool emitting = emitter != null && emitter.isActiveAndEnabled && emitter.IsShutterOpen && emitter.OutputPower > 0f;

            BuildLaserLine(emitter, emitting);
            BuildStatus(emitter, emitting, out Color statusColor);
            BuildPowerAndBeams();

            Apply(headerText, m_Header, new Color(0.8f, 0.9f, 1f, 1f));
            Apply(statusText, StatusLine, statusColor);
            Apply(hintText, HintLine, neutralColor);
            Apply(laserText, LaserLine, emitting ? laserOnColor : neutralColor);
            Apply(powerText, PowerLine, target != null && target.IsIlluminated ? statusColor : neutralColor);
            Apply(beamsText, BeamsLine, neutralColor);
        }

        private void BuildLaserLine(LaserEmitter emitter, bool emitting)
        {
            if (emitter == null)
            {
                LaserLine = "NO LASER";
                return;
            }

            float nm = emitter.WavelengthNm;
            string kind = LaserOptics.IsHumanVisible(nm) ? "visible" : "infrared";
            string state = emitting ? "LASER ON" : "SHUTTER CLOSED";
            LaserLine = state + "  " + LaserText.Number(nm, 0) + " nm (" + kind + ")";
        }

        private void BuildStatus(LaserEmitter emitter, bool emitting, out Color color)
        {
            if (target == null)
            {
                State = ReadoutState.NoLight;
                StatusLine = "NO DETECTOR LINKED";
                HintLine = "Fill the Target slot of this monitor.";
                color = warningColor;
                return;
            }

            if (objective != null && objective.IsCompleted)
            {
                State = ReadoutState.Completed;
                bool allDone = levelManager != null && levelManager.IsLastLevel;
                StatusLine = (allDone ? "ALL LEVELS COMPLETE  " : levelManager != null ? "LEVEL COMPLETE  " : "ALIGNMENT COMPLETE  ") + Stars(objective.Stars);
                HintLine = "Done in " + LaserText.Number(objective.ElapsedSeconds, 0) + " s with " + objective.Mistakes +
                           (objective.Mistakes == 1 ? " safety incident." : " safety incidents.");
                float next = levelManager != null ? levelManager.SecondsUntilNextLevel : -1f;
                if (next >= 0f)
                {
                    HintLine += " Next level in " + LaserText.Number(Mathf.Ceil(next), 0) + " s.";
                }
                else if (allDone)
                {
                    HintLine += " Press NEXT LEVEL to play again from level 1.";
                }
                else if (levelManager != null && levelManager.WaitsForPlayer)
                {
                    HintLine += " See the results card; press NEXT LEVEL to continue.";
                }

                color = goodColor;
                return;
            }

            if (levelManager != null && levelManager.IsBriefingOpen)
            {
                State = ReadoutState.Briefing;
                StatusLine = "READ THE BRIEFING";
                HintLine = "Close the floating screen (X or START) to begin. The timer starts then. HELP on the pad opens it again.";
                color = neutralColor;
                return;
            }

            if (!emitting)
            {
                State = ReadoutState.LaserOff;
                StatusLine = "NO SIGNAL - LASER OFF";
                HintLine = "Open the shutter (SHUTTER button or L).";
                color = neutralColor;
                return;
            }

            bool invisible = emitter != null && !LaserVisibility.ShouldRender(emitter.WavelengthNm);
            string seeIt = invisible ? " Beam is infrared: turn on the IR viewer to see it." : string.Empty;
            bool showBriefing = m_Level != null && !string.IsNullOrEmpty(m_Level.Briefing) && Time.time - m_LevelStartTime < briefingSeconds;

            if (m_Level != null)
            {
                LaserTarget[] checkpoints = m_Level.Checkpoints;
                for (int i = 0; i < checkpoints.Length; i++)
                {
                    LaserTarget checkpoint = checkpoints[i];
                    if (checkpoint == null || checkpoint.IsAligned)
                    {
                        continue;
                    }

                    State = ReadoutState.Checkpoint;
                    string label = m_Level.CheckpointLabel(i);
                    StatusLine = checkpoint.IsIlluminated ? "CENTRE THE BEAM ON " + label.ToUpperInvariant() : "BEAM MISSES " + label.ToUpperInvariant();
                    string position = checkpoint.IsIlluminated
                        ? label + ": " + LaserText.Number(checkpoint.WorstBeamOffset * 1000f, 1) + " mm from the centre (" + checkpoint.DescribeSpotPosition() +
                          "), goal under " + LaserText.Number(checkpoint.CenteringTolerance * 1000f, 0) + " mm. "
                        : string.Empty;
                    HintLine = showBriefing ? m_Level.Briefing : position + m_Level.CheckpointHint(i) + seeIt;
                    color = warningColor;
                    return;
                }
            }

            if (!target.IsIlluminated)
            {
                State = ReadoutState.NoLight;
                StatusLine = "NO LIGHT ON DETECTOR";
                string where = safetyMonitor != null && safetyMonitor.IsStrayLightActive && safetyMonitor.StraySurfaceName.Length > 0
                    ? "Light is hitting " + safetyMonitor.StraySurfaceName + ". "
                    : string.Empty;
                HintLine = showBriefing ? m_Level.Briefing : where + "Steer the end mirrors until the beams reach this detector." + seeIt;
                color = warningColor;
                return;
            }

            if (target.BeamCount < target.RequiredBeamCount)
            {
                State = ReadoutState.MissingBeam;
                StatusLine = target.BeamCount + " OF " + target.RequiredBeamCount + " BEAMS ON DETECTOR";
                HintLine = "Select the other end mirror (NEXT) and bring its beam here too." + seeIt;
                color = warningColor;
                return;
            }

            bool centred = target.WorstBeamOffset <= target.CenteringTolerance;
            if (!target.IsAligned && centred && target.ReceivedPowerFraction < target.RequiredPowerFraction)
            {
                State = ReadoutState.NotCentred;
                StatusLine = "POWER TOO LOW";
                HintLine = "The spots are centred but only " + LaserText.Percent(target.ReceivedPowerFraction) + " of the laser arrives (need " +
                           LaserText.Percent(target.RequiredPowerFraction) + "). A beam is clipped: centre it better on the irises and mirrors.";
                color = warningColor;
                return;
            }

            if (!target.IsAligned)
            {
                State = ReadoutState.NotCentred;
                StatusLine = "CENTRE THE SPOTS";
                string worst = LaserText.Number(target.WorstBeamOffset * 1000f, 1);
                string goal = LaserText.Number(target.CenteringTolerance * 1000f, 1);
                HintLine = "Worst beam is " + worst + " mm off centre, goal under " + goal +
                           " mm. Use FINE steps for the last bit.";
                if (target.ReceivedPowerFraction < target.RequiredPowerFraction)
                {
                    HintLine += " Power is low: a beam is clipped on an optic edge.";
                }

                color = warningColor;
                return;
            }

            State = ReadoutState.Holding;
            float progress = objective != null ? objective.HoldProgress : 1f;
            StatusLine = "ALIGNED - HOLD " + LaserText.Percent(progress);
            HintLine = "Keep both spots inside the green circle.";
            color = goodColor;
        }

        private void BuildPowerAndBeams()
        {
            if (target == null || !target.IsIlluminated)
            {
                PowerLine = "0.00 W";
                BeamsLine = "BEAM 1  ---\nBEAM 2  ---";
                return;
            }

            PowerLine = LaserText.Number(target.ReceivedPowerWatts, 2) + " W (" + LaserText.Percent(target.ReceivedPowerFraction) + ")";

            int rows = Mathf.Max(target.RequiredBeamCount, target.TrackedBeamCount);
            var builder = new System.Text.StringBuilder(64);
            for (int i = 0; i < rows; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append("BEAM ").Append(i + 1).Append("  ");
                if (i >= target.TrackedBeamCount)
                {
                    builder.Append("---");
                    continue;
                }

                target.GetBeam(i, out Vector2 offset, out _);
                builder.Append("x ").Append(LaserText.SignedNumber(offset.x * 1000f, 1))
                       .Append("  y ").Append(LaserText.SignedNumber(offset.y * 1000f, 1)).Append(" mm");
            }

            BeamsLine = builder.ToString();
        }

        private static string Stars(int stars)
        {
            stars = Mathf.Clamp(stars, 0, 3);
            return new string('*', stars) + new string('-', 3 - stars);
        }

        private static void Apply(Text label, string value, Color color)
        {
            if (label == null)
            {
                return;
            }

            if (label.text != value)
            {
                label.text = value;
            }

            label.color = color;
        }
    }
}
