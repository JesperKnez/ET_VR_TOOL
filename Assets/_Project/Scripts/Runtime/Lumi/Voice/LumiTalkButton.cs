using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// A small Talk button in the corner of the Game view, for testing Lumi in the Editor without a controller.
    /// It only exists in the Editor, so it never appears in a build or on the Quest.
    /// Put it on the Lumi object next to LumiListener.
    /// </summary>
    [RequireComponent(typeof(LumiListener))]
    public class LumiTalkButton : MonoBehaviour
    {
#if UNITY_EDITOR
        private const float Width = 80f, Height = 34f, Margin = 12f;

        private LumiListener listener;

        private void Awake()
        {
            listener = GetComponent<LumiListener>();
        }

        private void OnGUI()
        {
            GUI.enabled = listener.IsListening || listener.CanListen;
            var corner = new Rect(Screen.width - Width - Margin, Screen.height - Height - Margin, Width, Height);
            if (GUI.Button(corner, listener.IsListening ? "Stop" : "Talk"))
            {
                if (listener.IsListening) listener.StopListening();
                else listener.StartListening();
            }
            GUI.enabled = true;
        }
#endif
    }
}
