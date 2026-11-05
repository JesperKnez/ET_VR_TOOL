using System.Collections.Generic;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// The last few question/answer pairs, so follow-ups like "and the other mirror?" make sense.
    /// Kept short on purpose: each turn costs tokens (money and time) on every request.
    /// </summary>
    public class LumiConversationHistory
    {
        private readonly List<LumiChatMessage> messages = new List<LumiChatMessage>();

        public int MaxTurns { get; set; }

        public LumiConversationHistory(int maxTurns)
        {
            MaxTurns = maxTurns;
        }

        public IReadOnlyList<LumiChatMessage> Messages => messages;

        public void Add(string question, string answer)
        {
            messages.Add(LumiChatMessage.User(question));
            messages.Add(LumiChatMessage.Assistant(answer));
            int extra = messages.Count - MaxTurns * 2;
            if (extra > 0) messages.RemoveRange(0, extra);
        }

        /// <summary>Forget everything, e.g. when a new level starts.</summary>
        public void Clear() => messages.Clear();
    }
}
