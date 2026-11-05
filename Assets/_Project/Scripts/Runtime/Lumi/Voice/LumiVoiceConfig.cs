using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>Which service speaks Lumi's lines.</summary>
    public enum LumiVoiceProvider
    {
        Azure,
        ElevenLabs,
    }

    /// <summary>
    /// Settings for Lumi's voice (text-to-speech with Azure AI Speech or ElevenLabs). Create one with
    /// Create > Einstein Telescope > Lumi > Voice Config. Provider chooses the service; each one keeps
    /// its own key and voice, so switching back and forth loses nothing. It is read when Play starts.
    ///
    /// ⚠️ This asset holds the keys. Keep it ONLY in the git-ignored Assets/_Project/Secrets/
    /// folder, like LumiAIConfig. Without a key for the chosen provider, Lumi still answers but stays silent.
    /// </summary>
    [CreateAssetMenu(fileName = "LumiVoiceConfig", menuName = "Einstein Telescope/Lumi/Voice Config")]
    public class LumiVoiceConfig : ScriptableObject
    {
        [Tooltip("The service that speaks Lumi's lines. Takes effect the next time Play starts.")]
        [SerializeField] private LumiVoiceProvider provider = LumiVoiceProvider.Azure;

        [Header("Azure AI Speech")]
        [Tooltip("Secret! 'Key 1' of your Speech resource in the Azure portal (Keys and Endpoint).")]
        [SerializeField] private string subscriptionKey = "";
        [Tooltip("The region of your Speech resource, e.g. westeurope, northeurope, eastus.")]
        [SerializeField] private string region = "westeurope";

        [Header("Voice")]
        [Tooltip("An Azure neural voice, e.g. en-US-JennyNeural, en-US-AvaMultilingualNeural, en-US-AnaNeural (child), " +
                 "en-GB-SoniaNeural. Full list: Azure 'Language and voice support'.")]
        [SerializeField] private string voiceName = "en-US-JennyNeural";
        [Tooltip("Speaking speed in percent: 0 = normal, 10 = a bit faster (sounds more excited and friendly).")]
        [SerializeField, Range(-30, 30)] private int ratePercent = 5;
        [Tooltip("Pitch in percent: positive = higher, a little more 'small robot'.")]
        [SerializeField, Range(-20, 20)] private int pitchPercent = 4;

        [Header("ElevenLabs")]
        [Tooltip("Secret! An API key from ElevenLabs (Developers > API Keys) with Text to Speech access.")]
        [SerializeField] private string elevenLabsApiKey = "";
        [Tooltip("The ID of a voice from your ElevenLabs account (My Voices > ... > Copy voice ID). The default is 'Sarah'.")]
        [SerializeField] private string elevenLabsVoiceId = "EXAVITQu4vr4xnSDxMaL";
        [Tooltip("eleven_flash_v2_5 is the fastest and uses the fewest credits. eleven_multilingual_v2 sounds richer but is slower.")]
        [SerializeField] private string elevenLabsModel = "eleven_flash_v2_5";
        [Tooltip("Speaking speed: 1 = normal.")]
        [SerializeField, Range(0.7f, 1.2f)] private float elevenLabsSpeed = 1f;

        [Header("Network")]
        [Tooltip("Give up and stay silent after this many seconds.")]
        [SerializeField, Range(3, 30)] private int timeoutSeconds = 10;

        public LumiVoiceProvider Provider => provider;
        public string ElevenLabsApiKey => elevenLabsApiKey;
        public string ElevenLabsVoiceId => elevenLabsVoiceId;
        public string ElevenLabsModel => elevenLabsModel;
        public float ElevenLabsSpeed => elevenLabsSpeed;
        public string SubscriptionKey => subscriptionKey;
        public string Region => region;
        public string VoiceName => voiceName;
        public int RatePercent => ratePercent;
        public int PitchPercent => pitchPercent;
        public int TimeoutSeconds => timeoutSeconds;

        /// <summary>True if the chosen provider has what it needs to speak.</summary>
        public bool HasKey => provider == LumiVoiceProvider.ElevenLabs
            ? !string.IsNullOrWhiteSpace(elevenLabsApiKey) && !string.IsNullOrWhiteSpace(elevenLabsVoiceId)
            : !string.IsNullOrWhiteSpace(subscriptionKey) && !string.IsNullOrWhiteSpace(region);
    }
}
