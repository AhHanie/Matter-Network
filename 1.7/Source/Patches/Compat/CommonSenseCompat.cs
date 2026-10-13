using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class CommonSenseCompat
    {
        private const string PackageId = "avilmask.CommonSense";

        private static readonly Type opportunisticTasksType =
            AccessTools.TypeByName("CommonSense.OpportunisticTasks");

        private static readonly MethodInfo haulingOpportunityMethod =
            opportunisticTasksType != null
                ? AccessTools.Method(opportunisticTasksType, "Hauling_Opportunity", new[] { typeof(Job), typeof(Pawn) })
                : null;

        private static bool warnedMissingApi;

        private static bool IsAvailable() => haulingOpportunityMethod != null;

        [HarmonyPatch]
        public static class Patch_Hauling_Opportunity
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                bool available = IsAvailable();
                if (ModsConfig.IsActive(PackageId) && !available && !warnedMissingApi)
                {
                    warnedMissingApi = true;
                    Logger.Warning("[Common Sense] Compatibility disabled because CommonSense.OpportunisticTasks.Hauling_Opportunity(Job, Pawn) was not found.");
                }

                return available;
            }

            public static MethodBase TargetMethod() => haulingOpportunityMethod;

            public static void Postfix(Job billJob, Pawn pawn, ref Job __result)
            {
                if (__result == null)
                {
                    return;
                }

                Thing source = __result.targetA.Thing;
                if (!NetworkItemSearchUtility.TryResolveNetworkItem(pawn.Map, source, out _))
                {
                    return;
                }

                Logger.Message("[Common Sense] Suppressed pre-bill haul of network item " + source.LabelShort +
                    " for " + pawn.LabelShort + "; bill " + billJob.def.defName + " will collect it through the network instead.");

                __result = null;
            }
        }
    }
}
