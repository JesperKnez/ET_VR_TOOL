using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Lumi's ears: push to talk, and the question goes to LumiTutor.
    ///
    /// 1. While the talk button is held (A on the right controller), Lumi shows the Listen pose and records.
    ///    A + grip stays free for the laser's fine/coarse switch, so holding grip doesn't start listening.
    /// 2. On release, Lumi shows the Think pose and Whisper turns the recording into text on the headset.
    ///    The recording is thrown away right after; it is never saved or sent anywhere.
    /// 3. The text goes to <see cref="LumiTutor.Ask"/>. If nothing was understood, Lumi looks puzzled.
    ///
    /// Put it on the Lumi object next to LumiTutor.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LumiTutor))]
    public class LumiListener : MonoBehaviour
    {
        [Header("Whisper")]
        [Tooltip("Model file in Assets/StreamingAssets/Whisper.")]
        [SerializeField] private string modelFile = "ggml-tiny.en.bin";
        [Tooltip("Words Whisper should expect, so it hears 'mirror' and 'photon' rather than similar-sounding words.")]
        [SerializeField, TextArea(2, 4)] private string vocabularyHint =
            "Lumi, laser, beam, mirror, lens, light, photon, reflection, interference, detector, " +
            "Einstein Telescope, gravitational waves.";

        [Header("Recording")]
        [Tooltip("A question stops recording by itself after this long (s).")]
        [SerializeField, Range(5, 30)] private int maxSeconds = 15;
        [Tooltip("Keep recording this long after the button is released, so the last word isn't cut off (s).")]
        [SerializeField, Range(0f, 0.5f)] private float tailSeconds = 0.2f;
        [Tooltip("Shorter recordings count as an accidental press and are ignored (s).")]
        [SerializeField, Range(0.1f, 1f)] private float minSeconds = 0.3f;
        [Tooltip("Quieter recordings (average level) count as silence and are not transcribed.")]
        [SerializeField, Range(0f, 0.05f)] private float silenceLevel = 0.002f;

        [Header("Testing")]
        [Tooltip("Hold this key to talk (Editor and development builds only).")]
        [SerializeField] private Key testKey = Key.LeftCtrl;

        private const int WhisperFrequency = 16000;

        private LumiTutor tutor;
        private Animator animator;
        private ILumiSpeechToText speech;
        private AudioClip recording;
        private bool wasHeld;

        /// <summary>True while the microphone is recording.</summary>
        public bool IsListening { get; private set; }

        /// <summary>True while the recording is being turned into text.</summary>
        public bool IsTranscribing { get; private set; }

        /// <summary>What Whisper last understood.</summary>
        public string LastHeard { get; private set; } = string.Empty;

        /// <summary>Why the last recording didn't become a question, if it didn't.</summary>
        public string LastProblem { get; private set; } = string.Empty;

        /// <summary>"loading", "ready" or "no model": the state of the speech service.</summary>
        public string SpeechStatus => speech.IsReady ? "ready" : speech.IsLoading ? "loading" : "no model";

        /// <summary>True if a press of the talk button would start recording now.</summary>
        public bool CanListen => speech.IsReady && !IsListening && !IsTranscribing && !tutor.IsBusy;

        private void Awake()
        {
            tutor = GetComponent<LumiTutor>();
            animator = GetComponentInChildren<Animator>();
            speech = new WhisperSpeechToText(transform, modelFile, vocabularyHint);
        }

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Ask at startup rather than on the first press, so the headset's popup doesn't interrupt a question.
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) Permission.RequestUserPermission(Permission.Microphone);
#endif
        }

        private void Update()
        {
            bool held = ReadTalkButton(wasHeld);
            if (held && !wasHeld) StartListening();
            else if (!held && wasHeld) StopListening();
            else if (IsListening && !Microphone.IsRecording(null)) StopListening(); // reached maxSeconds
            wasHeld = held;
        }

        /// <summary>Starts recording, if Lumi is free to listen. Also usable from an on-screen button.</summary>
        public void StartListening()
        {
            if (!CanListen) return;
            LastProblem = string.Empty;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Report("no permission to use the microphone");
                Permission.RequestUserPermission(Permission.Microphone);
                return;
            }
#endif
            if (Microphone.devices.Length == 0)
            {
                Report("no microphone found");
                return;
            }

            recording = Microphone.Start(null, false, maxSeconds, WhisperFrequency);
            if (recording == null)
            {
                Report("the microphone could not start");
                return;
            }
            IsListening = true;
            SetMode(LumiMode.Listen);
        }

        /// <summary>Stops recording and sends what was said to Lumi.</summary>
        public void StopListening()
        {
            if (!IsListening) return;
            IsListening = false;
            IsTranscribing = true;
            _ = TranscribeAsync();
        }

        private async Awaitable TranscribeAsync()
        {
            try
            {
                if (Microphone.IsRecording(null)) await Awaitable.WaitForSecondsAsync(tailSeconds, destroyCancellationToken);
                SetMode(LumiMode.Think);

                float[] samples = TakeRecording(out int frequency, out int channels);
                if (samples.Length < minSeconds * frequency * channels)
                {
                    SetMode(LumiMode.Idle); // an accidental tap: no reaction needed
                    return;
                }
                if (AverageLevel(samples) < silenceLevel)
                {
                    HeardNothing("the recording was silent (is the microphone working?)");
                    return;
                }

                string text = await speech.TranscribeAsync(samples, frequency, channels, destroyCancellationToken);
                LastHeard = text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    HeardNothing("no words were recognised");
                    return;
                }
                IsTranscribing = false; // Lumi takes it from here (Ask keeps the Think pose going)
                tutor.Ask(text);
            }
            catch (OperationCanceledException)
            {
                // Lumi was destroyed or the scene closed while transcribing: nothing to do.
            }
            catch (LumiSpeechException e)
            {
                HeardNothing(e.Message);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                if (this != null) SetMode(LumiMode.Idle);
            }
            finally
            {
                IsTranscribing = false;
            }
        }

        /// <summary>Ends the recording and returns only the part that holds sound; the clip itself is freed.</summary>
        private float[] TakeRecording(out int frequency, out int channels)
        {
            bool stillRecording = Microphone.IsRecording(null);
            int length = stillRecording ? Microphone.GetPosition(null) : recording.samples; // read before End
            Microphone.End(null);

            frequency = recording.frequency;
            channels = recording.channels;
            var samples = new float[length * channels];
            if (length > 0) recording.GetData(samples, 0);
            Destroy(recording);
            recording = null;
            return samples;
        }

        private void HeardNothing(string why)
        {
            Report(why);
            SetMode(LumiMode.Idle);
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetTrigger(LumiAnimatorParams.PuzzledId);
        }

        private void Report(string why)
        {
            LastProblem = why;
            Debug.LogWarning("[Lumi] Didn't hear a question: " + why, this);
        }

        private static float AverageLevel(float[] samples)
        {
            float sum = 0f;
            foreach (float s in samples) sum += Mathf.Abs(s);
            return sum / samples.Length;
        }

        private bool ReadTalkButton(bool alreadyHeld)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Keyboard.current != null && Keyboard.current[testKey].isPressed) return true;
            if (Gamepad.current != null && Gamepad.current.buttonNorth.isPressed) return true;   // PS5 Triangle / Xbox Y while play testing
#endif
            XRController hand = XRController.rightHand;
            if (hand == null) return false;

            var talk = hand.TryGetChildControl<ButtonControl>("{PrimaryButton}");
            if (talk == null || !talk.isPressed) return false;
            if (alreadyHeld) return true; // grabbing the grip mid-question doesn't cut the student off

            var grip = hand.TryGetChildControl<ButtonControl>("{GripButton}");
            return grip == null || !grip.isPressed;
        }

        private void SetMode(LumiMode mode)
        {
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetInteger(LumiAnimatorParams.ModeId, (int)mode);
        }

        private void OnDestroy()
        {
            if (Microphone.IsRecording(null)) Microphone.End(null);
            if (recording != null) Destroy(recording);
        }
    }
}
