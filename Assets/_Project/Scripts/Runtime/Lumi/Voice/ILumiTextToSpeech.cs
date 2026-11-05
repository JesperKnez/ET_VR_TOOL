using System.Threading;
using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Turns text into speech audio. Behind an interface so the voice service can be swapped
    /// (Azure today; another service or an on-device voice later) without touching LumiVoice.
    /// </summary>
    public interface ILumiTextToSpeech
    {
        /// <summary>Short name for logs, e.g. "Azure".</summary>
        string Name { get; }

        /// <summary>
        /// Returns the spoken text as an AudioClip. Throws <see cref="LumiVoiceException"/> when the
        /// service can't, and OperationCanceledException when cancelled.
        /// </summary>
        Awaitable<AudioClip> SynthesizeAsync(string text, CancellationToken cancellation);
    }

    /// <summary>The voice service could not speak (no key, network error, bad voice name, ...).</summary>
    public class LumiVoiceException : System.Exception
    {
        public LumiVoiceException(string message) : base(message) { }
    }
}
