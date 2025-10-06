using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WorkGiver_DoBill
    {
        [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestIngredientsHelper")]
        public static class TryFindBestIngredientsHelper
        {
            /*
             * foreach (IHaulSource item in pawn.Map.haulDestinationManager.AllHaulSourcesListForReading)
		        {
			        if (!item.HaulSourceEnabled || !(item is Thing { Spawned: not false, Position: var position } thing) || !position.InHorDistOf(billGiver.Position, searchRadius) || thing.IsForbidden(pawn))
			        {
				        continue;
			        }
			        ThingOwnerUtility.GetAllThingsRecursively(item, newRelevantThings);
			        foreach (Thing newRelevantThing in newRelevantThings)
			        {
				        if (!processedThings.Contains(newRelevantThing) && !newRelevantThing.IsForbidden(pawn) && pawn.CanReserve(newRelevantThing) && thingValidator(newRelevantThing))
				        {
					        relevantThings.Add(newRelevantThing);
					        processedThings.Add(newRelevantThing);
				        }
			        }
		        }
            -------------------------> We are inserting our function call here
	        	newRelevantThings.Clear();
             */
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);

                // Get field references
                var newRelevantThingsField = AccessTools.Field(typeof(WorkGiver_DoBill), "newRelevantThings");
                var relevantThingsField = AccessTools.Field(typeof(WorkGiver_DoBill), "relevantThings");
                var processedThingsField = AccessTools.Field(typeof(WorkGiver_DoBill), "processedThings");
                var addNetworkThingsMethod = AccessTools.Method(typeof(Patch_WorkGiver_DoBill), nameof(AddNetworkThings));

                // Get display class fields
                var displayClassType = AccessTools.Inner(typeof(WorkGiver_DoBill), "<>c__DisplayClass24_0");
                if (displayClassType == null)
                {
                    Log.Error("[Matter Network]: Could not find display class in TryFindBestIngredientsHelper");
                    return codes;
                }

                var pawnField = AccessTools.Field(displayClassType, "pawn");
                var thingValidatorField = AccessTools.Field(displayClassType, "thingValidator");

                // Find the insertion point: ldsfld newRelevantThings followed by Clear, after the foreach loop
                int insertIndex = -1;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].LoadsField(newRelevantThingsField) &&
                        i + 1 < codes.Count &&
                        codes[i + 1].Calls(AccessTools.Method(typeof(List<Thing>), "Clear")))
                    {
                        // Check if there's an endfinally within the previous 20 instructions
                        // This ensures we're at the right Clear call (after the foreach)
                        bool foundEndFinally = false;
                        for (int j = Math.Max(0, i - 20); j < i; j++)
                        {
                            if (codes[j].opcode == OpCodes.Endfinally)
                            {
                                foundEndFinally = true;
                                break;
                            }
                        }

                        if (foundEndFinally)
                        {
                            insertIndex = i;
                            break;
                        }
                    }
                }

                if (insertIndex == -1)
                {
                    Log.Error("[Matter Network]: Could not find insertion point in TryFindBestIngredientsHelper");
                    return codes;
                }

                // Build the instructions to insert
                var instructionsToInsert = new List<CodeInstruction>
                {
                    // Load pawn (from display class stored in local 0)
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Ldfld, pawnField),
                    
                    // Load relevantThings (static field)
                    new CodeInstruction(OpCodes.Ldsfld, relevantThingsField),
                    
                    // Load processedThings (static field)
                    new CodeInstruction(OpCodes.Ldsfld, processedThingsField),
                    
                    // Load thingValidator (from display class stored in local 0)
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Ldfld, thingValidatorField),
                    
                    // Call AddNetworkThings
                    new CodeInstruction(OpCodes.Call, addNetworkThingsMethod)
                };

                codes.InsertRange(insertIndex, instructionsToInsert);

                return codes;
            }
        }

        public static void AddNetworkThings(Pawn pawn, List<Thing> relevantThings, HashSet<Thing> processedThings, Predicate<Thing> thingValidator)
        {
            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();

            foreach (DataNetwork network in mapComp.Networks)
            {
                foreach (Thing thing in network.StoredItems)
                {
                    if (!processedThings.Contains(thing) &&
                        !thing.IsForbidden(pawn) &&
                        pawn.CanReserve(thing) &&
                        thingValidator(thing))
                    {
                        relevantThings.Add(thing);
                        processedThings.Add(thing);
                    }
                }
            }
        }
    }
}