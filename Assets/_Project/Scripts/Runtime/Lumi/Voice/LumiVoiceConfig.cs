using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Settings for Lumi's voice (Azure AI Speech, text-to-speech). Create one with
    /// Create > Einstein Telescope > Lumi > Voice Config.
    ///
    /// ⚠️ This asset holds the Azure key. Keep it ONLY in the git-ignored Assets/_Project/Secrets/
    /// folder, like LumiAIConfig. Without it, Lumi still answers but stays silent.
    /// </summary>
    [CreateAssetMenu(fileName = "LumiVoiceConfig", menuName = "Einstein Telescope/Lumi/Voice Config")]
    public class LumiVoiceConfig : ScriptableObject
    {
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

        [Header("Network")]
        [Tooltip("Give up and stay silent after this many seconds.")]
        [SerializeField, Range(3, 30)] private int timeoutSeconds = 10;

        public string SubscriptionKey => subscriptionKey;
        public string Region => region;
        public string VoiceName => voiceName;
        public int RatePercent => ratePercent;
        public int PitchPercent => pitchPercent;
        public int TimeoutSeconds => timeoutSeconds;
        public bool HasKey => !string.IsNullOrWhiteSpace(subscriptionKey) && !string.IsNullOrWhiteSpace(region);
    }
}
