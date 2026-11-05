using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using EinsteinTelescope.Laser;
using UnityEngine;
using UnityEngine.Events;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Lumi's brain: a question goes in, a tutor-style answer comes out.
    ///
    /// 1. Lumi switches to the Think pose.
    /// 2. The question goes to DeepSeek together with Lumi's personality, the live laser status and the
    ///    last few exchanges (see LumiPromptBuilder). Without a key, or if the request fails, a scripted
    ///    answer built from the level's own texts is used instead, so Lumi always answers.
    /// 3. An action tag at the end of the answer ([CELEBRATE], [WAVE], [FOCUS], [POINT:mirror]) is removed.
    ///    With a LumiVoice on the same object, the answer is turned into speech while Lumi still thinks.
    /// 4. OnReply fires with the clean text (for subtitles), Lumi switches to the Talk pose, speaks and acts
    ///    out the tag. The Talk pose lasts as long as the speech (or a reading-time estimate when silent).
    ///
    /// Call <see cref="Ask"/> from any script (LumiListener does, with what the student said). Put it on the Lumi object.
    /// </summary>
    [DisallowMultipleComponent]
    public class LumiTutor : MonoBehaviour
    {
        [Tooltip("AI settings with the API key (keep it in the git-ignored secrets folder). Empty = offline answers.")]
        [SerializeField] private LumiAIConfig config;

        [Header("Speaking")]
        [Tooltip("Words per second used to guess how long Lumi 'talks' (Talk pose, subtitles).")]
        [SerializeField, Range(1f, 5f)] private float wordsPerSecond = 2.5f;
        [Tooltip("How long a pointed-at mirror stays pointed at after answering (s).")]
        [SerializeField, Range(0.5f, 6f)] private float pointSeconds = 2.5f;

        [Header("Events")]
        [Tooltip("The student's question, as soon as it is asked.")]
        [SerializeField] private UnityEvent<string> onQuestion = new UnityEvent<string>();
        [Tooltip("Lumi's answer (without action tags): hook up subtitles and text-to-speech here.")]
        [SerializeField] private UnityEvent<string> onReply = new UnityEvent<string>();

        private static readonly Regex ActionTag = new Regex(@"\[(CELEBRATE|WAVE|FOCUS|POINT\s*:\s*([^\]]+))\]",
                                                            RegexOptions.IgnoreCase);
        private static readonly Regex AnyTag = new Regex(@"\[[A-Z_]+(\s*:[^\]]*)?\]");

        private Animator animator;
        private LumiLookAt lookAt;
        private LumiLaserReactions laser;
        private LumiVoice voice;
        private ILumiLLMClient online, offline;
        private LumiConversationHistory history;
        private int offlineIndex;

        public UnityEvent<string> OnQuestion => onQuestion;
        public UnityEvent<string> OnReply => onReply;

        /// <summary>True while Lumi is thinking about or answering a question.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>"DeepSeek" or "Offline": who will answer the next question.</summary>
        public string BrainName => (online ?? offline).Name;

        /// <summary>The last question and answer.</summary>
        public string LastQuestion { get; private set; } = string.Empty;
        public string LastReply { get; private set; } = string.Empty;

        /// <summary>Why the last answer came from the offline brain, if it did.</summary>
        public string LastProblem { get; private set; } = string.Empty;

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            lookAt = GetComponent<LumiLookAt>();
            laser = GetComponent<LumiLaserReactions>();
            voice = GetComponent<LumiVoice>();
            offline = new OfflineTutorClient(ScriptedAnswer);
            online = config != null && config.HasKey ? new DeepSeekClient(config) : null;
            history = new LumiConversationHistory(config != null ? config.HistoryTurns : 6);
        }

        /// <summary>Ask Lumi something. Ignored while Lumi is still answering the previous question.</summary>
        public void Ask(string question)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(question)) return;
            _ = AskAsync(question.Trim());
        }

        /// <summary>Forget the conversation (e.g. when a new level starts).</summary>
        public void ForgetConversation() => history.Clear();

        private async Awaitable AskAsync(string question)
        {
            IsBusy = true;
            LastQuestion = question;
            LastProblem = string.Empty;
            onQuestion.Invoke(question);
            SetMode(LumiMode.Think);

            try
            {
                var messages = new List<LumiChatMessage> { LumiChatMessage.System(LumiPromptBuilder.BuildSystemPrompt(BuildContext())) };
                messages.AddRange(history.Messages);
                messages.Add(LumiChatMessage.User(question));

                string reply;
                try
                {
                    reply = online != null
                        ? await online.CompleteAsync(messages, destroyCancellationToken)
                        : await offline.CompleteAsync(messages, destroyCancellationToken);
                }
                catch (LumiLLMException e)
                {
                    LastProblem = e.Message;
                    Debug.LogWarning("[Lumi] " + e.Message + " Using the offline answer.", this);
                    reply = ScriptedAnswer(question);
                }

                string text = ExtractAction(reply, out string action, out string mirrorName);
                history.Add(question, text);
                LastReply = text;

                // Prepare the speech while Lumi still looks thoughtful, so voice and Talk pose start together.
                AudioClip speech = voice != null ? await voice.PrepareAsync(text, destroyCancellationToken) : null;

                onReply.Invoke(text);
                SetMode(LumiMode.Talk);
                Transform pointedMirror = Act(action, mirrorName);
                float talkSeconds = speech != null ? speech.length : Mathf.Clamp(CountWords(text) / wordsPerSecond, 1.5f, 15f);
                if (speech != null) voice.Play(speech);
                await Awaitable.WaitForSecondsAsync(Mathf.Max(talkSeconds, pointedMirror ? pointSeconds : 0f),
                                                    destroyCancellationToken);
                if (pointedMirror) StopPointing(pointedMirror);
                if (CurrentMode() == LumiMode.Talk) SetMode(LumiMode.Idle);
            }
            catch (OperationCanceledException)
            {
                // Lumi was destroyed or the scene closed while thinking: nothing to do.
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                if (this != null) SetMode(LumiMode.Idle);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private LumiTutorContext BuildContext()
        {
            var context = new LumiTutorContext();
            if (laser == null) return context;

            LaserLevel level = laser.CurrentLevel;
            if (level != null)
            {
                context.LevelTitle = level.Title;
                context.Objective = level.ObjectiveText;
                context.Physics = level.PhysicsText;
                context.ControlsTip = level.ControlsTip;
                var names = new List<string>();
                foreach (MirrorMount mirror in level.Mirrors)
                {
                    if (mirror != null) names.Add(mirror.DisplayName);
                }
                context.MirrorNames = names.ToArray();
            }
            context.StatusReport = laser.BuildStatusReport();
            float ago = Time.time - laser.LastWarningTime;
            if (!string.IsNullOrEmpty(laser.LastWarning) && ago < 60f)
            {
                context.RecentWarning = laser.LastWarning;
                context.WarningSecondsAgo = ago;
            }
            return context;
        }

        // ------------------------------------------------------------------ actions
        private static string ExtractAction(string reply, out string action, out string mirrorName)
        {
            action = null;
            mirrorName = null;
            Match match = ActionTag.Match(reply);
            if (match.Success)
            {
                action = match.Groups[2].Success ? "POINT" : match.Groups[1].Value.ToUpperInvariant();
                mirrorName = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null;
            }
            string clean = AnyTag.Replace(reply, string.Empty);
            return Regex.Replace(clean, @"\s{2,}", " ").Trim();
        }

        private Transform Act(string action, string mirrorName)
        {
            if (animator == null || action == null) return null;
            switch (action)
            {
                case "CELEBRATE": animator.SetTrigger(LumiAnimatorParams.CelebrateId); return null;
                case "WAVE": animator.SetTrigger(LumiAnimatorParams.WaveId); return null;
                case "FOCUS": animator.SetTrigger(LumiAnimatorParams.FocusZoomId); return null;
                case "POINT":
                    MirrorMount mirror = laser != null ? laser.FindMirror(mirrorName) : null;
                    if (mirror == null) return null; // unknown name: just talk, don't point at nothing
                    if (lookAt != null) lookAt.Target = mirror.transform;
                    animator.SetBool(LumiAnimatorParams.PointingId, true);
                    return mirror.transform;
            }
            return null;
        }

        private void StopPointing(Transform mirror)
        {
            if (animator != null) animator.SetBool(LumiAnimatorParams.PointingId, false);
            if (lookAt != null && lookAt.Target == mirror) lookAt.Target = null;
        }

        // ------------------------------------------------------------------ offline answers
        /// <summary>
        /// A scripted, tutor-style answer built from the level's own texts. Used without an API key,
        /// and whenever DeepSeek can't answer (no network at school, timeout...).
        /// </summary>
        private string ScriptedAnswer(string question)
        {
            string q = (question ?? string.Empty).ToLowerInvariant();
            LaserLevel level = laser != null ? laser.CurrentLevel : null;

            if (laser != null && !string.IsNullOrEmpty(laser.LastWarning) && Time.time - laser.LastWarningTime < 30f)
            {
                return "Ooh, interesting! " + laser.LastWarning.Trim() + " What could you change so the light goes where you want?";
            }
            if (level == null)
            {
                return "Light loves to travel in straight lines until something bounces it away. Want to start a level and take a closer look? [WAVE]";
            }
            if (ContainsAny(q, "why", "how does", "what is", "what's", "physics", "explain", "warum", "wieso", "was ist"))
            {
                string physics = FirstSentences(level.PhysicsText, 2);
                if (!string.IsNullOrEmpty(physics)) return physics + " What do you think that means for your mirrors? [FOCUS]";
            }
            if (ContainsAny(q, "help", "stuck", "what do i do", "how do i", "what now", "hint", "control", "hilfe", "tipp"))
            {
                string goal = FirstSentences(level.ObjectiveText, 2);
                string tip = FirstSentences(level.ControlsTip, 1);
                if (!string.IsNullOrEmpty(goal)) return (goal + " " + tip).Trim() + " Which mirror do you want to try first?";
            }

            string[] nudges =
            {
                "Let's take a closer look! Where does the beam hit right now, and where should it go?",
                "Good question! Try a tiny step with one mirror and watch which way the spot moves.",
                "Light always bounces off a mirror at the same angle it came in. Can you use that to aim the beam?",
                "You're doing real science: change one thing, then watch what happens. What do you notice?",
            };
            return nudges[offlineIndex++ % nudges.Length];
        }

        private static bool ContainsAny(string text, params string[] words)
        {
            foreach (string w in words)
            {
                if (text.Contains(w)) return true;
            }
            return false;
        }

        private static string FirstSentences(string text, int count)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string flat = Regex.Replace(text, @"\s+", " ").Trim();
            int end = 0;
            for (int i = 0; i < count; i++)
            {
                int next = flat.IndexOfAny(new[] { '.', '!', '?' }, end);
                if (next < 0) return flat;
                end = next + 1;
            }
            return flat.Substring(0, end).Trim();
        }

        // ------------------------------------------------------------------ helpers
        private static int CountWords(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

        private LumiMode CurrentMode() => animator != null ? (LumiMode)animator.GetInteger(LumiAnimatorParams.ModeId) : LumiMode.Idle;

        private void SetMode(LumiMode mode)
        {
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetInteger(LumiAnimatorParams.ModeId, (int)mode);
        }
    }
}
