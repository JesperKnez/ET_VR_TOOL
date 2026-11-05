using System;

namespace EinsteinTelescope.Lumi
{
    /// <summary>
    /// One message of a chat with the language model, in the OpenAI/DeepSeek format.
    /// The lower-case field names are what the API expects in its JSON.
    /// </summary>
    [Serializable]
    public struct LumiChatMessage
    {
        public string role;    // "system", "user" or "assistant"
        public string content;

        public LumiChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }

        public static LumiChatMessage System(string content) => new LumiChatMessage("system", content);
        public static LumiChatMessage User(string content) => new LumiChatMessage("user", content);
        public static LumiChatMessage Assistant(string content) => new LumiChatMessage("assistant", content);
    }

    /// <summary>The model could not answer (network error, bad key, timeout, empty reply...).</summary>
    public class LumiLLMException : Exception
    {
        public LumiLLMException(string message) : base(message) { }
    }
}
