using System;
using Verse;

namespace SK_Matter_Network
{
    public static class Logger
    {
        private const string Prefix = "[Matter Network] ";

        public static void Message(string message)
        {
            Log.Message(Prefix + message);
        }

        public static void Warning(string message)
        {
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

        public static string DescribeThing(Thing thing)
        {
            if (thing == null)
            {
                return "thing=<null>";
            }

            string defName = thing.def?.defName ?? "null";
            string mapState = thing.MapHeld == null ? "map=null" : $"map={thing.MapHeld.uniqueID}";
            string positionState = thing.PositionHeld.IsValid ? $"pos={thing.PositionHeld}" : "pos=invalid";
            string ownerState = thing.holdingOwner == null ? "holdingOwner=null" : $"holdingOwner={thing.holdingOwner.GetType().Name}";
            string parentState = thing.ParentHolder == null ? "parentHolder=null" : $"parentHolder={thing.ParentHolder.GetType().Name}";

            return $"{defName} id={thing.thingIDNumber} stack={thing.stackCount} destroyed={thing.Destroyed} spawned={thing.Spawned} {mapState} {positionState} {ownerState} {parentState}";
        }
    }
}
