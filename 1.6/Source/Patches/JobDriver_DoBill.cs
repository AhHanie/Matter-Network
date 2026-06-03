using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobDriver_DoBill
    {
        [HarmonyPatch(typeof(JobDriver_DoBill), nameof(JobDriver_DoBill.TryMakePreToilReservations))]
        public static class TryMakePreToilReservations
        {
            public static bool Prefix(JobDriver_DoBill __instance, bool errorOnFailed, ref bool __result)
            {
                List<LocalTargetInfo> targetQueueB = __instance.job.GetTargetQueue(TargetIndex.B);
                if (targetQueueB.NullOrEmpty() || !NetworkBillIngredientUtility.ContainsNetworkIngredient(__instance.pawn, targetQueueB))
                {
                    return true;
                }

                Thing billGiverThing = __instance.job.GetTarget(TargetIndex.A).Thing;
                if (!__instance.pawn.Reserve(__instance.job.GetTarget(TargetIndex.A), __instance.job, 1, -1, null, errorOnFailed))
                {
                    __result = false;
                    return false;
                }

                if (billGiverThing != null && billGiverThing.def.hasInteractionCell &&
                    !__instance.pawn.ReserveSittableOrSpot(billGiverThing.InteractionCell, __instance.job, errorOnFailed))
                {
                    __result = false;
                    return false;
                }

                NetworkBillIngredientUtility.ReserveIngredientQueue(__instance.pawn, __instance.job, targetQueueB);
                __result = true;
                return false;
            }
        }
    }
}
