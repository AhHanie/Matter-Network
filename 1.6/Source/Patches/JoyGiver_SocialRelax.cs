using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JoyGiver_SocialRelax
    {
        [HarmonyPatch(typeof(JoyGiver_SocialRelax), "TryFindIngestibleToNurse")]
        public static class TryFindIngestibleToNurse
        {
            public static void Postfix(IntVec3 center, Pawn ingester, ref Thing ingestible, ref bool __result)
            {
                Thing networkDrug = NetworkItemSearchUtility.FindClosestReachableThing(center, ingester, item => IsValidNurseableDrug(ingester, item), out float networkDistanceSquared);
                if (networkDrug == null)
                {
                    return;
                }

                if (!__result)
                {
                    ingestible = networkDrug;
                    __result = true;
                    return;
                }

                float currentDistanceSquared = NetworkItemSearchUtility.GetThingDistanceSquared(ingester, ingestible);
                if (networkDistanceSquared < currentDistanceSquared)
                {
                    ingestible = networkDrug;
                    __result = true;
                }
            }

            private static bool IsValidNurseableDrug(Pawn ingester, Thing item)
            {
                if (!item.def.IsDrug || item.def.ingestible == null || !item.def.ingestible.nurseable)
                {
                    return false;
                }

                if (ingester.drugs == null || !ingester.drugs.CurrentPolicy[item.def].allowedForJoy)
                {
                    return false;
                }

                return ingester.CanReserve(item) && !item.IsForbidden(ingester);
            }
        }
    }
}
