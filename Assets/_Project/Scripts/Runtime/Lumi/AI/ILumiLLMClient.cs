using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Something that answers a chat: DeepSeek online, or the scripted offline tutor.
    /// Behind an interface so the tutor works the same either way, and a provider can be swapped.
    /// </summary>
    public interface ILumiLLMClient
    {
        /// <summary>Short name for logs and the debug window, e.g. "DeepSeek" or "Offline".</summary>
        string Name { get; }

        /// <summary>
        /// Returns the reply text for the conversation so far (the last message is the student's).
        /// Throws <see cref="LumiLLMException"/> when it can't answer, and OperationCanceledException when cancelled.
        /// </summary>
        Awaitable<string> CompleteAsync(IReadOnlyList<LumiChatMessage> messages, CancellationToken cancellation);
    }
}
