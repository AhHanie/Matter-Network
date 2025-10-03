using System;
using Verse;

namespace SK_Matter_Network
{
    public static class Logger
    {
        private const string Prefix = "[Matter Network] ";

        public static void Message(string message)
        {
            if (!ModSettings.EnableLogging)
            {
                return;
            }

            Log.Message(Prefix + message);
        }

        public static void Warning(string message)
        {
            if (!ModSettings.EnableLogging)
            {
                return;
            }

            Log.Warning(Prefix + message);
        }

        public static void Error(string message)
        {
            Log.Error(Prefix + message);
        }

        public static void Exception(Exception exception, string context = null)
        {
            if (exception == null)
            {
                return;
            }

            string prefix = string.IsNullOrWhiteSpace(context) ? Prefix : Prefix + context + ": ";
            Log.Error(prefix + exception);
        }
    }
}
