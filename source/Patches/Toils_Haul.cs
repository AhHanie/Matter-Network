using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;    
using Verse.AI;
using System.Linq;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Toils_Haul
    {
        [HarmonyPatch(typeof(Toils_Haul), "ErrorCheckForCarry")]
        public static class ErrorCheckForCarry
        {
            public static bool Prefix(Pawn pawn, Thing haulThing, bool canTakeFromInventory, ref bool __result)
            {
                if (haulThing.MapHeld == null)
                {
                    return true;
                }
                NetworksMapComponent mapComp = haulThing.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(haulThing, out DataNetwork network))
                {
                    return true;
                }
                if (haulThing.stackCount == 0)
                {
                    Logger.Message(pawn?.ToString() + " tried to start carry " + haulThing?.ToString() + " which had stackcount 0.");
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    __result = true;
                    return false;
                }
                if (pawn.jobs.curJob.count <= 0)
                {
                    Logger.Error("Invalid count: " + pawn.jobs.curJob.count + ", setting to 1. Job was " + pawn.jobs.curJob);
                    pawn.jobs.curJob.count = 1;
                }
                __result = false;
                return false;
            }
        }

        [HarmonyPatch]
        public static class TakeToInventory
        {
            static MethodBase TargetMethod()
            {
                // Find the compiler-generated display class that contains the TakeToInventory lambda
                var displayClassType = AccessTools.FirstInner(
                    typeof(Toils_Haul),
                    type => type.Name.Contains("DisplayClass") &&
                            type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                                .Any(m => m.Name.Contains("TakeToInventory"))
                );

                if (displayClassType == null)
                {
                    Logger.Error("Could not find TakeToInventory display class");
                    return null;
                }

                // Find the lambda method (the one that contains the actual Loggeric)
                var method = AccessTools.FirstMethod(
                    displayClassType,
                    m => m.Name.Contains("TakeToInventory")
                );

                if (method == null)
                {
                    Logger.Error("Could not find TakeToInventory lambda method");
                }

                return method;
            }

            /*
             * else
				{
					actor.inventory.GetDirectlyHeldThings().TryAdd(thing.SplitOff(num));
                    ---------------------------------------------------------------------> We're adding our function call here
					if (thing.def.ingestible != null && (int)thing.def.ingestible.preferability <= 5)
					{
						actor.mindState.lastInventoryRawFoodUseTick = Find.TickManager.TicksGame;
					}
					thing.def.soundPickup.PlayOneShot(new TargetInfo(actor.Position, actor.Map));
				}
             */
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                var codes = new List<CodeInstruction>(instructions);
                LocalBuilder originalStackCountLocal = generator.DeclareLocal(typeof(int));

                // Capture the original stack count before SplitOff mutates the tracked thing.
                var splitOffMethod = AccessTools.Method(typeof(Thing), nameof(Thing.SplitOff), new Type[] { typeof(int) });
                var stackCountField = AccessTools.Field(typeof(Thing), nameof(Thing.stackCount));
                var tryAddMethod = AccessTools.Method(typeof(ThingOwner), nameof(ThingOwner.TryAdd), new Type[] { typeof(Thing), typeof(bool) });
                var removeFromNetworkMethod = AccessTools.Method(typeof(TakeToInventory), nameof(RemoveFromNetwork));

                int stackCountInsertIndex = -1;
                int insertIndex = -1;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (stackCountInsertIndex == -1 && codes[i].Calls(splitOffMethod))
                    {
                        stackCountInsertIndex = i;
                    }

                    if (codes[i].Calls(tryAddMethod) &&
                        i + 1 < codes.Count &&
                        codes[i + 1].opcode == OpCodes.Pop)
                    {
                        insertIndex = i + 2; // Insert after the pop
                        break;
                    }
                }

                if (stackCountInsertIndex == -1 || insertIndex == -1)
                {
                    Logger.Error("Could not find insertion point in TakeToInventory transpiler");
                    return codes;
                }

                var stackCountCaptureInstructions = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldloc_1),
                    new CodeInstruction(OpCodes.Ldfld, stackCountField),
                    new CodeInstruction(OpCodes.Stloc, originalStackCountLocal.LocalIndex)
                };

                var instructionsToInsert = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Ldloc_1),
                    new CodeInstruction(OpCodes.Ldloc_2),
                    new CodeInstruction(OpCodes.Ldloc, originalStackCountLocal.LocalIndex),
                    new CodeInstruction(OpCodes.Call, removeFromNetworkMethod)
                };

                codes.InsertRange(stackCountInsertIndex, stackCountCaptureInstructions);
                insertIndex += stackCountCaptureInstructions.Count;
                codes.InsertRange(insertIndex, instructionsToInsert);
                Logger.Message("TakeToInventory transpiler patched successfully.");

                return codes;
            }

            public static void RemoveFromNetwork(Pawn actor, Thing thing, int count, int originalStackCount)
            {
                NetworksMapComponent mapComp = actor.Map.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(thing, out DataNetwork network))
                {
                    network.RemoveItem(thing, count, count >= originalStackCount);
                    Logger.Message($"[TakeToInventory] Removed {count} of {thing.def.defName} from network {network.NetworkId}");
                    network.ValidateNetwork();
                }
            }
        }
    }
}
