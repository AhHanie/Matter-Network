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

                var clearMethod = AccessTools.Method(typeof(List<Thing>), "Clear");

                // Find the post-haul-source cleanup:
                //   ldsfld newRelevantThings
                //   callvirt List<Thing>.Clear
                //   ldloc <display class>
                //   ldloc <display class>
                //   ldfld pawn
                // The captured-variable display class is taken from the matched `pawn` field read instead of
                // being looked up by its compiler-generated name.
                int insertIndex = -1;
                FieldInfo pawnField = null;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].LoadsField(newRelevantThingsField) &&
                        i + 1 < codes.Count &&
                        codes[i + 1].Calls(clearMethod) &&
                        i + 4 < codes.Count &&
                        codes[i + 2].opcode == OpCodes.Ldloc_0 &&
                        codes[i + 3].opcode == OpCodes.Ldloc_0 &&
                        codes[i + 4].operand is FieldInfo candidate && candidate.Name == "pawn" &&
                        codes[i + 4].opcode == OpCodes.Ldfld)
                    {
                        insertIndex = i;
                        pawnField = candidate;
                        break;
                    }
                }

                if (insertIndex == -1)
                {
                    Logger.Error("[Matter Network]: Could not find insertion point in TryFindBestIngredientsHelper");
                    return codes;
                }

                Type displayClassType = pawnField.DeclaringType;
                var billGiverField = AccessTools.Field(displayClassType, "billGiver");
                var searchRadiusField = AccessTools.Field(displayClassType, "searchRadius");
                var thingValidatorField = AccessTools.Field(displayClassType, "thingValidator");
                if (billGiverField == null || searchRadiusField == null || thingValidatorField == null)
                {
                    Logger.Error($"[Matter Network]: Display class {displayClassType} in TryFindBestIngredientsHelper is missing an expected captured field");
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
                Logger.Message($"[Matter Network]: Patched WorkGiver_DoBill.TryFindBestIngredientsHelper transpiler successfully at instruction {insertIndex}.");

                return codes;
            }
        }

        public static void AddNetworkThings(Pawn pawn, Thing billGiver, float searchRadius, List<Thing> relevantThings, HashSet<Thing> processedThings, Predicate<Thing> thingValidator)
        {
            NetworkBillIngredientUtility.AddNetworkIngredientCandidates(pawn, billGiver, searchRadius, relevantThings, processedThings, thingValidator);
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
