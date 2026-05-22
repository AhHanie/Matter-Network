using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobGiver_BingeDrug
    {
        private const float MaxOverdoseSeverityToAvoid = 0.786f;

        [HarmonyPatch(typeof(JobGiver_BingeDrug), "BestIngestTarget")]
        public static class BestIngestTarget
        {
            public static void Postfix(Pawn pawn, ref Thing __result)
            {
                if (!pawn.Spawned)
                {
                    return;
                }

                MentalState_BingingDrug mentalState = pawn.MentalState as MentalState_BingingDrug;
                ChemicalDef chemical = mentalState?.chemical;
                if (chemical == null)
                {
                    return;
                }

                DrugCategory drugCategory = mentalState.drugCategory;
                Hediff overdose = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.DrugOverdose);
                Thing closestNetworkDrug = NetworkItemSearchUtility.FindClosestReachableThing(
                    pawn,
                    item => IsValidNetworkDrug(pawn, item, chemical, drugCategory, overdose),
                    out float closestNetworkDistanceSquared);

                if (closestNetworkDrug == null)
                {
                    return;
                }

                if (__result == null)
                {
                    __result = closestNetworkDrug;
                    return;
                }

                float currentResultDistanceSquared = NetworkItemSearchUtility.GetThingDistanceSquared(pawn, __result);
                if (closestNetworkDistanceSquared < currentResultDistanceSquared)
                {
                    __result = closestNetworkDrug;
                }
            }

            private static bool IsValidNetworkDrug(Pawn pawn, Thing item, ChemicalDef chemical, DrugCategory drugCategory, Hediff overdose)
            {
                if (!Patch_AddictionUtility.IsMatchingDrug(item, chemical, drugCategory))
                {
                    return false;
                }

                if (!pawn.InMentalState && item.IsForbidden(pawn))
                {
                    return false;
                }

                if (!pawn.CanReserve(item))
                {
                    return false;
                }

                CompDrug compDrug = item.TryGetComp<CompDrug>();
                if (compDrug == null)
                {
                    return false;
                }

                if (overdose != null && compDrug.Props.CanCauseOverdose && overdose.Severity + compDrug.Props.overdoseSeverityOffset.max >= MaxOverdoseSeverityToAvoid)
                {
                    return false;
                }

                return true;
            }
        }
    }
}
