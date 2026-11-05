using System;
using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Runs a series of <see cref="LaserLevel"/>s in one scene. Loading a level activates its optics,
    /// hands its mirrors to the <see cref="MirrorControlPanel"/>, its irises and detector to the
    /// <see cref="LaserAlignmentObjective"/> and points the monitor, control pad and keyboard at its laser.
    ///
    /// When a level is completed: the mirrors are locked, the time and stars are saved as personal best
    /// (<see cref="LaserBestTimes"/>) and <see cref="LevelCompleted"/> is raised with a <see cref="LaserLevelResult"/>.
    /// In Room_Scene the curved monitor shows it (<see cref="LaserMonitorResults"/>) with NEXT LEVEL and RETRY.
    /// Without any results screen the next level loads automatically after a short pause.
    /// Teammates: call <see cref="NextLevel"/>, <see cref="RestartLevel"/> or <see cref="LoadLevel"/> from any button.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic - 20)]
    public class LaserLevelManager : MonoBehaviour
    {
        [SerializeField] private LaserLevel[] levels = new LaserLevel[0];

        [Tooltip("Level loaded when Play starts (0 = level 1).")]
        [SerializeField, Min(0)] private int startLevel = 0;

        [Tooltip("Load the next level automatically after a level is completed (only when no results screen waits for the player).")]
        [SerializeField] private bool autoAdvance = true;

        [Tooltip("Seconds between completing a level and loading the next one.")]
        [SerializeField, Min(0f)] private float advanceDelaySeconds = 5f;

        [Header("Scene systems (filled in by the demo builder)")]
        [SerializeField] private MirrorControlPanel controlPanel = null;
        [SerializeField] private LaserAlignmentObjective objective = null;
        [SerializeField] private LaserTargetDisplay targetDisplay = null;
        [SerializeField] private LaserMonitorReadout readout = null;
        [SerializeField] private LaserControlPad controlPad = null;
        [SerializeField] private LaserDebugKeyboard debugKeyboard = null;

        [Tooltip("Optional briefing screen. The level timer starts when the player closes it or first moves a mirror.")]
        [SerializeField] private LaserBriefingPanel briefing = null;
        [SerializeField] private bool showBriefingOnLevelStart = true;

        [Tooltip("Optional floating results card shown when a level is completed.")]
        [SerializeField] private LaserResultsCard resultsCard = null;

        [Tooltip("Also show the floating results card when another results screen (the curved monitor) is present. " +
                 "Off: the card is only used when nothing else shows the results.")]
        [SerializeField] private bool showFloatingResultsCard = false;

        [Tooltip("Optional: incident breakdown on the results screens.")]
        [SerializeField] private LaserSafetyMonitor safetyMonitor = null;

        [Tooltip("Optional beam-camera screen: shows the irises of the current level (or the detector).")]
        [SerializeField] private LaserBeamCameraScreen beamCameras = null;

        [Header("Events")]
        [Tooltip("Index of the level that just started (0 = level 1).")]
        [SerializeField] private UnityEvent<int> onLevelStarted = new UnityEvent<int>();
        [SerializeField] private UnityEvent onAllLevelsCompleted = new UnityEvent();

        private int m_Current = -1;
        private float m_AdvanceAt = -1f;
        private bool m_CurrentCompleted;
        private LaserLevelResult m_LastResult;
        private bool m_HasLastResult;

        public int CurrentLevelIndex => m_Current;
        public int LevelCount => levels.Length;
        public LaserLevel CurrentLevel => m_Current >= 0 && m_Current < levels.Length ? levels[m_Current] : null;
        public string CurrentTitle => CurrentLevel != null ? CurrentLevel.Title : string.Empty;
        public bool IsLastLevel => m_Current == levels.Length - 1;

        /// <summary>True after the current level was completed, until another level (or the same one again) loads.</summary>
        public bool IsCurrentLevelCompleted => m_CurrentCompleted;

        /// <summary>The result of the last completed level (valid when <see cref="HasLastResult"/>).</summary>
        public LaserLevelResult LastResult => m_LastResult;
        public bool HasLastResult => m_HasLastResult;

        /// <summary>Seconds until the next level loads, or -1 when nothing is scheduled.</summary>
        public float SecondsUntilNextLevel => m_AdvanceAt < 0f ? -1f : Mathf.Max(0f, m_AdvanceAt - Time.time);

        public UnityEvent<int> OnLevelStarted => onLevelStarted;
        public UnityEvent OnAllLevelsCompleted => onAllLevelsCompleted;

        /// <summary>
        /// Raised (C#) when a level is completed, after the personal best is saved. A results screen that subscribes
        /// makes the manager wait for the player (NEXT LEVEL / RETRY) instead of advancing by itself.
        /// </summary>
        public event Action<LaserLevelResult> LevelCompleted;

        /// <summary>The level at an index, or null.</summary>
        public LaserLevel GetLevel(int index) => index >= 0 && index < levels.Length ? levels[index] : null;

        /// <summary>Load a level (0-based). Its mirrors start at their misaligned start angles.</summary>
        public void LoadLevel(int index) => Load(index, true);

        private void Load(int index, bool resetMirrors)
        {
            if (levels.Length == 0)
            {
                return;
            }

            index = ((index % levels.Length) + levels.Length) % levels.Length;
            LaserEmitter previousLaser = CurrentLevel != null ? CurrentLevel.Laser : null;
            float wavelength = previousLaser != null ? previousLaser.WavelengthNm : -1f;
            bool shutterOpen = previousLaser == null || previousLaser.IsShutterOpen;

            m_Current = index;
            m_AdvanceAt = -1f;
            m_CurrentCompleted = false;
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] != null && i != index)
                {
                    SetMirrorsLocked(levels[i], false);
                    levels[i].gameObject.SetActive(false);
                }
            }

            LaserLevel level = levels[index];
            if (level == null)
            {
                Debug.LogError("[LaserLevelManager] Level slot " + (index + 1) + " is empty.", this);
                return;
            }

            level.gameObject.SetActive(true);
            SetMirrorsLocked(level, false);

            foreach (MirrorMount mirror in level.Mirrors)
            {
                if (mirror != null && resetMirrors)
                {
                    mirror.ResetToStart();
                }
            }

            LaserEmitter laser = level.Laser;
            if (laser != null)
            {
                if (wavelength > 0f)
                {
                    laser.SetWavelength(wavelength);
                }

                laser.SetShutter(shutterOpen);
            }

            if (controlPanel != null) controlPanel.SetMirrors(level.Mirrors);
            if (targetDisplay != null) targetDisplay.SetTarget(level.Detector);
            if (controlPad != null) controlPad.SetLaser(laser);
            if (debugKeyboard != null) debugKeyboard.SetLaser(laser);
            bool brief = briefing != null && showBriefingOnLevelStart;
            if (objective != null)
            {
                // Targets and star rules first: the briefing and the monitor read them.
                LaserTarget[] targets = level.AllTargets();
                objective.SetRequiredTargets(targets);
                objective.SetParTime(level.ParTimeSeconds);
                objective.ResetObjective();
                if (targets.Length == 0)
                {
                    Debug.LogWarning("[LaserLevelManager] Level '" + level.Title + "' has no detector or checkpoints: it can never be completed.", level);
                }

                if (!brief)
                {
                    objective.StartObjective();
                }
                // With a briefing the timer starts when it is closed or when the player first moves a mirror.
            }

            if (readout != null) readout.SetLevel(level, index, levels.Length);

            if (resultsCard != null)
            {
                resultsCard.Hide();
            }

            if (beamCameras != null)
            {
                bool irises = level.Checkpoints.Length > 0;
                var cameraTargets = irises ? level.Checkpoints : new[] { level.Detector };
                var cameraLabels = new string[cameraTargets.Length];
                for (int i = 0; i < cameraLabels.Length; i++)
                {
                    cameraLabels[i] = irises ? level.CheckpointLabel(i) : "Output detector";
                }

                beamCameras.SetTargets(cameraTargets, cameraLabels);
            }

            if (brief)
            {
                briefing.Show(level, index, levels.Length);
            }

            onLevelStarted.Invoke(index);
        }

        /// <summary>True when the next level waits for the player (a results screen), instead of loading by itself.</summary>
        public bool WaitsForPlayer => LevelCompleted != null || ShowsFloatingCard;

        private bool ShowsFloatingCard => resultsCard != null && (showFloatingResultsCard || LevelCompleted == null);

        /// <summary>Unique key of a level for the saved personal bests.</summary>
        public string LevelKey(int index) => "L" + (index + 1) + "_" + (levels[index] != null ? levels[index].Title : "Level");

        /// <summary>True while the briefing screen is open.</summary>
        public bool IsBriefingOpen => briefing != null && briefing.IsOpen;

        /// <summary>Open the briefing of the current level again (HELP button), or close it when it is open.</summary>
        public void ToggleBriefing()
        {
            if (briefing == null)
            {
                return;
            }

            if (briefing.IsOpen)
            {
                briefing.Close();
            }
            else if (CurrentLevel != null)
            {
                briefing.Open(0);
            }
        }

        /// <summary>Next level; after the last level this wraps around to level 1.</summary>
        public void NextLevel() => LoadLevel(m_Current + 1);
        public void PreviousLevel() => LoadLevel(m_Current - 1);
        public void RestartLevel() => LoadLevel(Mathf.Max(0, m_Current));

        /// <summary>Back to level 1 (button on the "all levels complete" screen).</summary>
        public void RestartFromFirstLevel() => LoadLevel(0);

        /// <summary>Best stars summed over all levels (saved between sessions).</summary>
        public int TotalBestStars()
        {
            int total = 0;
            for (int i = 0; i < levels.Length; i++)
            {
                total += LaserBestTimes.BestStars(LevelKey(i));
            }

            return total;
        }

        private void OnEnable()
        {
            if (objective != null)
            {
                objective.OnCompleted.AddListener(HandleCompleted);
            }

            if (briefing != null)
            {
                briefing.OnClosed.AddListener(HandleBriefingClosed);
            }

            if (resultsCard != null)
            {
                resultsCard.OnNext.AddListener(NextLevel);
                resultsCard.OnRetry.AddListener(RestartLevel);
            }
        }

        private void HandleBriefingClosed()
        {
            // First close of a level's briefing starts the timer; reopening it later (HELP) changes nothing.
            if (objective != null && objective.IsWaitingToStart)
            {
                objective.StartObjective();
            }
        }

        private void OnDisable()
        {
            if (objective != null)
            {
                objective.OnCompleted.RemoveListener(HandleCompleted);
            }

            if (briefing != null)
            {
                briefing.OnClosed.RemoveListener(HandleBriefingClosed);
            }

            if (resultsCard != null)
            {
                resultsCard.OnNext.RemoveListener(NextLevel);
                resultsCard.OnRetry.RemoveListener(RestartLevel);
            }
        }

        private void Start()
        {
            // The mirrors already start at their start angles (MirrorMount.Awake), so they are not reset here:
            // other scripts may have moved them before the first frame.
            Load(Mathf.Clamp(startLevel, 0, Mathf.Max(0, levels.Length - 1)), false);
        }

        private LaserLevelResult BuildAndSaveResult()
        {
            string key = LevelKey(m_Current);
            bool hadBest = LaserBestTimes.TryGet(key, out float previousBest, out _);
            float seconds = objective.ElapsedSeconds;
            bool newBest = LaserBestTimes.Submit(key, seconds, objective.Stars);
            int bestStars = LaserBestTimes.BestStars(key);
            LaserLevel next = IsLastLevel ? null : GetLevel(m_Current + 1);

            return new LaserLevelResult
            {
                LevelIndex = m_Current,
                LevelCount = levels.Length,
                Title = CurrentLevel.Title,
                Seconds = seconds,
                Stars = objective.Stars,
                Incidents = objective.Mistakes,
                StrayLight = safetyMonitor != null ? safetyMonitor.StrayLightIncidents : 0,
                Clipping = safetyMonitor != null ? safetyMonitor.ClippingIncidents : 0,
                ActuatorLimits = safetyMonitor != null ? safetyMonitor.ActuatorLimitIncidents : 0,
                BackReflection = safetyMonitor != null ? safetyMonitor.BackReflectionIncidents : 0,
                HadPreviousBest = hadBest,
                PreviousBestSeconds = hadBest ? previousBest : seconds,
                BestStars = bestStars,
                IsNewBest = newBest,
                IsLastLevel = IsLastLevel,
                Completions = LaserBestTimes.Completions(key),
                ParTimeSeconds = objective.ParTimeSeconds,
                ThreeStarMistakeLimit = objective.ThreeStarMistakeLimit,
                TwoStarMistakeLimit = objective.TwoStarMistakeLimit,
                StarRules = objective.DescribeStarRules(),
                NextTitle = next != null ? next.Title : string.Empty,
                TotalBestStars = TotalBestStars(),
                MaxTotalStars = levels.Length * 3
            };
        }

        private void Update()
        {
            if (m_AdvanceAt >= 0f && Time.time >= m_AdvanceAt)
            {
                m_AdvanceAt = -1f;
                NextLevel();
            }
        }

        private void HandleCompleted()
        {
            if (m_CurrentCompleted || CurrentLevel == null || objective == null)
            {
                return;   // already handled (the objective cannot complete twice, but be safe)
            }

            m_CurrentCompleted = true;

            // Freeze the result: the player cannot knock the beams off any more until the next level or a retry.
            if (controlPanel != null)
            {
                controlPanel.StopHoldMove();
            }

            SetMirrorsLocked(CurrentLevel, true);

            m_LastResult = BuildAndSaveResult();
            m_HasLastResult = true;
            Debug.Log("[LaserLevelManager] Level " + (m_Current + 1) + " '" + CurrentTitle + "' completed in " +
                      LaserBestTimes.FormatTime(m_LastResult.Seconds) + " with " + m_LastResult.Incidents + " incident(s): " +
                      m_LastResult.Stars + " star(s).", this);

            bool showCard = ShowsFloatingCard;
            LevelCompleted?.Invoke(m_LastResult);
            if (showCard)
            {
                resultsCard.Show(m_LastResult);
            }

            if (IsLastLevel)
            {
                onAllLevelsCompleted.Invoke();
                return;
            }

            if (autoAdvance && !WaitsForPlayer)
            {
                m_AdvanceAt = Time.time + advanceDelaySeconds;
            }
        }

        private static void SetMirrorsLocked(LaserLevel level, bool locked)
        {
            if (level == null)
            {
                return;
            }

            foreach (MirrorMount mirror in level.Mirrors)
            {
                if (mirror != null)
                {
                    mirror.InputLocked = locked;
                }
            }
        }
    }
}
