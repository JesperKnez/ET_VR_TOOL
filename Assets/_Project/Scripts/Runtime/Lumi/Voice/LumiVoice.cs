using System;
using System.Threading;
using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Lumi's voice: turns the tutor's answers into speech (Azure AI Speech) and plays them as 3D sound
    /// from Lumi's head, so in VR the voice comes from where Lumi is.
    ///
    /// LumiTutor uses it automatically when it is on the same object: the audio is prepared while Lumi
    /// still "thinks", and the Talk pose lasts exactly as long as the speech. If the voice service fails
    /// (no key, no network), Lumi stays silent and everything else carries on.
    /// <see cref="Loudness"/> (0..1) follows the speech, for lip sync on the mouth screen later.
    /// Put it on the Lumi object.
    /// </summary>
    [DisallowMultipleComponent]
    public class LumiVoice : MonoBehaviour
    {
        [Tooltip("Azure key, region and voice (keep it in the git-ignored secrets folder). Empty = silent.")]
        [SerializeField] private LumiVoiceConfig config;

        [Header("Sound")]
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [Tooltip("Full volume up to this distance (m); quieter further away.")]
        [SerializeField, Range(0.2f, 5f)] private float fullVolumeDistance = 1.5f;
        [Tooltip("Silent beyond this distance (m).")]
        [SerializeField, Range(2f, 30f)] private float maxDistance = 12f;

        private AudioSource source;
        private ILumiTextToSpeech tts;
        private readonly float[] meterBuffer = new float[256];

        /// <summary>True if a voice service is configured.</summary>
        public bool IsAvailable => tts != null;

        /// <summary>Why the last line couldn't be spoken, if it couldn't (for the debug window).</summary>
        public string LastProblem { get; private set; } = string.Empty;

        /// <summary>How loud Lumi is speaking right now, 0..1 (for lip sync later).</summary>
        public float Loudness { get; private set; }

        /// <summary>True while speech is playing.</summary>
        public bool IsSpeaking => source != null && source.isPlaying;

        private void Awake()
        {
            tts = config != null && config.HasKey ? new AzureTextToSpeech(config) : null;

            // The voice comes from the head (it follows nods and turns); falls back to Lumi itself.
            Transform head = null;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "lens" || (head == null && t.name == "head")) head = t;
            }
            var speaker = new GameObject("Lumi Voice");
            speaker.transform.SetParent(head != null ? head : transform, false);

            source = speaker.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f; // 3D: louder and directional from Lumi's position
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = fullVolumeDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.volume = volume;
        }

        /// <summary>
        /// Turns text into audio, ready to play. Returns null (and sets <see cref="LastProblem"/>)
        /// when the voice service is missing or fails, so callers can carry on silently.
        /// </summary>
        public async Awaitable<AudioClip> PrepareAsync(string text, CancellationToken cancellation)
        {
            LastProblem = string.Empty;
            if (tts == null)
            {
                LastProblem = "no voice config with an Azure key";
                return null;
            }
            try
            {
                return await tts.SynthesizeAsync(text, cancellation);
            }
            catch (LumiVoiceException e)
            {
                LastProblem = e.Message;
                Debug.LogWarning("[Lumi] " + e.Message + " Lumi stays silent for this answer.", this);
                return null;
            }
        }

        /// <summary>Plays prepared speech (stops anything Lumi was still saying).</summary>
        public void Play(AudioClip clip)
        {
            if (clip == null) return;
            if (source.clip != null && source.clip != clip) Destroy(source.clip); // free the previous line's audio
            source.Stop();
            source.clip = clip;
            source.Play();
        }

        /// <summary>Stops talking immediately.</summary>
        public void Stop()
        {
            if (source != null) source.Stop();
        }

        private void Update()
        {
            if (!IsSpeaking)
            {
                Loudness = Mathf.MoveTowards(Loudness, 0f, Time.deltaTime * 8f);
                return;
            }
            source.GetOutputData(meterBuffer, 0);
            float sum = 0f;
            foreach (float s in meterBuffer) sum += s * s;
            float rms = Mathf.Sqrt(sum / meterBuffer.Length);
            Loudness = Mathf.Lerp(Loudness, Mathf.Clamp01(rms * 6f), 0.5f); // speech RMS is small; scale it up
        }

        private void OnDestroy()
        {
            if (source != null && source.clip != null) Destroy(source.clip);
        }
    }
}
