using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_RitualObligationTargetWorker_SkyLanterns
    {
        // Matches the maxPawns the actual fetch job reserves with (JobGiver_TakeCountToInventory.TryGiveJob:
        // pawn.CanReserve(x, 10, toTake)), so this preflight doesn't reject a shared stack that the fetch job
        // could still reserve. The vanilla preflight call omits maxPawns and defaults to 1.
        private const int FetchReservationMaxPawns = 10;

        [HarmonyPatch]
        public static class GetBlockingIssues
        {
            private static readonly FieldInfo ListerThingsField = AccessTools.Field(typeof(Map), nameof(Map.listerThings));
            private static readonly FieldInfo WoodLogField = AccessTools.Field(typeof(ThingDefOf), nameof(ThingDefOf.WoodLog));
            private static readonly MethodInfo ThingsOfDefMethod = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsOfDef), new[] { typeof(ThingDef) });
            private static readonly MethodInfo CanReserveAndReachMethod = AccessTools.Method(typeof(ReservationUtility), nameof(ReservationUtility.CanReserveAndReach), new[]
            {
                typeof(Pawn), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(Danger), typeof(int), typeof(int), typeof(ReservationLayerDef), typeof(bool)
            });
            private static readonly MethodInfo GetWoodCandidatesMethod = AccessTools.Method(typeof(GetBlockingIssues), nameof(GetWoodCandidates));
            private static readonly MethodInfo CanReserveAndReachForFetchMethod = AccessTools.Method(typeof(GetBlockingIssues), nameof(CanReserveAndReachForFetch));

            public static MethodBase TargetMethod()
            {
                MethodInfo getBlockingIssues = AccessTools.Method(typeof(RitualObligationTargetWorker_SkyLanterns), nameof(RitualObligationTargetWorker_SkyLanterns.GetBlockingIssues));
                return AccessTools.EnumeratorMoveNext(getBlockingIssues);
            }

            // Replaces `target.Map.listerThings.ThingsOfDef(ThingDefOf.WoodLog)` with a call that takes the
            // same Map already on the evaluation stack (pushed by the preceding TargetInfo.get_Map()) and
            // returns a candidate list that also includes extractable network-stored wood.
            //
            // Also redirects the participant CanReserveAndReach check to reserve with FetchReservationMaxPawns
            // instead of the vanilla default of 1 (see the constant's comment above).
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                List<CodeInstruction> result = new List<CodeInstruction>(codes.Count);
                int candidateListReplacements = 0;
                int reservationReplacements = 0;

                for (int i = 0; i < codes.Count; i++)
                {
                    if (i + 2 < codes.Count
                        && codes[i].opcode == OpCodes.Ldfld && codes[i].operand is FieldInfo listerField && listerField == ListerThingsField
                        && codes[i + 1].opcode == OpCodes.Ldsfld && codes[i + 1].operand is FieldInfo woodField && woodField == WoodLogField
                        && codes[i + 2].Calls(ThingsOfDefMethod))
                    {
                        result.Add(new CodeInstruction(OpCodes.Call, GetWoodCandidatesMethod));
                        candidateListReplacements++;
                        i += 2;
                        continue;
                    }

                    if (codes[i].Calls(CanReserveAndReachMethod))
                    {
                        result.Add(new CodeInstruction(OpCodes.Call, CanReserveAndReachForFetchMethod));
                        reservationReplacements++;
                        continue;
                    }

                    result.Add(codes[i]);
                }

                if (candidateListReplacements == 1)
                {
                    Logger.Message("Patched RitualObligationTargetWorker_SkyLanterns.GetBlockingIssues transpiler successfully. Replaced wood candidate list lookup.");
                }
                else
                {
                    Logger.Error($"Failed to patch RitualObligationTargetWorker_SkyLanterns.GetBlockingIssues transpiler: expected exactly 1 wood candidate list replacement, found {candidateListReplacements}. Sky lantern rituals will not see network-stored wood.");
                    return codes;
                }

                if (reservationReplacements == 1)
                {
                    Logger.Message("Patched RitualObligationTargetWorker_SkyLanterns.GetBlockingIssues transpiler successfully. Replaced participant reservation check.");
                }
                else
                {
                    Logger.Error($"Failed to patch RitualObligationTargetWorker_SkyLanterns.GetBlockingIssues transpiler: expected exactly 1 reservation check replacement, found {reservationReplacements}. Shared network wood stacks may show a false shortage.");
                    return codes;
                }

                return result;
            }

            private static List<Thing> GetWoodCandidates(Map map)
            {
                List<Thing> candidates = new List<Thing>(map.listerThings.ThingsOfDef(ThingDefOf.WoodLog));
                HashSet<Thing> seen = null;

                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (item.def != ThingDefOf.WoodLog || item.stackCount <= 0 || item.Destroyed)
                    {
                        continue;
                    }

                    if (seen == null)
                    {
                        seen = new HashSet<Thing>(candidates);
                    }

                    if (seen.Add(item))
                    {
                        candidates.Add(item);
                    }
                }

                return candidates;
            }

            private static bool CanReserveAndReachForFetch(Pawn p, LocalTargetInfo target, PathEndMode peMode, Danger maxDanger, int maxPawns, int stackCount, ReservationLayerDef layer, bool ignoreOtherReservations)
            {
                return p.CanReserveAndReach(target, peMode, maxDanger, FetchReservationMaxPawns, stackCount, layer, ignoreOtherReservations);
            }
        }
    }
}
