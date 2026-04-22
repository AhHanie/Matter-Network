using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobGiver_SatisfyChemicalNeed
    {
        [HarmonyPatch(typeof(JobGiver_SatisfyChemicalNeed), "FindDrugFor")]
        public static class FindDrugFor
        {
            public static void Postfix(Pawn pawn, Need_Chemical need, ref Thing __result)
            {
                if (!pawn.Spawned || need?.AddictionHediff == null)
                {
                    return;
                }

                Thing networkDrug = NetworkItemSearchUtility.FindClosestReachableThing(
                    pawn,
                    item => IsValidNetworkDrug(pawn, need, item),
                    out float networkDistanceSquared);

                if (networkDrug == null)
                {
                    return;
                }

                if (__result == null || networkDistanceSquared < NetworkItemSearchUtility.GetThingDistanceSquared(pawn, __result))
                {
                    __result = networkDrug;
                }
            }

            private static bool IsValidNetworkDrug(Pawn pawn, Need_Chemical need, Thing drug)
            {
                Hediff_Addiction addiction = need.AddictionHediff;
                if (addiction == null || !drug.def.IsDrug || !drug.IngestibleNow)
                {
                    return false;
                }

                if (!pawn.CanReserve(drug) || drug.IsForbidden(pawn) || !drug.IsSociallyProper(pawn))
                {
                    return false;
                }

                CompDrug compDrug = drug.TryGetComp<CompDrug>();
                if (compDrug?.Props.chemical == null || compDrug.Props.chemical.addictionHediff != addiction.def)
                {
                    return false;
                }

                DrugPolicy drugPolicy = pawn.drugs?.CurrentPolicy;
                if (drugPolicy != null
                    && !drugPolicy[drug.def].allowedForAddiction
                    && pawn.story != null
                    && pawn.story.traits.DegreeOfTrait(TraitDefOf.DrugDesire) <= 0
                    && (!pawn.InMentalState || !pawn.MentalStateDef.ignoreDrugPolicy))
                {
                    return false;
                }

                return true;
            }
        }
    }
}
