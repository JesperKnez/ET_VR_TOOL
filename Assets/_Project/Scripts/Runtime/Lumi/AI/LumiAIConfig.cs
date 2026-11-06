using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>Which service writes Lumi's answers.</summary>
    public enum LumiAIProvider
    {
        DeepSeek,
        Ollama,
        Anthropic, // appended last so existing config assets keep their chosen provider
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

        [Header("Anthropic (Claude, native Messages API)")]
        [Tooltip("Secret! An API key from console.anthropic.com (a Claude subscription does not work). Set a spending limit there.")]
        [SerializeField] private string anthropicApiKey = "";
        [Tooltip("The full Messages API URL, including /messages.")]
        [SerializeField] private string anthropicEndpoint = "https://api.anthropic.com/v1/messages";
        [Tooltip("Any Claude model id. A small, fast one suits short answers.")]
        [SerializeField] private string anthropicModel = "claude-haiku-5-5";
        [Tooltip("Give up and use the offline answer after this many seconds.")]
        [SerializeField, Range(3, 60)] private int anthropicTimeoutSeconds = 15;

        [Header("Answers")]
        [Tooltip("Upper limit for one reply. Lumi should answer in 1–3 short sentences anyway.")]
        [SerializeField, Range(30, 400)] private int maxTokens = 150;
        [Tooltip("0 = always the same wording, 1 = more varied.")]
        [SerializeField, Range(0f, 1.5f)] private float temperature = 0.7f;
        [Tooltip("How many earlier question+answer pairs Lumi remembers.")]
        [SerializeField, Range(0, 20)] private int historyTurns = 6;

        public LumiAIProvider Provider => provider;
        public bool UsesOllama => provider == LumiAIProvider.Ollama;
        public bool UsesAnthropic => provider == LumiAIProvider.Anthropic;

        /// <summary>The key of the chosen provider. Each provider's key only goes to that provider's endpoint.</summary>
        public string ApiKey => provider switch
        {
            LumiAIProvider.Anthropic => anthropicApiKey,
            LumiAIProvider.Ollama => string.Empty,
            _ => apiKey,
        };

        public string Endpoint => provider switch
        {
            LumiAIProvider.Anthropic => anthropicEndpoint,
            LumiAIProvider.Ollama => ollamaEndpoint,
            _ => endpoint,
        };

        public string Model => provider switch
        {
            LumiAIProvider.Anthropic => anthropicModel,
            LumiAIProvider.Ollama => ollamaModel,
            _ => model,
        };

        public int TimeoutSeconds => provider switch
        {
            LumiAIProvider.Anthropic => anthropicTimeoutSeconds,
            LumiAIProvider.Ollama => ollamaTimeoutSeconds,
            _ => timeoutSeconds,
        };

        public int MaxTokens => maxTokens;
        public float Temperature => temperature;
        public int HistoryTurns => historyTurns;
        public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

        /// <summary>DeepSeek gets its key as a Bearer token; Ollama never gets one (Anthropic has its own client and header).</summary>
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
