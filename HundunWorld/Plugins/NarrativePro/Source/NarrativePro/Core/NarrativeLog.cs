using FlaxEngine;

namespace NarrativePro.Core
{
    public static class NarrativeLog
    {
        private const string Prefix = "[NarrativePro] ";

        public static void Log(object message)
        {
            Debug.Log(Prefix + message);
        }

        public static void LogWarning(object message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void LogError(object message)
        {
            Debug.LogError(Prefix + message);
        }
    }
}
