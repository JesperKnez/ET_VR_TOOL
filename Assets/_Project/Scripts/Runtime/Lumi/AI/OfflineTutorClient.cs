using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// The scripted "brain" used without an API key or network: it hands the student's question to a
    /// function (LumiTutor builds answers from the level's own texts). The short pause lets the
    /// Think pose show, so offline and online feel the same.
    /// </summary>
    public sealed class OfflineTutorClient : ILumiLLMClient
    {
        private readonly Func<string, string> answer;
        private readonly float thinkingSeconds;

        public OfflineTutorClient(Func<string, string> answer, float thinkingSeconds = 0.6f)
        {
            this.answer = answer;
            this.thinkingSeconds = thinkingSeconds;
        }

        public string Name => "Offline";

        public async Awaitable<string> CompleteAsync(IReadOnlyList<LumiChatMessage> messages, CancellationToken cancellation)
        {
            if (thinkingSeconds > 0f) await Awaitable.WaitForSecondsAsync(thinkingSeconds, cancellation);
            string question = messages.Count > 0 ? messages[messages.Count - 1].content : string.Empty;
            return answer(question);
        }
    }
}
