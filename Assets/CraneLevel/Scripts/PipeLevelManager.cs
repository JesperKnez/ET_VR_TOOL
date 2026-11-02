using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ETVRTool.CraneLevel
{
    [Serializable]
    public class PipeStep
    {
        public string title;
        [TextArea] public string instruction;
        public PipeSection pipe;
        public PipeAlignmentTarget target;
    }

    /// <summary>
    /// Runs the "align the beam pipes" level: one pipe at a time, counts collisions and time,
    /// and writes everything to the instruction board next to the control panel.
    /// </summary>
    public class PipeLevelManager : MonoBehaviour
    {
        public BridgeCrane crane;
        public CraneHook hook;
        public List<PipeStep> steps = new List<PipeStep>();

        [Header("Instruction board")]
        public Text titleText;
        public Text bodyText;
        public Text statusText;
        public Text messageText;
        public float messageSeconds = 4f;

        int _current = -1;
        int _bumps;
        float _startTime;
        float _endTime = -1f;
        float _messageUntil;

        public bool Finished => _endTime >= 0f;

        void Start()
        {
            _startTime = Time.time;

            if (hook != null)
            {
                hook.Message += ShowMessage;
                hook.LoadAttached += p => ShowMessage(p == CurrentStep?.pipe
                    ? "Load attached. Lift it clear of everything before travelling."
                    : "That is not the pipe for this step - set it down again.");
            }
            if (crane != null)
                crane.EmergencyStopChanged += stopped =>
                    ShowMessage(stopped ? "EMERGENCY STOP - all movement stopped." : "Emergency stop reset.");

            foreach (var s in steps)
            {
                if (s.pipe != null) s.pipe.Bumped += OnBumped;
                if (s.target != null) { s.target.SetActive(false); s.target.Completed += OnTargetCompleted; }
            }

            if (messageText != null) messageText.text = "";
            NextStep();
        }

        PipeStep CurrentStep => _current >= 0 && _current < steps.Count ? steps[_current] : null;

        void NextStep()
        {
            _current++;
            if (_current >= steps.Count) { Finish(); return; }

            var step = steps[_current];
            if (step.target != null) step.target.SetActive(true);
            if (titleText != null) titleText.text = $"Step {_current + 1}/{steps.Count}: {step.title}";
            if (bodyText != null) bodyText.text = step.instruction;
        }

        void OnTargetCompleted(PipeAlignmentTarget target, PipeSection pipe)
        {
            ShowMessage("Pipe aligned and bolted. Good job!");
            Invoke(nameof(NextStep), 1.5f);
        }

        void OnBumped(PipeSection pipe, Collider other)
        {
            _bumps++;
            ShowMessage("COLLISION! The load hit " + other.transform.root.name + ". Slow down and lift higher.");
        }

        void Finish()
        {
            _endTime = Time.time;
            float t = _endTime - _startTime;
            int stars = _bumps == 0 ? 3 : _bumps <= 2 ? 2 : 1;
            if (titleText != null) titleText.text = "Level complete!";
            if (bodyText != null)
                bodyText.text = $"All {steps.Count} pipes are aligned.\n\n" +
                                $"Time: {t / 60f:0}:{t % 60f:00}\nCollisions: {_bumps}\n" +
                                $"Rating: {new string('*', stars)}{new string('-', 3 - stars)}\n\n" +
                                "Press RESTART on the panel to try again.";
        }

        public void ShowMessage(string msg)
        {
            if (messageText == null) return;
            messageText.text = msg;
            _messageUntil = Time.time + messageSeconds;
        }

        // The scene has to be in Build Settings (the builder adds it).
        public void RestartLevel() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);

        void Update()
        {
            if (messageText != null && Time.time > _messageUntil) messageText.text = "";
            if (statusText == null) return;

            float t = (Finished ? _endTime : Time.time) - _startTime;
            string status = $"Time {t / 60f:0}:{t % 60f:00}    Collisions {_bumps}";
            if (crane != null)
            {
                if (crane.EmergencyStopped) status += "    E-STOP ACTIVE";
                if (crane.CreepMode) status += "    CREEP SPEED";
            }
            var step = CurrentStep;
            if (!Finished && step != null && step.target != null && step.target.IsActive)
                status += "\n" + step.target.ReadoutText;
            statusText.text = status;
        }
    }
}
