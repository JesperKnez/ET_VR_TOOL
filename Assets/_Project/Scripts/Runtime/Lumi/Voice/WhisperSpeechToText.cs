using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using Whisper;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Speech-to-text with Whisper, running on the device itself (whisper.unity / whisper.cpp).
    /// The recorded voice never leaves the headset. English only.
    /// The model file lives in Assets/StreamingAssets/Whisper and starts loading as soon as this is created.
    /// </summary>
    public class WhisperSpeechToText : ILumiSpeechToText
    {
        // Whisper writes sounds it can't turn into words like [BLANK_AUDIO], (wind blowing) or *cough*.
        private static readonly Regex NonSpeech = new Regex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*");

        private readonly WhisperManager manager;

        public string Name => "Whisper";
        public bool IsReady => manager != null && manager.IsLoaded;
        public bool IsLoading => manager != null && manager.IsLoading;

        public WhisperSpeechToText(Transform parent, string modelFile, string vocabularyHint)
        {
            // WhisperManager starts loading in Awake, so it stays inactive until the model path is set.
            var host = new GameObject("Lumi Whisper");
            host.SetActive(false);
            host.transform.SetParent(parent, false);

            manager = host.AddComponent<WhisperManager>();
            manager.ModelPath = "Whisper/" + modelFile;
            manager.IsModelPathInStreamingAssets = true;
            manager.language = "en";
            manager.initialPrompt = vocabularyHint;

            host.SetActive(true);
        }

        public async Awaitable<string> TranscribeAsync(float[] samples, int frequency, int channels, CancellationToken cancellation)
        {
            if (!IsReady) throw new LumiSpeechException("the Whisper model is not loaded");

            WhisperResult result = await manager.GetTextAsync(samples, frequency, channels);
            cancellation.ThrowIfCancellationRequested();
            if (result == null) throw new LumiSpeechException("Whisper could not process the recording");

            return Regex.Replace(NonSpeech.Replace(result.Result, string.Empty), @"\s{2,}", " ").Trim();
        }
    }
}
