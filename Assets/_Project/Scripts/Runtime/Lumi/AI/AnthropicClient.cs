using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Talks to Claude through Anthropic's native Messages API (not the OpenAI-compatible layer):
    /// POST {endpoint} with { model, system, messages, max_tokens, temperature } and the headers
    /// "x-api-key" and "anthropic-version". The system prompt is a separate field here, and the
    /// reply text is in content[0].text.
    /// Non-blocking: waits frame by frame, so VR never stutters while Lumi thinks. Never logs the key.
    /// </summary>
    public sealed class AnthropicClient : ILumiLLMClient
    {
        private const string ApiVersion = "2023-06-01";

        [Serializable]
        private class Request
        {
            public string model;
            public string system;
            public LumiChatMessage[] messages;
            public int max_tokens;
            public bool stream;
            // No "temperature": newer Claude models reject it ("temperature is deprecated for this model").
        }

        [Serializable] private class Response { public ContentBlock[] content; public string stop_reason; public ApiError error; }
        [Serializable] private class ContentBlock { public string type; public string text; }
        [Serializable] private class ApiError { public string message; public string type; }
        [Serializable] private class ErrorOnly { public ApiError error; }

        private readonly LumiAIConfig config;

        public AnthropicClient(LumiAIConfig config)
        {
            this.config = config;
        }

        public string Name => "Anthropic";

        public async Awaitable<string> CompleteAsync(IReadOnlyList<LumiChatMessage> messages, CancellationToken cancellation)
        {
            if (config == null || !config.CanGoOnline) throw new LumiLLMException("No API key in the AI config.");

            // The Messages API wants the system prompt separately and only user/assistant turns in "messages".
            var system = new StringBuilder();
            var turns = new List<LumiChatMessage>();
            foreach (LumiChatMessage message in messages)
            {
                if (message.role == "system")
                {
                    if (system.Length > 0) system.Append("\n\n");
                    system.Append(message.content);
                }
                else
                {
                    turns.Add(message);
                }
            }

            var request = new Request
            {
                model = config.Model,
                system = system.ToString(),
                messages = turns.ToArray(),
                max_tokens = config.MaxTokens,
                stream = false,
            };
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));

            using var web = new UnityWebRequest(config.Endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = config.TimeoutSeconds,
            };
            web.SetRequestHeader("Content-Type", "application/json");
            web.SetRequestHeader("x-api-key", config.ApiKey.Trim());
            web.SetRequestHeader("anthropic-version", ApiVersion);

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
                if (string.IsNullOrEmpty(apiMessage)) apiMessage = Shorten(text);
                throw new LumiLLMException($"{Name} request failed (HTTP {web.responseCode}): " +
                                           (string.IsNullOrEmpty(apiMessage) ? web.error : apiMessage));
            }

            Response response;
            try
            {
                response = JsonUtility.FromJson<Response>(text);
            }
            catch (ArgumentException)
            {
                throw new LumiLLMException($"{Name} sent a reply that isn't valid JSON.");
            }

            var reply = new StringBuilder();
            if (response?.content != null)
            {
                foreach (ContentBlock block in response.content)
                {
                    if (block != null && block.type == "text") reply.Append(block.text);
                }
            }
            if (reply.ToString().Trim().Length == 0)
            {
                // Say what did come back (stop_reason, block types, start of the body) so the cause is visible in the Console.
                var types = new StringBuilder();
                if (response?.content != null)
                {
                    foreach (ContentBlock block in response.content) types.Append(block?.type).Append(' ');
                }
                throw new LumiLLMException($"{Name} sent an empty reply (stop_reason: {response?.stop_reason}, " +
                                           $"blocks: [{types.ToString().Trim()}]). Body: {Shorten(text)}" +
                                           (response?.error != null ? " " + response.error.message : string.Empty));
            }
            return reply.ToString().Trim();
        }

        private static string TryReadError(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                ApiError error = JsonUtility.FromJson<ErrorOnly>(json)?.error;
                return error == null ? null : $"{error.type}: {error.message}";
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            return text.Length <= 200 ? text : text.Substring(0, 200) + "...";
        }
    }
}
