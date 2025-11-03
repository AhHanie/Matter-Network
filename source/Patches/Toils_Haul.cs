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
                    Log.Message(pawn?.ToString() + " tried to start carry " + haulThing?.ToString() + " which had stackcount 0.");
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    __result = true;
                    return false;
                }
                if (pawn.jobs.curJob.count <= 0)
                {
                    Log.Error("Invalid count: " + pawn.jobs.curJob.count + ", setting to 1. Job was " + pawn.jobs.curJob);
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
                    Log.Error("[Matter Network] Could not find TakeToInventory display class");
                    return null;
                }

                // Find the lambda method (the one that contains the actual logic)
                var method = AccessTools.FirstMethod(
                    displayClassType,
                    m => m.Name.Contains("TakeToInventory")
                );

                if (method == null)
                {
                    Log.Error("[Matter Network] Could not find TakeToInventory lambda method");
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
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);

                // Find the TryAdd call followed by pop
                var tryAddMethod = AccessTools.Method(typeof(ThingOwner), nameof(ThingOwner.TryAdd), new Type[] { typeof(Thing), typeof(bool) });
                var removeFromNetworkMethod = AccessTools.Method(typeof(TakeToInventory), nameof(RemoveFromNetwork));

                int insertIndex = -1;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].Calls(tryAddMethod) &&
                        i + 1 < codes.Count &&
                        codes[i + 1].opcode == OpCodes.Pop)
                    {
                        insertIndex = i + 2; // Insert after the pop
                        break;
                    }
                }

                if (insertIndex == -1)
                {
                    Log.Error("[Matter Network] Could not find insertion point in TakeToInventory transpiler");
                    return codes;
                }

                // Insert the call to RemoveFromNetwork(actor, thing, num)
                // Based on the IL, local variables are: [0] actor, [1] thing, [2] num
                var instructionsToInsert = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldloc_0),  // Load actor (Pawn)
                    new CodeInstruction(OpCodes.Ldloc_1),  // Load thing (Thing)
                    new CodeInstruction(OpCodes.Ldloc_2),  // Load num (int)
                    new CodeInstruction(OpCodes.Call, removeFromNetworkMethod)
                };

                codes.InsertRange(insertIndex, instructionsToInsert);

                return codes;
            }

            public static void RemoveFromNetwork(Pawn actor, Thing thing, int count)
            {
                NetworksMapComponent mapComp = actor.Map.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(thing, out DataNetwork network))
                {
                    network.RemoveItem(thing, count);
                    Log.Message($"[TakeToInventory] Removed {count} of {thing.def.defName} from network {network.NetworkId}");
                    network.ValidateNetwork();
                }
            }
        }
    }
}
