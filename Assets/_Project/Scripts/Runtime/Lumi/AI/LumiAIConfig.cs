using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Settings for Lumi's AI brain. Create one with Create > Einstein Telescope > Lumi > AI Config.
    ///
    /// ⚠️ This asset holds the API key. Keep it ONLY in the git-ignored secrets folder and never
    /// commit it: the repository is on GitHub. (The key still ends up inside a built app, which is
    /// acceptable only because this is a classroom demo.) Without a config or key, Lumi answers offline.
    /// </summary>
    [CreateAssetMenu(fileName = "LumiAIConfig", menuName = "Einstein Telescope/Lumi/AI Config")]
    public class LumiAIConfig : ScriptableObject
    {
        [Header("DeepSeek (OpenAI-compatible)")]
        [Tooltip("Secret! From the DeepSeek platform. Set a spending limit there.")]
        [SerializeField] private string apiKey = "";
        [SerializeField] private string endpoint = "https://api.deepseek.com/chat/completions";
        [SerializeField] private string model = "deepseek-chat";

        [Header("Answers")]
        [Tooltip("Upper limit for one reply. Lumi should answer in 1–3 short sentences anyway.")]
        [SerializeField, Range(30, 400)] private int maxTokens = 150;
        [Tooltip("0 = always the same wording, 1 = more varied.")]
        [SerializeField, Range(0f, 1.5f)] private float temperature = 0.7f;
        [Tooltip("Give up and use the offline answer after this many seconds.")]
        [SerializeField, Range(3, 60)] private int timeoutSeconds = 15;
        [Tooltip("How many earlier question+answer pairs Lumi remembers.")]
        [SerializeField, Range(0, 20)] private int historyTurns = 6;

        public string ApiKey => apiKey;
        public string Endpoint => endpoint;
        public string Model => model;
        public int MaxTokens => maxTokens;
        public float Temperature => temperature;
        public int TimeoutSeconds => timeoutSeconds;
        public int HistoryTurns => historyTurns;
        public bool HasKey => !string.IsNullOrWhiteSpace(apiKey);
    }
}
