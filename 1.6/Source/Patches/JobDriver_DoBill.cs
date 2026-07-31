using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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

        [HarmonyPatch]
        public static class CollectIngredientsToils
        {
            private static readonly MethodInfo GotoThingMethod = AccessTools.Method(
                typeof(Toils_Goto), nameof(Toils_Goto.GotoThing), new[] { typeof(TargetIndex), typeof(PathEndMode), typeof(bool) });

            public static MethodBase TargetMethod()
            {
                MethodInfo collectIngredientsToils = AccessTools.Method(typeof(JobDriver_DoBill), nameof(JobDriver_DoBill.CollectIngredientsToils));
                return AccessTools.EnumeratorMoveNext(collectIngredientsToils);
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo replacementMethod = AccessTools.Method(typeof(CollectIngredientsToils), nameof(MakeNetworkAwareIngredientGotoToil));
                int replacements = 0;

                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(GotoThingMethod))
                    {
                        replacements++;
                        yield return new CodeInstruction(OpCodes.Call, replacementMethod);
                        continue;
                    }

                    yield return instruction;
                }

                if (replacements > 0)
                {
                    Logger.Message($"Patched JobDriver_DoBill.CollectIngredientsToils transpiler successfully. Replaced {replacements} ingredient goto toil call(s).");
                }
                else
                {
                    Logger.Error("Failed to patch JobDriver_DoBill.CollectIngredientsToils transpiler: no ingredient goto toil call was found. Falling back to vanilla ingredient pathing.");
                }
            }

            public static Toil MakeNetworkAwareIngredientGotoToil(TargetIndex ind, PathEndMode peMode, bool canGotoSpawnedParent)
            {
                Toil toil = ToilMaker.MakeToil("MN_GotoBillIngredient");
                toil.initAction = delegate
                {
                    Pawn actor = toil.actor;
                    Job job = actor.jobs.curJob;
                    LocalTargetInfo dest = job.GetTarget(ind);
                    Thing thing = dest.Thing;
                    string recipeLabel = job.RecipeDef?.defName ?? job.def?.defName ?? "<no recipe>";

                    if (thing == null)
                    {
                        Logger.Message($"[BillIngredientRouting] {actor.LabelShort} recipe={recipeLabel}: no ingredient target, using vanilla goto.");
                        StartVanillaGoto(actor, dest, null, peMode, canGotoSpawnedParent);
                        return;
                    }

                    string holderType = thing.ParentHolder?.GetType().Name ?? "<unheld>";

                    if (actor.Map == null || !NetworkItemSearchUtility.TryResolveNetworkItem(actor.Map, thing, out DataNetwork network))
                    {
                        Logger.Message($"[BillIngredientRouting] {actor.LabelShort} recipe={recipeLabel} ingredient={thing.thingIDNumber}:{thing.LabelShort} holder={holderType}: not a network item, using vanilla goto. PositionHeld={thing.PositionHeld}, pawnPos={actor.Position}.");
                        StartVanillaGoto(actor, dest, thing, peMode, canGotoSpawnedParent);
                        return;
                    }

                    if (!network.IsOperational)
                    {
                        Logger.Warning($"[BillIngredientRouting] {actor.LabelShort} recipe={recipeLabel} ingredient={thing.thingIDNumber}:{thing.LabelShort} network={network.NetworkId}: network offline, ending job.");
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                        return;
                    }

                    NetworkBuildingNetworkInterface closestInterface = Patch_Toils_Goto.GotoThing.FindClosestReachableInterface(actor, network);
                    if (closestInterface == null)
                    {
                        Logger.Warning($"[BillIngredientRouting] {actor.LabelShort} recipe={recipeLabel} ingredient={thing.thingIDNumber}:{thing.LabelShort} network={network.NetworkId} controller={network.ActiveController?.thingIDNumber}: no reachable interface, ending job. PositionHeld={thing.PositionHeld}, pawnPos={actor.Position}.");
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                        return;
                    }

                    Logger.Message($"[BillIngredientRouting] {actor.LabelShort} recipe={recipeLabel} ingredient={thing.thingIDNumber}:{thing.LabelShort} holder={holderType} network={network.NetworkId} controller={network.ActiveController?.thingIDNumber}: redirecting to interface {closestInterface.thingIDNumber} at {closestInterface.Position} (PositionHeld={thing.PositionHeld}, pawnPos={actor.Position}).");
                    PathEndMode interfacePeMode = peMode == PathEndMode.InteractionCell ? PathEndMode.OnCell : peMode;

                    if (actor.Position == closestInterface.InteractionCell)
                    {
                        actor.pather.StopDead();
                        actor.jobs.curDriver.ReadyForNextToil();
                    }
                    else
                    {
                        actor.pather.StartPath(closestInterface.InteractionCell, interfacePeMode);
                    }
                };
                toil.defaultCompleteMode = ToilCompleteMode.PatherArrival;

                if (canGotoSpawnedParent)
                {
                    toil.FailOnSelfAndParentsDespawnedOrNull(ind);
                }
                else
                {
                    toil.FailOnDespawnedOrNull(ind);
                }

                return toil;
            }

            private static void StartVanillaGoto(Pawn actor, LocalTargetInfo dest, Thing thing, PathEndMode peMode, bool canGotoSpawnedParent)
            {
                if (thing != null && canGotoSpawnedParent)
                {
                    dest = thing.SpawnedParentOrMe;
                }

                actor.pather.StartPath(dest, peMode);
            }
        }
    }
}
