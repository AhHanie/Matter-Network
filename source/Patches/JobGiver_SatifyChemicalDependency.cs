using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobGiver_SatifyChemicalDependency
    {
        [HarmonyPatch(typeof(JobGiver_SatifyChemicalDependency), "FindDrugFor")]
        public static class FindDrugFor
        {
            public static void Postfix(Pawn pawn, Hediff_ChemicalDependency dependency, ref Thing __result)
            {
                if (!ModsConfig.BiotechActive || !pawn.Spawned)
                {
                    return;
                }

                Thing networkDrug = NetworkItemSearchUtility.FindClosestReachableThing(
                    pawn,
                    item => IsValidNetworkDrug(pawn, dependency, item),
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

            private static bool IsValidNetworkDrug(Pawn pawn, Hediff_ChemicalDependency dependency, Thing drug)
            {
                if (!drug.def.IsDrug || !drug.IngestibleNow)
                {
                    return false;
                }

                if (!pawn.CanReserve(drug) || drug.IsForbidden(pawn) || !drug.IsSociallyProper(pawn))
                {
                    return false;
                }

                CompDrug compDrug = drug.TryGetComp<CompDrug>();
                if (compDrug?.Props.chemical == null || compDrug.Props.chemical != dependency.chemical)
                {
                    return false;
                }

                if (pawn.drugs != null
                    && !pawn.drugs.CurrentPolicy[drug.def].allowedForAddiction
                    && (!pawn.InMentalState || pawn.MentalStateDef.ignoreDrugPolicy))
                {
                    return false;
                }

                return true;
            }
        }
    }
}
