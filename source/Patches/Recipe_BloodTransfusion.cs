using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Recipe_BloodTransfusion
    {
        [HarmonyPatch(typeof(Recipe_BloodTransfusion), nameof(Recipe_BloodTransfusion.AvailableOnNow))]
        public static class AvailableOnNow
        {
            public static void Postfix(Thing thing, ref bool __result)
            {
                if (__result || thing.MapHeld == null)
                {
                    return;
                }

                if (NetworkItemSearchUtility.AnyMatchingThing(thing.MapHeld, item => item.def == ThingDefOf.HemogenPack))
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(Recipe_BloodTransfusion), nameof(Recipe_BloodTransfusion.GetIngredientCount))]
        public static class GetIngredientCount
        {
            public static void Postfix(Bill bill, ref float __result)
            {
                if (!(bill.billStack?.billGiver is Pawn pawn))
                {
                    return;
                }

                Hediff firstHediffOfDef = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
                if (firstHediffOfDef == null)
                {
                    return;
                }

                int networkCount = NetworkItemSearchUtility.CountMatchingThingStacks(bill.Map, item => item.def == ThingDefOf.HemogenPack);
                if (networkCount > 0)
                {
                    __result = Mathf.Min(__result + networkCount, firstHediffOfDef.Severity / 0.35f);
                }
            }
        }
    }
}
