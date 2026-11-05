using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Talks to DeepSeek's Chat Completions API (OpenAI-compatible):
    /// POST {endpoint} with "Authorization: Bearer {key}" and { model, messages, max_tokens, temperature }.
    /// Non-blocking: waits frame by frame, so VR never stutters while Lumi thinks. Never logs the key.
    /// </summary>
    public sealed class DeepSeekClient : ILumiLLMClient
    {
        [Serializable]
        private class Request
        {
            public string model;
            public LumiChatMessage[] messages;
            public int max_tokens;
            public float temperature;
            public bool stream;
        }

        [Serializable] private class Response { public Choice[] choices; public ApiError error; }
        [Serializable] private class Choice { public LumiChatMessage message; public string finish_reason; }
        [Serializable] private class ApiError { public string message; public string type; }
        [Serializable] private class ErrorOnly { public ApiError error; }

        private readonly LumiAIConfig config;

        public DeepSeekClient(LumiAIConfig config)
        {
            this.config = config;
        }

        public string Name => "DeepSeek";

        public async Awaitable<string> CompleteAsync(IReadOnlyList<LumiChatMessage> messages, CancellationToken cancellation)
        {
            if (config == null || !config.CanGoOnline) throw new LumiLLMException("No API key in the AI config.");

            var request = new Request
            {
                model = config.Model,
                messages = new LumiChatMessage[messages.Count],
                max_tokens = config.MaxTokens,
                temperature = config.Temperature,
                stream = false,
            };
            for (int i = 0; i < messages.Count; i++) request.messages[i] = messages[i];
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));

            using var web = new UnityWebRequest(config.Endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = config.TimeoutSeconds,
            };
            web.SetRequestHeader("Content-Type", "application/json");
            if (config.HasKey) web.SetRequestHeader("Authorization", "Bearer " + config.ApiKey.Trim());

            UnityWebRequestAsyncOperation operation = web.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellation.IsCancellationRequested) web.Abort();
                await Awaitable.NextFrameAsync(cancellation); // throws OperationCanceledException when cancelled
            }

            string text = web.downloadHandler?.text ?? string.Empty;
            if (web.result != UnityWebRequest.Result.Success)
            {
                string apiMessage = TryReadError(text);
                throw new LumiLLMException($"DeepSeek request failed (HTTP {web.responseCode}): " +
                                           (string.IsNullOrEmpty(apiMessage) ? web.error : apiMessage));
            }

            Response response;
            try
            {
                response = JsonUtility.FromJson<Response>(text);
            }
            catch (ArgumentException)
            {
                throw new LumiLLMException("DeepSeek sent a reply that isn't valid JSON.");
            }

            string content = response?.choices != null && response.choices.Length > 0
                ? response.choices[0].message.content
                : null;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new LumiLLMException("DeepSeek sent an empty reply." +
                                           (response?.error != null ? " " + response.error.message : string.Empty));
            }
            return content.Trim();
        }

        private static string TryReadError(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<ErrorOnly>(json)?.error?.message;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
