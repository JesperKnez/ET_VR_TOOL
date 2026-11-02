using EinsteinTelescope.Laser;
using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Makes Lumi react to the laser level, using events the laser kit already has (no changes to it):
    ///
    ///   level starts            → Wave (a greeting)
    ///   a mirror is selected    → look at that mirror for a moment, then back at the player
    ///   beam held on target     → Listen mood (watching intently) while the hold timer runs
    ///   objective completed     → Celebrate
    ///   safety warning          → Puzzled (with a cooldown; Lumi is never disappointed)
    ///
    /// It finds the laser objects in whatever scenes are loaded (and keeps looking for a few seconds,
    /// for scenes loaded additively), so nothing needs to be assigned. Without a laser level it does
    /// nothing. The latest warning text and the level's status report are kept for the AI tutor.
    /// Put it on the Lumi object (next to the Animator and LumiLookAt).
    /// </summary>
    [DisallowMultipleComponent]
    public class LumiLaserReactions : MonoBehaviour
    {
        [Header("Reactions")]
        [SerializeField] private bool waveOnLevelStart = true;
        [SerializeField] private bool lookAtSelectedMirror = true;
        [SerializeField, Range(0.5f, 6f)] private float mirrorLookSeconds = 2.5f;
        [SerializeField] private bool listenWhileHolding = true;
        [SerializeField] private bool celebrateOnComplete = true;
        [SerializeField] private bool puzzledOnWarning = true;
        [Tooltip("Minimum seconds between two Puzzled reactions, so Lumi doesn't nag.")]
        [SerializeField, Range(0f, 30f)] private float puzzledCooldown = 8f;

        [Tooltip("For how long (seconds) to keep looking for laser objects after Play starts.")]
        [SerializeField, Range(1f, 60f)] private float searchSeconds = 10f;

        private Animator animator;
        private LumiLookAt lookAt;
        private LaserLevelManager levelManager;
        private MirrorControlPanel mirrorPanel;
        private LaserAlignmentObjective objective;
        private LaserSafetyMonitor safety;

        private float nextSearchAt, stopSearchingAt;
        private float lookBackAt = -1f, lastPuzzledAt = float.NegativeInfinity;
        private Transform lookingAtMirror;
        private bool listeningBecauseOfHold;

        /// <summary>The most recent safety warning, in plain words (for the AI tutor). Empty if none yet.</summary>
        public string LastWarning { get; private set; } = string.Empty;

        /// <summary>Time.time of <see cref="LastWarning"/>, or negative infinity if there was none.</summary>
        public float LastWarningTime { get; private set; } = float.NegativeInfinity;

        /// <summary>True once the laser level's objective was found.</summary>
        public bool IsConnected => objective != null;

        /// <summary>A plain-text snapshot of the level for the AI tutor, or empty without a laser level.</summary>
        public string BuildStatusReport() => objective != null ? objective.BuildStatusReport() : string.Empty;

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            lookAt = GetComponent<LumiLookAt>();
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
            if (objective != null) objective.OnCompleted.RemoveListener(HandleCompleted);
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
        }

        private bool AllFound() => levelManager && mirrorPanel && objective && safety;

        private void Connect()
        {
            if (levelManager == null && (levelManager = FindFirstObjectByType<LaserLevelManager>()) != null)
                levelManager.OnLevelStarted.AddListener(HandleLevelStarted);

            if (mirrorPanel == null && (mirrorPanel = FindFirstObjectByType<MirrorControlPanel>()) != null)
                mirrorPanel.OnSelectionChanged.AddListener(HandleMirrorSelected);

            if (objective == null && (objective = FindFirstObjectByType<LaserAlignmentObjective>()) != null)
                objective.OnCompleted.AddListener(HandleCompleted);

            if (safety == null && (safety = FindFirstObjectByType<LaserSafetyMonitor>()) != null)
            {
                safety.OnStrayLightStarted.AddListener(HandleSafetyProblem);
                safety.OnClippingStarted.AddListener(HandleSafetyProblem);
                safety.OnActuatorLimitReached.AddListener(HandleSafetyProblem);
                safety.OnWarningMessage.AddListener(HandleWarningMessage);
            }
        }

        private void HandleLevelStarted(int levelIndex)
        {
            if (waveOnLevelStart) Trigger(LumiAnimatorParams.WaveId);
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
