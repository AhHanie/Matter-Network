using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
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
                var billGiverField = AccessTools.Field(displayClassType, "billGiver");
                var searchRadiusField = AccessTools.Field(displayClassType, "searchRadius");
                var thingValidatorField = AccessTools.Field(displayClassType, "thingValidator");
                var clearMethod = AccessTools.Method(typeof(List<Thing>), "Clear");

                // Find the post-haul-source cleanup:
                //   ldsfld newRelevantThings
                //   callvirt List<Thing>.Clear
                //   ldloc.0
                //   ldloc.0
                //   ldfld pawn
                // This corresponds to IL_029a in the provided method body.
                int insertIndex = -1;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].LoadsField(newRelevantThingsField) &&
                        i + 1 < codes.Count &&
                        codes[i + 1].Calls(clearMethod) &&
                        i + 4 < codes.Count &&
                        codes[i + 2].opcode == OpCodes.Ldloc_0 &&
                        codes[i + 3].opcode == OpCodes.Ldloc_0 &&
                        codes[i + 4].LoadsField(pawnField))
                    {
                        insertIndex = i;
                        break;
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

                    // Load billGiver (from display class stored in local 0)
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Ldfld, billGiverField),

                    // Load searchRadius (from display class stored in local 0)
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Ldfld, searchRadiusField),
                    
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

                instructionsToInsert[0].labels.AddRange(codes[insertIndex].labels);
                codes[insertIndex].labels.Clear();
                instructionsToInsert[0].blocks.AddRange(codes[insertIndex].blocks);
                codes[insertIndex].blocks.Clear();

                codes.InsertRange(insertIndex, instructionsToInsert);
                Log.Message($"[Matter Network]: Patched WorkGiver_DoBill.TryFindBestIngredientsHelper transpiler successfully at instruction {insertIndex}.");

                return codes;
            }
        }

        public static void AddNetworkThings(Pawn pawn, Thing billGiver, float searchRadius, List<Thing> relevantThings, HashSet<Thing> processedThings, Predicate<Thing> thingValidator)
        {
            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            float radiusSq = searchRadius * searchRadius;

            foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
            {
                if (!HasReachableInterfaceInRadius(pawn, billGiver, searchRadius, radiusSq, network))
                {
                    continue;
                }

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

        private static bool HasReachableInterfaceInRadius(Pawn pawn, Thing billGiver, float searchRadius, float radiusSq, DataNetwork network)
        {
            foreach (NetworkBuildingNetworkInterface interf in network.NetworkInterfaces)
            {
                if (!interf.Position.InHorDistOf(billGiver.Position, searchRadius))
                {
                    continue;
                }

                if ((interf.Position - billGiver.Position).LengthHorizontalSquared > radiusSq)
                {
                    continue;
                }

                if (pawn.Map.reachability.CanReach(pawn.Position, interf.InteractionCell, PathEndMode.OnCell, TraverseParms.For(pawn)))
                {
                    return true;
                }
            }

            return false;
        }

        [HarmonyPatch(typeof(WorkGiver_DoBill), "CannotDoBillDueToMedicineRestriction")]
        public static class CannotDoBillDueToMedicineRestriction
        {
            public static void Postfix(IBillGiver giver, Bill bill, List<IngredientCount> missingIngredients, ref bool __result)
            {
                if (!__result || !(giver is Pawn pawn))
                {
                    return;
                }

                if (!missingIngredients.Any(x => x.filter.Allows(ThingDefOf.MedicineIndustrial)))
                {
                    return;
                }

                MedicalCareCategory medicalCareCategory = RimWorld.WorkGiver_DoBill.GetMedicalCareCategory(pawn);
                if (NetworkItemSearchUtility.AnyMatchingThing(pawn.Map, item => IsUsableIngredientForBill(item, bill) && medicalCareCategory.AllowsMedicine(item.def)))
                {
                    __result = false;
                }
            }
        }

        private static bool IsUsableIngredientForBill(Thing thing, Bill bill)
        {
            if (!bill.IsFixedOrAllowedIngredient(thing))
            {
                return false;
            }

            for (int i = 0; i < bill.recipe.ingredients.Count; i++)
            {
                if (bill.recipe.ingredients[i].filter.Allows(thing))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
