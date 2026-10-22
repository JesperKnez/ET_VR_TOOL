using System.Collections.Generic;
using UnityEngine;

namespace EinsteinTelescope.Laser
{
    /// <summary>
    /// Sound and controller vibration for the laser puzzle. All sounds are generated in code, so no audio
    /// files are needed (swap in your own clips in the inspector if you like):
    /// click per mirror step, tick per button press, motor hum while a mirror keeps moving, warning buzz
    /// for stray light and clipping, a ding when an iris or detector becomes aligned and a chime when the
    /// level is completed. Optional alignment tone: it beeps faster and higher as the beam gets closer to the
    /// centre of the next target, like the audio monitors technicians use while aligning by eye.
    /// </summary>
    [DefaultExecutionOrder(LaserExecutionOrder.GameLogic + 20)]
    public class LaserAudioFeedback : MonoBehaviour
    {
        private const int SampleRate = 44100;

        [Header("Sources")]
        [SerializeField] private LaserAlignmentObjective objective = null;
        [SerializeField] private LaserSafetyMonitor safetyMonitor = null;

        [Header("Volume")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float toneVolume = 0.25f;

        [Tooltip("Beeps faster and higher as the beam nears the centre of the next iris or detector.")]
        [SerializeField] private bool alignmentTone = true;

        [Tooltip("Vibrate the XR controllers together with the sounds.")]
        [SerializeField] private bool haptics = true;

        [Header("Optional clips (empty = generated)")]
        [SerializeField] private AudioClip stepClip = null;
        [SerializeField] private AudioClip pressClip = null;
        [SerializeField] private AudioClip warningClip = null;
        [SerializeField] private AudioClip alignedClip = null;
        [SerializeField] private AudioClip completeClip = null;

        private AudioSource m_Effects;
        private AudioSource m_Tone;
        private AudioSource m_Hum;
        private int m_LastAlignedCount;
        private float m_BeepPhase;
        private float m_LastPressSound = -1f;

        public bool AlignmentToneEnabled => alignmentTone;

        /// <summary>Pitch (Hz) the alignment tone currently aims for, 0 = silent. For tests and debugging.</summary>
        public float ToneFrequency { get; private set; }

        /// <summary>Number of sounds played so far, per kind. For tests and debugging.</summary>
        public readonly Dictionary<string, int> PlayCount = new Dictionary<string, int>();

        public void SetAlignmentTone(bool enabled) => alignmentTone = enabled;
        public void ToggleAlignmentTone() => alignmentTone = !alignmentTone;
        public void SetHaptics(bool enabled) => haptics = enabled;

        private void Awake()
        {
            m_Effects = CreateSource("Effects", false);
            m_Tone = CreateSource("Alignment Tone", true);
            m_Hum = CreateSource("Motor Hum", true);

            stepClip = stepClip != null ? stepClip : MakeClick("Step Click", 0.03f, 2400f, 0.9f);
            pressClip = pressClip != null ? pressClip : MakeClick("Button Tick", 0.02f, 1500f, 0.6f);
            warningClip = warningClip != null ? warningClip : MakeWarning();
            alignedClip = alignedClip != null ? alignedClip : MakeBell("Aligned Ding", new[] { 1318.5f }, 0.45f, 0f);
            completeClip = completeClip != null ? completeClip : MakeBell("Complete Chime", new[] { 523.3f, 659.3f, 784f, 1046.5f }, 0.9f, 0.11f);
            m_Tone.clip = MakeLoop("Tone 440", new[] { 440f }, new[] { 1f });
            m_Hum.clip = MakeLoop("Motor Hum", new[] { 110f, 220f, 330f }, new[] { 0.6f, 0.3f, 0.15f });
            m_Tone.volume = 0f;
            m_Hum.volume = 0f;
        }

        private void OnEnable()
        {
            LaserUiFeedback.Pressed += HandlePressed;
            MirrorMount.AnyStepped += HandleStepped;
            if (objective != null) objective.OnCompleted.AddListener(HandleCompleted);
            if (safetyMonitor != null)
            {
                safetyMonitor.OnStrayLightStarted.AddListener(HandleWarning);
                safetyMonitor.OnClippingStarted.AddListener(HandleWarning);
            }
        }

        private void OnDisable()
        {
            LaserUiFeedback.Pressed -= HandlePressed;
            MirrorMount.AnyStepped -= HandleStepped;
            if (objective != null) objective.OnCompleted.RemoveListener(HandleCompleted);
            if (safetyMonitor != null)
            {
                safetyMonitor.OnStrayLightStarted.RemoveListener(HandleWarning);
                safetyMonitor.OnClippingStarted.RemoveListener(HandleWarning);
            }

            if (m_Tone != null) m_Tone.Stop();
            if (m_Hum != null) m_Hum.Stop();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateHum(dt);
            UpdateAlignmentTone(dt);
            UpdateAlignedDing();
        }

        // ----- Events -----------------------------------------------------------------------------

        private void HandlePressed()
        {
            m_LastPressSound = Time.unscaledTime;
            Play("press", pressClip, 0.5f);
            Vibrate(0.25f, 0.03f);
        }

        private void HandleStepped(MirrorMount mirror)
        {
            // A tap on a pad arrow is already a button press: no second click in the same frame.
            if (Time.unscaledTime - m_LastPressSound < 0.005f)
            {
                return;
            }

            Play("step", stepClip, 0.7f);
            Vibrate(0.15f, 0.02f);
        }

        private void HandleWarning()
        {
            Play("warning", warningClip, 0.8f);
            Vibrate(0.7f, 0.25f);
        }

        private void HandleCompleted()
        {
            Play("complete", completeClip, 1f);
            Vibrate(0.8f, 0.4f);
        }

        private void UpdateAlignedDing()
        {
            int aligned = objective != null && objective.IsRunning ? objective.AlignedTargetCount() : 0;
            if (aligned > m_LastAlignedCount && objective != null && !objective.IsCompleted)
            {
                Play("aligned", alignedClip, 0.6f);
                Vibrate(0.4f, 0.1f);
            }

            m_LastAlignedCount = aligned;
        }

        private void UpdateHum(float dt)
        {
            bool moving = false;
            IReadOnlyList<MirrorMount> mounts = MirrorMount.ActiveMounts;
            for (int i = 0; i < mounts.Count && !moving; i++)
            {
                moving = mounts[i] != null && mounts[i].IsHoldMoving;
            }

            float humVolume = 0.18f * masterVolume;
            Fade(m_Hum, moving ? humVolume : 0f, dt, 0.15f, humVolume);
        }

        /// <summary>
        /// The first target of the objective that is not aligned yet (irises in order, then the detector)
        /// sets the tone: closer to its centre = higher pitch and faster beeps; aligned = steady tone.
        /// </summary>
        private void UpdateAlignmentTone(float dt)
        {
            LaserTarget target = alignmentTone && objective != null && objective.IsRunning ? NextTarget() : null;
            if (target == null || !target.IsIlluminated)
            {
                ToneFrequency = 0f;
                Fade(m_Tone, 0f, dt, 0.05f, toneVolume * masterVolume);
                return;
            }

            float error = Mathf.Clamp01(target.WorstBeamOffset / Mathf.Max(target.DetectorRadius, 1e-4f));
            ToneFrequency = target.IsAligned ? 990f : Mathf.Lerp(880f, 330f, error);
            m_Tone.pitch = ToneFrequency / 440f;

            bool on;
            if (target.IsAligned)
            {
                on = true;
            }
            else
            {
                float period = Mathf.Lerp(0.12f, 0.6f, error);
                m_BeepPhase = (m_BeepPhase + dt / period) % 1f;
                on = m_BeepPhase < 0.5f;
            }

            Fade(m_Tone, on ? toneVolume * masterVolume : 0f, dt, 0.015f, toneVolume * masterVolume);
        }

        private LaserTarget NextTarget()
        {
            IReadOnlyList<LaserTarget> targets = objective.RequiredTargets;
            LaserTarget last = null;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] == null)
                {
                    continue;
                }

                last = targets[i];
                if (!targets[i].IsAligned)
                {
                    return targets[i];
                }
            }

            return last;
        }

        // ----- Helpers ----------------------------------------------------------------------------

        private void Play(string kind, AudioClip clip, float volume)
        {
            PlayCount[kind] = PlayCount.TryGetValue(kind, out int count) ? count + 1 : 1;
            if (clip != null && m_Effects != null && masterVolume > 0f)
            {
                m_Effects.PlayOneShot(clip, volume * masterVolume);
            }
        }

        private void Vibrate(float amplitude, float duration)
        {
            if (haptics)
            {
                LaserHaptics.Pulse(amplitude, duration);
            }
        }

        /// <summary>Ramps the volume (no clicks) and starts / stops the looping source.</summary>
        private static void Fade(AudioSource source, float target, float dt, float rampSeconds, float fullVolume)
        {
            if (source == null)
            {
                return;
            }

            source.volume = Mathf.MoveTowards(source.volume, target, Mathf.Max(fullVolume, 0.01f) / rampSeconds * dt);
            if (source.volume > 0f && !source.isPlaying)
            {
                source.Play();
            }
            else if (source.volume <= 0f && source.isPlaying)
            {
                source.Stop();
            }
        }

        private AudioSource CreateSource(string name, bool loop)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;   // 2D: the control-room speakers
            return source;
        }

        private static AudioClip MakeClick(string name, float seconds, float frequency, float noise)
        {
            int count = Mathf.Max(1, (int)(seconds * SampleRate));
            var data = new float[count];
            var random = new System.Random(7);
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float envelope = Mathf.Exp(-t / (seconds * 0.22f));
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float hiss = (float)(random.NextDouble() * 2.0 - 1.0) * noise * Mathf.Exp(-t / 0.002f);
                data[i] = 0.6f * envelope * (tone + hiss);
            }

            return Clip(name, data);
        }

        private static AudioClip MakeWarning()
        {
            // Two short low buzzes (square wave), like an interlock alarm.
            const float buzz = 0.13f, gap = 0.07f;
            int count = (int)((2f * buzz + gap) * SampleRate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                bool inBuzz = t < buzz || (t > buzz + gap && t < 2f * buzz + gap);
                if (!inBuzz)
                {
                    continue;
                }

                float local = t < buzz ? t : t - buzz - gap;
                float edge = Mathf.Clamp01(local / 0.005f) * Mathf.Clamp01((buzz - local) / 0.005f);
                float square = Mathf.Sin(2f * Mathf.PI * 220f * t) >= 0f ? 1f : -1f;
                data[i] = 0.35f * edge * square;
            }

            return Clip("Warning Buzz", data);
        }

        private static AudioClip MakeBell(string name, float[] notes, float seconds, float noteDelay)
        {
            int count = (int)((seconds + noteDelay * notes.Length) * SampleRate);
            var data = new float[count];
            for (int n = 0; n < notes.Length; n++)
            {
                int start = (int)(n * noteDelay * SampleRate);
                for (int i = start; i < count; i++)
                {
                    float t = (float)(i - start) / SampleRate;
                    float envelope = Mathf.Exp(-t / (seconds * 0.3f)) * Mathf.Clamp01(t / 0.004f);
                    float f = notes[n];
                    data[i] += 0.3f * envelope * (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * f * t));
                }
            }

            return Clip(name, data);
        }

        /// <summary>One second of whole cycles, so it loops without a click.</summary>
        private static AudioClip MakeLoop(string name, float[] frequencies, float[] amplitudes)
        {
            var data = new float[SampleRate];
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / SampleRate;
                float sum = 0f;
                for (int k = 0; k < frequencies.Length; k++)
                {
                    sum += amplitudes[k] * Mathf.Sin(2f * Mathf.PI * frequencies[k] * t);
                }

                data[i] = 0.5f * sum;
            }

            return Clip(name, data);
        }

        private static AudioClip Clip(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
