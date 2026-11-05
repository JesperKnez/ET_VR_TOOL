using System;
using System.Security;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Azure AI Speech text-to-speech over its REST API:
    /// POST https://{region}.tts.speech.microsoft.com/cognitiveservices/v1 with an SSML body.
    /// Asks for 16-bit PCM WAV and decodes it here, so it plays on the Mac Editor and on the Quest
    /// alike without depending on platform MP3 support. Never logs the key.
    /// </summary>
    public sealed class AzureTextToSpeech : ILumiTextToSpeech
    {
        private const string OutputFormat = "riff-24khz-16bit-mono-pcm";
        private readonly LumiVoiceConfig config;

        public AzureTextToSpeech(LumiVoiceConfig config)
        {
            this.config = config;
        }

        public string Name => "Azure";

        public async Awaitable<AudioClip> SynthesizeAsync(string text, CancellationToken cancellation)
        {
            if (config == null || !config.HasKey) throw new LumiVoiceException("No Azure key or region in the voice config.");
            if (string.IsNullOrWhiteSpace(text)) throw new LumiVoiceException("Nothing to say.");

            string url = $"https://{config.Region.Trim()}.tts.speech.microsoft.com/cognitiveservices/v1";
            byte[] body = Encoding.UTF8.GetBytes(BuildSsml(text));

            using var web = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = config.TimeoutSeconds,
            };
            web.SetRequestHeader("Ocp-Apim-Subscription-Key", config.SubscriptionKey.Trim());
            web.SetRequestHeader("Content-Type", "application/ssml+xml");
            web.SetRequestHeader("X-Microsoft-OutputFormat", OutputFormat);
            web.SetRequestHeader("User-Agent", "EinsteinTelescopeLumi");

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
                    401 => " (wrong key or region?)",
                    400 => " (unknown voice name?)",
                    429 => " (too many requests or free quota used up)",
                    _ => string.Empty,
                };
                throw new LumiVoiceException($"Azure text-to-speech failed (HTTP {web.responseCode}){hint}: {web.error}");
            }

            return DecodeWav(web.downloadHandler.data, "Lumi speech");
        }

        private string BuildSsml(string text)
        {
            // Escape &, <, > etc. so the answer can't break the XML.
            string safe = SecurityElement.Escape(text.Trim());
            string rate = (config.RatePercent >= 0 ? "+" : "") + config.RatePercent + "%";
            string pitch = (config.PitchPercent >= 0 ? "+" : "") + config.PitchPercent + "%";
            return "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
                   $"<voice name='{SecurityElement.Escape(config.VoiceName)}'>" +
                   $"<prosody rate='{rate}' pitch='{pitch}'>{safe}</prosody>" +
                   "</voice></speak>";
        }

        /// <summary>Reads a 16-bit PCM WAV (RIFF) into an AudioClip.</summary>
        private static AudioClip DecodeWav(byte[] wav, string clipName)
        {
            if (wav == null || wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF")
                throw new LumiVoiceException("Azure did not send WAV audio.");

            int channels = 1, sampleRate = 24000, bits = 16, dataStart = -1, dataLength = 0;
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                string chunk = Encoding.ASCII.GetString(wav, pos, 4);
                int size = BitConverter.ToInt32(wav, pos + 4);
                if (chunk == "fmt ")
                {
                    channels = BitConverter.ToInt16(wav, pos + 10);
                    sampleRate = BitConverter.ToInt32(wav, pos + 12);
                    bits = BitConverter.ToInt16(wav, pos + 22);
                }
                else if (chunk == "data")
                {
                    dataStart = pos + 8;
                    dataLength = Mathf.Min(size, wav.Length - dataStart);
                    break;
                }
                pos += 8 + size + (size & 1); // chunks are padded to an even size
            }
            if (dataStart < 0 || bits != 16) throw new LumiVoiceException("Unexpected WAV format from Azure.");

            int frames = dataLength / 2 / channels;
            var samples = new float[frames * channels];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = BitConverter.ToInt16(wav, dataStart + i * 2) / 32768f;
            }
            AudioClip clip = AudioClip.Create(clipName, frames, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
