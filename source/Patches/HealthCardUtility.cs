using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_HealthCardUtility
    {
        [HarmonyPatch(typeof(HealthCardUtility), "CanDoRecipeWithMedicineRestriction")]
        public static class CanDoRecipeWithMedicineRestriction
        {
            public static void Postfix(IBillGiver giver, RecipeDef recipe, ref bool __result)
            {
                Pawn pawn = giver as Pawn;
                if (__result || pawn == null || pawn.playerSettings == null)
                {
                    return;
                }

                if (!recipe.ingredients.Any(x => x.filter.AnyAllowedDef.IsMedicine))
                {
                    return;
                }

                MedicalCareCategory medicalCareCategory = WorkGiver_DoBill.GetMedicalCareCategory(pawn);
                if (NetworkItemSearchUtility.AnyMatchingThing(pawn.MapHeld, item => AllowsMedicineForRecipe(item, recipe, medicalCareCategory)))
                {
                    __result = true;
                }
            }

            private static bool AllowsMedicineForRecipe(Thing item, RecipeDef recipe, MedicalCareCategory medicalCareCategory)
            {
                if (!medicalCareCategory.AllowsMedicine(item.def))
                {
                    return false;
                }

                foreach (IngredientCount ingredient in recipe.ingredients)
                {
                    if (ingredient.filter.Allows(item))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
