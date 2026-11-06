using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using EinsteinTelescope.Laser;
using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Makes Lumi react to the laser level, using events the laser kit already has (no changes to it):
    ///
    ///   level starts            → Wave (a greeting), and the first time also a spoken explanation
    ///                             (LumiLevelIntros), which stops as soon as the level's timer starts
    ///   a mirror is selected    → look at that mirror for a moment, then back at the player
    ///   beam held on target     → Listen mood (watching intently) while the hold timer runs
    ///   objective completed     → Celebrate, and says the stars and why it worked
    ///   safety warning          → Puzzled and a short spoken warning (with cooldowns; Lumi is never disappointed)
    ///   stuck on the same thing → a spoken hint for the next step, less often each time
    ///
    /// It finds the laser objects in whatever scenes are loaded (and keeps looking for a few seconds,
    /// for scenes loaded additively), so nothing needs to be assigned. Without a laser level it does
    /// nothing. The latest warning text and the level's status report are kept for the AI tutor.
    /// The spoken lines are in LumiReactionLines.
    /// Put it on the Lumi object (next to the Animator and LumiLookAt).
    /// </summary>
    [DisallowMultipleComponent]
    public class LumiLaserReactions : MonoBehaviour
    {
        [Header("Reactions")]
        [SerializeField] private bool waveOnLevelStart = true;
        [Tooltip("Lumi explains a level out loud the first time it starts in a session. The lines are in LumiLevelIntros.")]
        [SerializeField] private bool explainLevels = true;
        [Tooltip("Seconds between the level starting and Lumi starting to explain, so the player can settle.")]
        [SerializeField, Range(0f, 5f)] private float explainDelay = 1f;
        [SerializeField] private bool lookAtSelectedMirror = true;
        [SerializeField, Range(0.5f, 6f)] private float mirrorLookSeconds = 2.5f;
        [SerializeField] private bool listenWhileHolding = true;
        [SerializeField] private bool celebrateOnComplete = true;
        [SerializeField] private bool puzzledOnWarning = true;
        [Tooltip("Minimum seconds between two Puzzled reactions, so Lumi doesn't nag.")]
        [SerializeField, Range(0f, 30f)] private float puzzledCooldown = 8f;

        [Header("Speaking by itself")]
        [SerializeField] private bool speakWarnings = true;
        [Tooltip("Minimum seconds between two spoken warnings, so Lumi doesn't nag.")]
        [SerializeField, Range(0f, 120f)] private float spokenWarningCooldown = 20f;
        [SerializeField] private bool speakWins = true;
        [SerializeField] private bool speakHints = true;
        [Tooltip("Seconds on the same step before Lumi offers a hint. The wait doubles after each hint on that step.")]
        [SerializeField, Range(15f, 300f)] private float stuckSeconds = 60f;

        [Tooltip("For how long (seconds) to keep looking for laser objects after Play starts.")]
        [SerializeField, Range(1f, 60f)] private float searchSeconds = 10f;

        private Animator animator;
        private LumiLookAt lookAt;
        private LumiTutor tutor;
        private LumiListener listener;
        private LaserLevelManager levelManager;
        private MirrorControlPanel mirrorPanel;
        private LaserAlignmentObjective objective;
        private LaserSafetyMonitor safety;
        private LaserMonitorReadout readout;
        private LaserMirrorCamera mirrorCamera;

        private static readonly Regex RealWorldScale = new Regex(@" Real ET scale:[^\r\n]*");

        private float lastWarningSpokenAt = float.NegativeInfinity;
        private string stuckStep;
        private float stuckSince, stuckWait;
        private float nextSearchAt, stopSearchingAt;
        private float lookBackAt = -1f, lastPuzzledAt = float.NegativeInfinity;
        private Transform lookingAtMirror;
        private bool listeningBecauseOfHold;

        private readonly HashSet<int> explainedLevels = new HashSet<int>();
        private string pendingExplanation;
        private float explainAt;
        private bool levelStartHandled, timerWasRunning;

        /// <summary>The most recent safety warning, in plain words (for the AI tutor). Empty if none yet.</summary>
        public string LastWarning { get; private set; } = string.Empty;

        /// <summary>Time.time of <see cref="LastWarning"/>, or negative infinity if there was none.</summary>
        public float LastWarningTime { get; private set; } = float.NegativeInfinity;

        /// <summary>True once the laser level's objective was found.</summary>
        public bool IsConnected => objective != null;

        /// <summary>
        /// A plain-text snapshot of the level for the AI tutor, or empty without a laser level: the laser kit's report
        /// in the names the player sees, plus what the monitor and mirror camera are telling the player right now.
        /// </summary>
        public string BuildStatusReport()
        {
            if (objective == null) return string.Empty;

            var report = new StringBuilder(UsePlayerNames(RealWorldScale.Replace(objective.BuildStatusReport(), string.Empty)));
            report.AppendLine().AppendLine("WHAT THE STUDENT SEES RIGHT NOW");
            if (readout != null) report.Append("Monitor: ").Append(readout.StatusLine).Append(". ").AppendLine(readout.HintLine);

            MirrorMount selected = mirrorPanel != null ? mirrorPanel.SelectedMirror : null;
            if (selected != null)
            {
                report.Append("Selected mirror: ").Append(selected.DisplayName).Append(" (")
                      .Append(selected.IsFineMode ? "FINE" : "COARSE").AppendLine(" steps).");
            }

            if (mirrorCamera != null && mirrorCamera.ShownMirror == selected)
            {
                report.Append("Mirror camera view: ").Append(mirrorCamera.CurrentViewName).Append(". ")
                      .AppendLine(ArrowsInWords(UsePlayerNames(mirrorCamera.BannerLine)));
                report.AppendLine(TryGetArrowPresses(out int right, out int up)
                    ? "Exact arrow presses for the selected mirror: " + LumiReactionLines.ArrowPresses(right, up)
                    : "Exact arrow presses: not known in this view. The CLOSE-UP view on the mirror camera shows them.");
            }

            if (safety != null)
            {
                report.Append("Back-reflection incidents: ").Append(safety.BackReflectionIncidents)
                      .Append(". All safety incidents that count for the stars: ").Append(safety.TotalIncidents).AppendLine(".");
            }
            return report.ToString();
        }

        /// <summary>
        /// What the player should do next, as one spoken sentence, worked out from the monitor and mirror camera.
        /// Empty when there is nothing to advise (no level, briefing open, holding, or finished).
        /// </summary>
        public string DescribeNextStep()
        {
            if (readout == null) return string.Empty;

            switch (readout.State)
            {
                case LaserMonitorReadout.ReadoutState.Briefing:
                case LaserMonitorReadout.ReadoutState.Holding:
                case LaserMonitorReadout.ReadoutState.Completed:
                    return string.Empty;
                case LaserMonitorReadout.ReadoutState.LaserOff:
                    return LumiReactionLines.LaserOff;
            }

            if (mirrorCamera != null && mirrorCamera.BannerLine.StartsWith("NO LASER REACHES", System.StringComparison.Ordinal))
                return LumiReactionLines.NoLaserOnMirror;
            if (TryGetArrowPresses(out int right, out int up)) return LumiReactionLines.ArrowPresses(right, up);

            switch (readout.State)
            {
                case LaserMonitorReadout.ReadoutState.Checkpoint: return NextCheckpointHint();
                case LaserMonitorReadout.ReadoutState.NoLight: return LumiReactionLines.NoLightOnDetector;
                case LaserMonitorReadout.ReadoutState.MissingBeam: return LumiReactionLines.MissingBeam;
                case LaserMonitorReadout.ReadoutState.NotCentred: return LumiReactionLines.NotCentred;
                default: return string.Empty;
            }
        }

        // The camera's button suggestion, for the selected mirror. Only known in its CLOSE-UP view, and not once on target.
        private bool TryGetArrowPresses(out int right, out int up)
        {
            right = up = 0;
            if (mirrorCamera == null || mirrorPanel == null || mirrorCamera.GoalCentred) return false;
            if (mirrorCamera.ShownMirror == null || mirrorCamera.ShownMirror != mirrorPanel.SelectedMirror) return false;
            if (!mirrorCamera.TryGetSuggestedSteps(out Vector2 steps)) return false;
            right = Mathf.RoundToInt(steps.x);
            up = Mathf.RoundToInt(steps.y);
            return right != 0 || up != 0;
        }

        private string NextCheckpointHint()
        {
            LaserLevel level = CurrentLevel;
            if (level == null) return string.Empty;
            for (int i = 0; i < level.Checkpoints.Length; i++)
            {
                LaserTarget checkpoint = level.Checkpoints[i];
                if (checkpoint == null || checkpoint.IsAligned) continue;
                string hint = level.CheckpointHint(i);
                return string.IsNullOrWhiteSpace(hint) ? "Steer the beam through the centre of " + level.CheckpointLabel(i) + "." : hint.Trim();
            }
            return string.Empty;
        }

        // The laser kit reports object names and calls every target a detector; use the names on the player's screens.
        private string UsePlayerNames(string text)
        {
            LaserLevel level = CurrentLevel;
            if (level == null || string.IsNullOrEmpty(text)) return text;
            for (int i = 0; i < level.Checkpoints.Length; i++)
            {
                if (level.Checkpoints[i] != null) text = Rename(text, level.Checkpoints[i].name, level.CheckpointLabel(i));
            }
            return level.Detector != null ? Rename(text, level.Detector.name, "Output detector") : text;
        }

        private static string Rename(string text, string objectName, string label) => text
            .Replace("Detector '" + objectName + "'", label)
            .Replace(objectName.ToUpperInvariant(), label.ToUpperInvariant());

        private static string ArrowsInWords(string text) => text
            .Replace("►", "right arrow").Replace("◄", "left arrow").Replace("▲", "up arrow").Replace("▼", "down arrow");

        /// <summary>The laser level being played, or null without a laser level.</summary>
        public LaserLevel CurrentLevel => levelManager != null ? levelManager.CurrentLevel : null;

        /// <summary>A mirror of the current level by its display name (case-insensitive), or null.</summary>
        public MirrorMount FindMirror(string displayName)
        {
            LaserLevel level = CurrentLevel;
            if (level == null || string.IsNullOrWhiteSpace(displayName)) return null;
            foreach (MirrorMount mirror in level.Mirrors)
            {
                if (mirror != null && string.Equals(mirror.DisplayName.Trim(), displayName.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    return mirror;
            }
            return null;
        }

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            lookAt = GetComponent<LumiLookAt>();
            tutor = GetComponent<LumiTutor>();
            listener = GetComponent<LumiListener>();
        }

        private void OnEnable()
        {
            stopSearchingAt = Time.time + searchSeconds;
            nextSearchAt = 0f;
        }

        private void OnDisable()
        {
            if (levelManager != null) levelManager.OnLevelStarted.RemoveListener(HandleLevelStarted);
            if (mirrorPanel != null) mirrorPanel.OnSelectionChanged.RemoveListener(HandleMirrorSelected);
            if (objective != null)
            {
                objective.OnCompleted.RemoveListener(HandleCompleted);
                objective.OnStarsAwarded.RemoveListener(HandleStarsAwarded);
            }
            if (safety != null)
            {
                safety.OnStrayLightStarted.RemoveListener(HandleSafetyProblem);
                safety.OnClippingStarted.RemoveListener(HandleSafetyProblem);
                safety.OnActuatorLimitReached.RemoveListener(HandleSafetyProblem);
                safety.OnWarningMessage.RemoveListener(HandleWarningMessage);
            }
            levelManager = null;
            mirrorPanel = null;
            objective = null;
            safety = null;
            readout = null;
            mirrorCamera = null;
            pendingExplanation = null;
            levelStartHandled = false;
        }

        private void Update()
        {
            if (Time.time >= nextSearchAt && Time.time < stopSearchingAt && !AllFound())
            {
                Connect();
                nextSearchAt = Time.time + 1f;
            }

            // Look back at the player once the mirror has had its moment.
            if (lookBackAt > 0f && Time.time >= lookBackAt)
            {
                if (lookAt != null && lookAt.Target == lookingAtMirror) lookAt.Target = null;
                lookingAtMirror = null;
                lookBackAt = -1f;
            }

            UpdateHoldListening();
            UpdateExplanation();
            UpdateStuckHint();
        }

        private bool AllFound() => levelManager && mirrorPanel && objective && safety && readout && mirrorCamera;

        private void Connect()
        {
            if (levelManager == null && (levelManager = FindFirstObjectByType<LaserLevelManager>()) != null)
                levelManager.OnLevelStarted.AddListener(HandleLevelStarted);

            if (mirrorPanel == null && (mirrorPanel = FindFirstObjectByType<MirrorControlPanel>()) != null)
                mirrorPanel.OnSelectionChanged.AddListener(HandleMirrorSelected);

            if (objective == null && (objective = FindFirstObjectByType<LaserAlignmentObjective>()) != null)
            {
                objective.OnCompleted.AddListener(HandleCompleted);
                objective.OnStarsAwarded.AddListener(HandleStarsAwarded);
            }

            if (readout == null) readout = FindFirstObjectByType<LaserMonitorReadout>();
            if (mirrorCamera == null) mirrorCamera = FindFirstObjectByType<LaserMirrorCamera>();

            if (safety == null && (safety = FindFirstObjectByType<LaserSafetyMonitor>()) != null)
            {
                safety.OnStrayLightStarted.AddListener(HandleSafetyProblem);
                safety.OnClippingStarted.AddListener(HandleSafetyProblem);
                safety.OnActuatorLimitReached.AddListener(HandleSafetyProblem);
                safety.OnWarningMessage.AddListener(HandleWarningMessage);
            }

            // The first level usually starts before Lumi connects, so its event has already been missed.
            if (!levelStartHandled && levelManager != null && levelManager.CurrentLevelIndex >= 0)
                HandleLevelStarted(levelManager.CurrentLevelIndex);
        }

        private void HandleLevelStarted(int levelIndex)
        {
            levelStartHandled = true;
            if (waveOnLevelStart) Trigger(LumiAnimatorParams.WaveId);

            // A new level replaces whatever Lumi was still explaining about the last one.
            if (tutor != null) tutor.StopExplaining();
            pendingExplanation = null;
            timerWasRunning = objective != null && objective.IsRunning;
            LastWarning = string.Empty; // a warning from the last level says nothing about this one
            LastWarningTime = float.NegativeInfinity;
            stuckStep = null;

            if (explainLevels && tutor != null && explainedLevels.Add(levelIndex))
            {
                pendingExplanation = LumiLevelIntros.For(levelManager != null ? levelManager.CurrentLevel : null, levelIndex);
                explainAt = Time.time + explainDelay;
            }
        }

        // Starts the waiting explanation when Lumi is free. Once the timer starts, the player is playing, so Lumi stops.
        private void UpdateExplanation()
        {
            if (tutor == null) return;

            bool timerRunning = objective != null && objective.IsRunning;
            bool timerJustStarted = timerRunning && !timerWasRunning;
            timerWasRunning = timerRunning;
            if (timerJustStarted)
            {
                tutor.StopExplaining();
                pendingExplanation = null;
                return;
            }

            if (pendingExplanation == null) return;
            if (listener != null && (listener.IsListening || listener.IsTranscribing))
            {
                pendingExplanation = null; // the player is already asking something
            }
            else if (Time.time >= explainAt && !tutor.IsBusy)
            {
                tutor.Explain(pendingExplanation);
                pendingExplanation = null;
            }
        }

        // Says a line if Lumi is free. An AI answer or a question being asked always goes first.
        private bool TrySay(string line)
        {
            if (tutor == null || string.IsNullOrEmpty(line) || tutor.IsBusy) return false;
            if (listener != null && (listener.IsListening || listener.IsTranscribing)) return false;
            tutor.Explain(line);
            return true;
        }

        // "Stuck" means the monitor has shown the same step, with the same targets aligned, for a long time.
        private void UpdateStuckHint()
        {
            bool playing = speakHints && readout != null && objective != null && objective.IsRunning && !objective.IsCompleted
                           && readout.State != LaserMonitorReadout.ReadoutState.Holding;
            if (!playing)
            {
                stuckStep = null;
                return;
            }

            string step = readout.StatusLine + "|" + objective.AlignedTargetCount();
            if (step != stuckStep)
            {
                stuckStep = step;
                stuckSince = Time.time;
                stuckWait = stuckSeconds;
                return;
            }

            if (Time.time - stuckSince < stuckWait) return;
            string hint = DescribeNextStep();
            if (!string.IsNullOrEmpty(hint) && TrySay(LumiReactionLines.HintOpening + hint))
            {
                stuckSince = Time.time;
                stuckWait *= 2f;
            }
        }

        private void HandleMirrorSelected(string mirrorName)
        {
            if (!lookAtSelectedMirror || lookAt == null || mirrorPanel == null || mirrorPanel.SelectedMirror == null) return;
            lookingAtMirror = mirrorPanel.SelectedMirror.transform;
            lookAt.Target = lookingAtMirror;
            lookBackAt = Time.time + mirrorLookSeconds;
        }

        private void HandleCompleted()
        {
            StopHoldListening();
            if (celebrateOnComplete) Trigger(LumiAnimatorParams.CelebrateId);
        }

        private void HandleStarsAwarded(int stars)
        {
            if (!speakWins || tutor == null || levelManager == null) return;
            tutor.StopExplaining(); // a win goes before a warning or hint that is still being said
            int levelKey = LaserBriefingTexts.LevelKey(levelManager.CurrentLevel, levelManager.CurrentLevelIndex);
            TrySay(LumiReactionLines.ForWin(stars, levelKey, levelManager.IsLastLevel));
        }

        private void HandleSafetyProblem()
        {
            if (!puzzledOnWarning || Time.time - lastPuzzledAt < puzzledCooldown) return;
            lastPuzzledAt = Time.time;
            Trigger(LumiAnimatorParams.PuzzledId);
        }

        private void HandleWarningMessage(string message)
        {
            LastWarning = message ?? string.Empty;
            LastWarningTime = Time.time;

            if (speakWarnings && objective != null && objective.IsRunning && Time.time - lastWarningSpokenAt >= spokenWarningCooldown
                && TrySay(LumiReactionLines.ForWarning(LastWarning)))
            {
                lastWarningSpokenAt = Time.time;
            }
        }

        // While the beam is held on target, Lumi watches intently (Listen). Only switches from Idle and
        // only undoes its own change, so other scripts (e.g. the AI's Think / Talk) are never overridden.
        private void UpdateHoldListening()
        {
            if (!listenWhileHolding || animator == null || objective == null) return;
            bool holding = objective.IsRunning && !objective.IsCompleted && objective.HoldProgress > 0f;
            var mode = (LumiMode)animator.GetInteger(LumiAnimatorParams.ModeId);

            if (holding && !listeningBecauseOfHold && mode == LumiMode.Idle)
            {
                animator.SetInteger(LumiAnimatorParams.ModeId, (int)LumiMode.Listen);
                listeningBecauseOfHold = true;
            }
            else if (!holding && listeningBecauseOfHold)
            {
                StopHoldListening();
            }
        }

        private void StopHoldListening()
        {
            if (!listeningBecauseOfHold || animator == null) return;
            if ((LumiMode)animator.GetInteger(LumiAnimatorParams.ModeId) == LumiMode.Listen)
                animator.SetInteger(LumiAnimatorParams.ModeId, (int)LumiMode.Idle);
            listeningBecauseOfHold = false;
        }

        // Test helpers: right-click the component header (or its ⋮ menu) in Play mode.
        [ContextMenu("Test: Objective Completed")]
        private void TestCompleted() => HandleCompleted();

        [ContextMenu("Test: Say Next Step")]
        private void TestNextStep() => TrySay(LumiReactionLines.HintOpening + DescribeNextStep());

        [ContextMenu("Test: Log Status Report")]
        private void TestStatusReport() => Debug.Log("[Lumi] What the AI is told:\n" + BuildStatusReport(), this);

        [ContextMenu("Test: Safety Warning")]
        private void TestWarning()
        {
            lastPuzzledAt = float.NegativeInfinity; // ignore the cooldown for the test
            HandleWarningMessage("Test warning: stray light is leaving the table.");
            HandleSafetyProblem();
        }

        private void Trigger(int triggerId)
        {
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetTrigger(triggerId);
        }
    }
}
