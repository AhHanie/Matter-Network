using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JoyGiver_TakeDrug
    {
        [HarmonyPatch(typeof(JoyGiver_TakeDrug), "BestIngestItem")]
        public static class BestIngestItem
        {
            public static void Postfix(Pawn pawn, Predicate<Thing> extraValidator, ref Thing __result)
            {
                if (!pawn.Spawned)
                {
                    return;
                }

                Thing networkDrug = NetworkItemSearchUtility.FindClosestReachableThing(pawn, item => IsValidDrugForJoy(pawn, item, extraValidator), out float networkDistanceSquared);
                if (networkDrug == null)
                {
                    return;
                }

                if (__result == null || networkDistanceSquared < NetworkItemSearchUtility.GetThingDistanceSquared(pawn, __result))
                {
                    __result = networkDrug;
                }
            }

            private static bool IsValidDrugForJoy(Pawn pawn, Thing item, Predicate<Thing> extraValidator)
            {
                if (!item.def.IsIngestible || item.def.ingestible == null || item.def.ingestible.drugCategory == DrugCategory.None)
                {
                    return false;
                }

                if (item.def.ingestible.joyKind == null || item.def.ingestible.joy <= 0f || !pawn.WillEat(item))
                {
                    return false;
                }

                if (extraValidator != null && !extraValidator(item))
                {
                    return false;
                }

                if (!pawn.CanReserve(item) || item.IsForbidden(pawn) || !item.IsSociallyProper(pawn) || !item.IsPoliticallyProper(pawn))
                {
                    return false;
                }

                if (item.def.IsDrug && pawn.drugs != null && !pawn.drugs.CurrentPolicy[item.def].allowedForJoy && pawn.story != null && pawn.story.traits.DegreeOfTrait(TraitDefOf.DrugDesire) <= 0 && !pawn.InMentalState)
                {
                    return false;
                }

                return true;
            }
        }
    }
}
