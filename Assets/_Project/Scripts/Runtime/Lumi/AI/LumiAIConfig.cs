using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>Which service writes Lumi's answers.</summary>
    public enum LumiAIProvider
    {
        DeepSeek,
        Ollama,
    }

    /// <summary>
    /// Settings for Lumi's AI brain. Create one with Create > Einstein Telescope > Lumi > AI Config.
    /// Provider chooses the service; each one keeps its own settings, so switching back and forth
    /// loses nothing. It is read when Play starts.
    ///
    /// ⚠️ This asset holds the API key. Keep it ONLY in the git-ignored secrets folder and never
    /// commit it: the repository is on GitHub. (The key still ends up inside a built app, which is
    /// acceptable only because this is a classroom demo.) DeepSeek without a key, or no config at all,
    /// means Lumi answers offline. Ollama needs no key, and the DeepSeek key is never sent to it.
    /// </summary>
    [CreateAssetMenu(fileName = "LumiAIConfig", menuName = "Einstein Telescope/Lumi/AI Config")]
    public class LumiAIConfig : ScriptableObject
    {
        [Tooltip("The service that writes Lumi's answers. Takes effect the next time Play starts.")]
        [SerializeField] private LumiAIProvider provider = LumiAIProvider.DeepSeek;

        [Header("DeepSeek (OpenAI-compatible)")]
        [Tooltip("Secret! From the DeepSeek platform. Set a spending limit there.")]
        [SerializeField] private string apiKey = "";
        [SerializeField] private string endpoint = "https://api.deepseek.com/chat/completions";
        [SerializeField] private string model = "deepseek-chat";
        [Tooltip("Give up and use the offline answer after this many seconds.")]
        [SerializeField, Range(3, 60)] private int timeoutSeconds = 15;

        [Header("Ollama (runs on a computer, no key needed)")]
        [SerializeField] private string ollamaEndpoint = "http://localhost:11434/v1/chat/completions";
        [Tooltip("A model you downloaded with 'ollama pull'. Avoid models that write out their thinking.")]
        [SerializeField] private string ollamaModel = "llama3.2";
        [Tooltip("Give up and use the offline answer after this many seconds. Laptops can be slow, especially on the first question.")]
        [SerializeField, Range(3, 120)] private int ollamaTimeoutSeconds = 60;

        [Header("Answers")]
        [Tooltip("Upper limit for one reply. Lumi should answer in 1–3 short sentences anyway.")]
        [SerializeField, Range(30, 400)] private int maxTokens = 150;
        [Tooltip("0 = always the same wording, 1 = more varied.")]
        [SerializeField, Range(0f, 1.5f)] private float temperature = 0.7f;
        [Tooltip("How many earlier question+answer pairs Lumi remembers.")]
        [SerializeField, Range(0, 20)] private int historyTurns = 6;

        public LumiAIProvider Provider => provider;
        public bool UsesOllama => provider == LumiAIProvider.Ollama;
        public string ApiKey => apiKey;
        public string Endpoint => UsesOllama ? ollamaEndpoint : endpoint;
        public string Model => UsesOllama ? ollamaModel : model;
        public int MaxTokens => maxTokens;
        public float Temperature => temperature;
        public int TimeoutSeconds => UsesOllama ? ollamaTimeoutSeconds : timeoutSeconds;
        public int HistoryTurns => historyTurns;
        public bool HasKey => !string.IsNullOrWhiteSpace(apiKey);

        /// <summary>The key only ever goes to DeepSeek's endpoint setting, never to Ollama.</summary>
        public bool SendsApiKey => !UsesOllama && HasKey;

        /// <summary>True for a server on this computer (e.g. Ollama), which needs no API key.</summary>
        public bool IsLocalEndpoint =>
            Endpoint != null &&
            (Endpoint.StartsWith("http://localhost", System.StringComparison.OrdinalIgnoreCase) ||
             Endpoint.StartsWith("http://127.0.0.1", System.StringComparison.OrdinalIgnoreCase));

        /// <summary>Lumi can use the AI: Ollama needs no key, otherwise there is a key or a local endpoint.</summary>
        public bool CanGoOnline => UsesOllama || HasKey || IsLocalEndpoint;
    }
}
