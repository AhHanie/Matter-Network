using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_RecipeDef
    {
        [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.PotentiallyMissingIngredients))]
        public static class PotentiallyMissingIngredients
        {
            public static IEnumerable<ThingDef> Postfix(IEnumerable<ThingDef> values, RecipeDef __instance, Pawn billDoer, Map map)
            {
                foreach (ThingDef thingDef in values)
                {
                    IngredientCount ingredient = FindIngredientForMissingDef(__instance, thingDef);
                    if (ingredient != null && NetworkItemSearchUtility.AnyMatchingThing(map, item => IngredientAllowsItem(item, ingredient, __instance, billDoer)))
                    {
                        continue;
                    }

                    yield return thingDef;
                }
            }

            private static IngredientCount FindIngredientForMissingDef(RecipeDef recipe, ThingDef thingDef)
            {
                for (int i = 0; i < recipe.ingredients.Count; i++)
                {
                    IngredientCount ingredient = recipe.ingredients[i];
                    if (ingredient.IsFixedIngredient)
                    {
                        if (ingredient.filter.AllowedThingDefs.FirstOrDefault() == thingDef)
                        {
                            return ingredient;
                        }
                    }
                    else if (ingredient.filter.AllowedThingDefs.Contains(thingDef) && recipe.fixedIngredientFilter.Allows(thingDef))
                    {
                        return ingredient;
                    }
                }

                return null;
            }

            private static bool IngredientAllowsItem(Thing item, IngredientCount ingredient, RecipeDef recipe, Pawn billDoer)
            {
                if (billDoer != null && item.IsForbidden(billDoer))
                {
                    return false;
                }

                if ((ingredient.IsFixedIngredient || recipe.fixedIngredientFilter.Allows(item)) && ingredient.filter.Allows(item))
                {
                    return true;
                }

                return false;
            }
        }
    }
}
