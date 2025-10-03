using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Xenogerm
    {
        [HarmonyPatch(typeof(Xenogerm), "SendImplantationLetter")]
        public static class SendImplantationLetter
        {
            public static bool Prefix(Pawn targetPawn)
            {
                string bedInfo = string.Empty;
                if (!targetPawn.InBed() && !targetPawn.Map.listerBuildings.allBuildingsColonist.Any(x => x is Building_Bed bed && RestUtility.CanUseBedEver(targetPawn, bed.def) && bed.Medical))
                {
                    bedInfo = "XenogermOrderedImplantedBedNeeded".Translate(targetPawn.Named("PAWN"));
                }

                int requiredMedicineForImplanting = GetRequiredMedicineForImplanting();
                int medicineCount = targetPawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).Sum(x => x.stackCount) + NetworkItemSearchUtility.CountMatchingThingStacks(targetPawn.Map, item => item.def.IsMedicine);
                string medicineInfo = string.Empty;
                if (medicineCount < requiredMedicineForImplanting)
                {
                    medicineInfo = "XenogermOrderedImplantedMedicineNeeded".Translate(requiredMedicineForImplanting.Named("MEDICINENEEDED"));
                }

                Find.LetterStack.ReceiveLetter(
                    "LetterLabelXenogermOrderedImplanted".Translate(),
                    "LetterXenogermOrderedImplanted".Translate(targetPawn.Named("PAWN"), requiredMedicineForImplanting.Named("MEDICINENEEDED"), bedInfo.Named("BEDINFO"), medicineInfo.Named("MEDICINEINFO")),
                    LetterDefOf.NeutralEvent);
                return false;
            }

            private static int GetRequiredMedicineForImplanting()
            {
                int num = 0;
                for (int i = 0; i < RecipeDefOf.ImplantXenogerm.ingredients.Count; i++)
                {
                    IngredientCount ingredientCount = RecipeDefOf.ImplantXenogerm.ingredients[i];
                    if (ingredientCount.filter.Allows(ThingDefOf.MedicineIndustrial))
                    {
                        num += (int)ingredientCount.GetBaseCount();
                    }
                }

                return num;
            }
        }
    }
}
