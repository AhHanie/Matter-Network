using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobGiver_GatherOfferingsForPsychicRitual
    {
        // Vanilla searches ThingRequestGroup.HaulableAlways via GenClosest.ClosestThingReachable,
        // which our existing GenClosest patch only extends for single-def searches (see
        // Patch_GenClosest.ClosestThingReachable). A group search finds no job when the only
        // eligible offering (Shards, Bioferrite) is sitting in a network, even though the setup
        // dialog (once Patch_PsychicRitualDef is applied) already counts it as reachable.
        [HarmonyPatch(typeof(JobGiver_GatherOfferingsForPsychicRitual), "TryGiveJob")]
        public static class TryGiveJob
        {
            public static void Postfix(Pawn pawn, ref Job __result)
            {
                if (__result != null || !ModsConfig.AnomalyActive || pawn?.Map == null)
                {
                    return;
                }

                Lord lord = pawn.GetLord();
                if (lord == null || !(lord.CurLordToil is LordToil_PsychicRitual lordToil))
                {
                    return;
                }

                if (!(lordToil.RitualData.psychicRitual.def is PsychicRitualDef_InvocationCircle ritualDef) || ritualDef.RequiredOffering == null)
                {
                    return;
                }

                PsychicRitual psychicRitual = lordToil.RitualData.psychicRitual;
                PsychicRitualRoleDef role = psychicRitual.assignments.RoleForPawn(pawn);
                if (role == null)
                {
                    return;
                }

                float offeringCount = PsychicRitualToil_GatherOfferings.PawnsOfferingCount(psychicRitual.assignments.AssignedPawns(role), ritualDef.RequiredOffering);
                int needed = Mathf.CeilToInt(ritualDef.RequiredOffering.GetBaseCount() - offeringCount);
                if (needed <= 0)
                {
                    return;
                }

                Thing networkThing = NetworkItemSearchUtility.FindClosestReachableThing(pawn, item =>
                    ritualDef.RequiredOffering.filter.Allows(item) &&
                    !item.IsForbidden(pawn) &&
                    pawn.CanReserve(item, 10, Mathf.Min(needed, item.stackCount)),
                    out _);

                if (networkThing == null)
                {
                    return;
                }

                Job job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, networkThing);
                job.count = Mathf.Min(needed, networkThing.stackCount);
                __result = job;
            }
        }
    }
}
