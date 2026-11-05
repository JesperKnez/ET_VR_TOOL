using System;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// ElevenLabs text-to-speech over its REST API:
    /// POST https://api.elevenlabs.io/v1/text-to-speech/{voice_id} with a JSON body.
    /// Asks for raw 16-bit PCM and decodes it here, so it plays on the Mac Editor and on the Quest
    /// alike without depending on platform MP3 support. Never logs the key.
    /// </summary>
    public sealed class ElevenLabsTextToSpeech : ILumiTextToSpeech
    {
        private const int SampleRate = 24000; // must match OutputFormat
        private const string OutputFormat = "pcm_24000";
        private const int MaxErrorDetail = 200;

        private readonly LumiVoiceConfig config;

        public ElevenLabsTextToSpeech(LumiVoiceConfig config)
        {
            this.config = config;
        }

        public string Name => "ElevenLabs";

        [Serializable]
        private class VoiceSettings
        {
            public float speed;
        }

        [Serializable]
        private class SpeechRequest
        {
            public string text;
            public string model_id;
            public VoiceSettings voice_settings;
        }

        public async Awaitable<AudioClip> SynthesizeAsync(string text, CancellationToken cancellation)
        {
            if (config == null || !config.HasKey) throw new LumiVoiceException("No ElevenLabs key or voice ID in the voice config.");
            if (string.IsNullOrWhiteSpace(text)) throw new LumiVoiceException("Nothing to say.");

            string voiceId = UnityWebRequest.EscapeURL(config.ElevenLabsVoiceId.Trim());
            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}?output_format={OutputFormat}";
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new SpeechRequest
            {
                text = text.Trim(),
                model_id = config.ElevenLabsModel.Trim(),
                voice_settings = new VoiceSettings { speed = config.ElevenLabsSpeed },
            }));

            using var web = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = config.TimeoutSeconds,
            };
            web.SetRequestHeader("xi-api-key", config.ElevenLabsApiKey.Trim());
            web.SetRequestHeader("Content-Type", "application/json");

            UnityWebRequestAsyncOperation operation = web.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellation.IsCancellationRequested) web.Abort();
                await Awaitable.NextFrameAsync(cancellation);
            }

            if (web.result != UnityWebRequest.Result.Success)
            {
                string hint = web.responseCode switch
                {
                    401 => " (wrong key, or the monthly credits are used up?)",
                    402 => " (this voice or model needs a paid plan?)",
                    404 => " (unknown voice ID?)",
                    422 => " (unknown model name?)",
                    429 => " (too many requests at once)",
                    _ => string.Empty,
                };
                throw new LumiVoiceException($"ElevenLabs text-to-speech failed (HTTP {web.responseCode}){hint}: {ErrorDetail(web)}");
            }

            return DecodePcm(web.downloadHandler.data, "Lumi speech");
        }

        /// <summary>The reason ElevenLabs gives is more useful than the HTTP error, e.g. "quota_exceeded".</summary>
        private static string ErrorDetail(UnityWebRequest web)
        {
            string detail = web.downloadHandler != null ? web.downloadHandler.text : null;
            if (string.IsNullOrWhiteSpace(detail)) return web.error;
            return detail.Length > MaxErrorDetail ? detail.Substring(0, MaxErrorDetail) : detail;
        }

        /// <summary>Reads raw 16-bit mono PCM (little-endian) into an AudioClip.</summary>
        private static AudioClip DecodePcm(byte[] pcm, string clipName)
        {
            if (pcm == null || pcm.Length < 2) throw new LumiVoiceException("ElevenLabs sent no audio.");

            var samples = new float[pcm.Length / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
            }
            AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
