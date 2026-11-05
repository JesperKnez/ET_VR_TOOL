using System.Threading;
using UnityEngine;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// Turns recorded speech into text. Behind an interface so the speech service can be swapped
    /// (on-device Whisper today) without touching LumiListener.
    /// </summary>
    public interface ILumiSpeechToText
    {
        /// <summary>Short name for logs, e.g. "Whisper".</summary>
        string Name { get; }

        /// <summary>True when speech can be turned into text right now.</summary>
        bool IsReady { get; }

        /// <summary>True while the service is still starting up.</summary>
        bool IsLoading { get; }

        /// <summary>
        /// Returns what was said (empty when nothing was understood). Throws
        /// <see cref="LumiSpeechException"/> when the service can't, and OperationCanceledException when cancelled.
        /// </summary>
        Awaitable<string> TranscribeAsync(float[] samples, int frequency, int channels, CancellationToken cancellation);
    }

    /// <summary>The speech service could not transcribe (model missing, native library error, ...).</summary>
    public class LumiSpeechException : System.Exception
    {
        public LumiSpeechException(string message) : base(message) { }
    }
}
