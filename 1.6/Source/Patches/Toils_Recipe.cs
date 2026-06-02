using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Toils_Recipe
    {
        [HarmonyPatch]
        public static class FinishRecipeAndStartStoringProduct_Patch
        {
            static MethodBase TargetMethod()
            {
                var innerType = AccessTools.Inner(typeof(Toils_Recipe), "<>c__DisplayClass3_0");
                if (innerType == null)
                {
                    Logger.Error("[Matter Network] Patch_Toils_Recipe: Could not find inner type <>c__DisplayClass3_0 in Toils_Recipe");
                    return null;
                }
                var method = AccessTools.Method(innerType, "<FinishRecipeAndStartStoringProduct>b__1");
                if (method == null)
                {
                    Logger.Error("[Matter Network] Patch_Toils_Recipe: Could not find <FinishRecipeAndStartStoringProduct>b__1");
                }
                return method;
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codes = new List<CodeInstruction>(instructions);

                var isValidMethod = AccessTools.Method(typeof(IntVec3), "get_IsValid");
                var listGetItemMethod = AccessTools.Method(typeof(List<Thing>), "get_Item");
                var tryNetworkMethod = AccessTools.Method(typeof(Patch_Toils_Recipe), nameof(TryStartMatterNetworkProductHaul));

                // Find the single call to IntVec3.get_IsValid() in this method.
                // It appears as: ldloca.s [foundCell], call get_IsValid, brfalse <dropOnFloor>
                int isValidIdx = -1;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].Calls(isValidMethod))
                    {
                        isValidIdx = i;
                        break;
                    }
                }

                if (isValidIdx < 1)
                {
                    Logger.Error("[Matter Network] Patch_Toils_Recipe: Could not find IntVec3.get_IsValid() call");
                    return codes;
                }

                if (codes[isValidIdx - 1].opcode != OpCodes.Ldloca_S)
                {
                    Logger.Error("[Matter Network] Patch_Toils_Recipe: Expected ldloca.s before get_IsValid() call");
                    return codes;
                }

                object foundCellLocal = codes[isValidIdx - 1].operand;

                // Find the list<Thing> local variable: scan backward for ldloc.s X, ldc.i4.0, callvirt get_Item
                object listLocal = null;
                for (int i = isValidIdx - 1; i >= 2; i--)
                {
                    if ((codes[i].opcode == OpCodes.Ldloc_S || codes[i].opcode == OpCodes.Ldloc) &&
                        codes[i + 1].opcode == OpCodes.Ldc_I4_0 &&
                        codes[i + 2].Calls(listGetItemMethod))
                    {
                        listLocal = codes[i].operand;
                        break;
                    }
                }

                if (listLocal == null)
                {
                    Logger.Error("[Matter Network] Patch_Toils_Recipe: Could not find list<Thing> local variable (list[0] pattern)");
                    return codes;
                }

                // Insert before the existing `ldloca.s [foundCell]` instruction:
                //   ldloca.s foundCell
                //   call IsValid
                //   brtrue.s <skipLabel>   // if valid, vanilla handles it - skip our code
                //   ldloc.0               // actor
                //   ldloc.1               // curJob
                //   ldloc.s list          // list
                //   ldc.i4.0
                //   callvirt get_Item     // list[0]
                //   call TryStartMatterNetworkProductHaul
                //   brfalse.s <skipLabel> // if helper returns false, let vanilla run
                //   ret                   // helper started the haul job; early exit
                // <skipLabel>:            // original ldloca.s continues here

                int insertAt = isValidIdx - 1;
                var skipLabel = generator.DefineLabel();

                // Move any existing labels on the original ldloca.s to the first inserted instruction
                var insertedFirst = new CodeInstruction(OpCodes.Ldloca_S, foundCellLocal);
                insertedFirst.labels.AddRange(codes[insertAt].labels);
                codes[insertAt].labels.Clear();

                // The original ldloca.s gets the skip label so the brtrue/brfalse can jump past our code to it
                codes[insertAt].labels.Add(skipLabel);

                var toInsert = new List<CodeInstruction>
                {
                    insertedFirst,
                    new CodeInstruction(OpCodes.Call, isValidMethod),
                    new CodeInstruction(OpCodes.Brtrue_S, skipLabel),
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Ldloc_1),
                    new CodeInstruction(OpCodes.Ldloc_S, listLocal),
                    new CodeInstruction(OpCodes.Ldc_I4_0),
                    new CodeInstruction(OpCodes.Callvirt, listGetItemMethod),
                    new CodeInstruction(OpCodes.Call, tryNetworkMethod),
                    new CodeInstruction(OpCodes.Brfalse_S, skipLabel),
                    new CodeInstruction(OpCodes.Ret),
                };

                codes.InsertRange(insertAt, toInsert);
                Logger.Message("[Matter Network] Patch_Toils_Recipe: Successfully patched FinishRecipeAndStartStoringProduct.");
                return codes;
            }
        }

        internal static bool TryStartMatterNetworkProductHaul(Pawn actor, Job curJob, Thing product)
        {
            if (actor == null || curJob?.bill == null || product == null)
                return false;

            if (curJob.bill.GetStoreMode() != BillStoreModeDefOf.BestStockpile)
                return false;

            if (product.Destroyed || product.stackCount <= 0)
                return false;

            if (!TryFindBestMatterNetworkOutputDestination(actor, product, out Thing destination))
                return false;

            return StartCarryAndHaulProductToDestination(actor, product, destination);
        }

        private static bool TryFindBestMatterNetworkOutputDestination(Pawn actor, Thing product, out Thing destination)
        {
            destination = null;
            StoragePriority bestPriority = StoragePriority.Unstored;
            float bestDistSq = float.MaxValue;

            foreach (IHaulDestination dest in actor.Map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder)
            {
                if (!CanUseMatterNetworkOutputDestination(actor, product, dest))
                    continue;

                Thing destThing = (Thing)dest;
                StoragePriority destPriority = dest.GetStoreSettings().Priority;

                // List is priority-ordered descending. Stop once we're below the best found.
                if (destination != null && destPriority < bestPriority)
                    break;

                if (destPriority <= StoragePriority.Unstored)
                    continue;

                float distSq = (destThing.Position - actor.Position).LengthHorizontalSquared;
                if (destination == null || destPriority > bestPriority ||
                    (destPriority == bestPriority && distSq < bestDistSq))
                {
                    bestPriority = destPriority;
                    bestDistSq = distSq;
                    destination = destThing;
                }
            }

            return destination != null;
        }

        private static bool CanUseMatterNetworkOutputDestination(Pawn actor, Thing product, IHaulDestination dest)
        {
            if (!(dest is NetworkBuildingNetworkInterface) && !(dest is NetworkBuildingNetworkChute))
                return false;

            if (!dest.HaulDestinationEnabled)
                return false;

            if (!dest.Accepts(product))
                return false;

            Thing destThing = (Thing)dest;
            if (destThing.Faction != actor.Faction)
                return false;

            if (destThing.IsForbidden(actor))
                return false;

            if (!actor.Map.reachability.CanReach(actor.Position, destThing, PathEndMode.Touch, TraverseParms.For(actor)))
                return false;

            if (dest is IHaulEnroute enroute)
            {
                if (enroute.GetSpaceRemainingWithEnroute(product.def) <= 0)
                    return false;
            }
            else if (!actor.CanReserveNew(destThing))
            {
                return false;
            }

            return true;
        }

        private static bool StartCarryAndHaulProductToDestination(Pawn actor, Thing product, Thing destination)
        {
            int maxCarry = actor.carryTracker.MaxStackSpaceEver(product.def);

            if (maxCarry < product.stackCount)
            {
                int excess = product.stackCount - maxCarry;
                Thing split = product.SplitOff(excess);
                if (!GenPlace.TryPlaceThing(split, actor.Position, actor.Map, ThingPlaceMode.Near))
                    Logger.Error($"[Matter Network] Bill product excess could not be placed near {actor.Position}");
            }

            if (maxCarry == 0)
            {
                actor.jobs.EndCurrentJob(JobCondition.Succeeded);
                return true;
            }

            actor.carryTracker.TryStartCarry(product);

            Job haulJob = HaulAIUtility.HaulToContainerJob(actor, product, destination);
            if (haulJob == null)
                return false;

            actor.jobs.StartJob(
                haulJob,
                JobCondition.Succeeded,
                null,   // jobGiver
                false,  // resumeCurJobAfterwards
                true,   // cancelBusyStances
                null,   // thinkTree
                null,   // tag
                false,  // fromQueue
                false,  // canReturnCurJobToPool
                true    // keepCarryingThingOverride
            );
            return true;
        }
    }
}
