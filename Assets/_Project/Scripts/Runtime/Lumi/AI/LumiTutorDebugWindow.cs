using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// A small text box in the Game view for typing or speaking questions to Lumi while testing.
    /// Shows who answers (DeepSeek / Offline), the last question and answer, and why the
    /// offline answer was used, if it was. Only in the Editor and development builds; never in VR.
    /// Put it on the Lumi object next to LumiTutor.
    /// </summary>
    [RequireComponent(typeof(LumiTutor))]
    public class LumiTutorDebugWindow : MonoBehaviour
    {
        [SerializeField] private bool show = true;
        [Tooltip("Size of the text in the window.")]
        [SerializeField, Range(10, 30)] private int fontSize = 15;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private LumiTutor tutor;
        private LumiVoice voice;
        private LumiListener listener;
        private string question = string.Empty;
        private GUIStyle wrap;

        private void Awake()
        {
            tutor = GetComponent<LumiTutor>();
            voice = GetComponent<LumiVoice>();
            listener = GetComponent<LumiListener>();
        }

        private void OnGUI()
        {
            if (!show || tutor == null) return;
            wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            wrap.fontSize = fontSize;
            GUI.skin.textField.fontSize = fontSize;
            GUI.skin.button.fontSize = fontSize;

            float width = Mathf.Min(560f, Screen.width - 20f);
            GUILayout.BeginArea(new Rect(10f, Screen.height - 270f, width, 260f), GUI.skin.box);
            string voiceName = voice == null ? "no voice" : voice.IsAvailable ? "voice on" : "voice off";
            string earsName = listener == null ? "no ears" : "ears " + listener.SpeechStatus;
            string state = listener != null && listener.IsListening ? " — listening…"
                         : listener != null && listener.IsTranscribing ? " — transcribing…"
                         : tutor.IsBusy ? " — thinking…" : string.Empty;
            GUILayout.Label($"Lumi tutor ({tutor.BrainName}, {voiceName}, {earsName})" + state, wrap);

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("LumiQuestion");
            question = GUILayout.TextField(question, GUILayout.ExpandWidth(true));
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
                         GUI.GetNameOfFocusedControl() == "LumiQuestion";
            if ((GUILayout.Button("Ask", GUILayout.Width(70f)) || enter) && !tutor.IsBusy)
            {
                tutor.Ask(question);
                question = string.Empty;
            }
            if (listener != null && GUILayout.Button(listener.IsListening ? "Stop" : "Talk", GUILayout.Width(70f)))
            {
                if (listener.IsListening) listener.StopListening();
                else listener.StartListening();
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(tutor.LastQuestion)) GUILayout.Label("You: " + tutor.LastQuestion, wrap);
            if (!string.IsNullOrEmpty(tutor.LastReply)) GUILayout.Label("Lumi: " + tutor.LastReply, wrap);
            if (!string.IsNullOrEmpty(tutor.LastProblem)) GUILayout.Label("(offline answer because: " + tutor.LastProblem + ")", wrap);
            if (voice != null && voice.IsAvailable && !string.IsNullOrEmpty(voice.LastProblem))
                GUILayout.Label("(silent because: " + voice.LastProblem + ")", wrap);
            if (listener != null && !string.IsNullOrEmpty(listener.LastProblem))
                GUILayout.Label("(didn't hear a question because: " + listener.LastProblem + ")", wrap);
            GUILayout.EndArea();
        }
#endif
    }
}
