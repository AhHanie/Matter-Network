using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class DragonsDescentCompat
    {
        private const string PackageId = "onyxae.dragonsdescent";
        private const string RecipeDefName = "HaulIncubateDragonEgg";
        private const string WorkGiverDefName = "DoBillsHaulDragonEggs";
        private static readonly string[] IncubatorDefNames = { "BasicIncubator", "AdvancedIncubator" };

        private static readonly System.Type billLoadIncubatorType =
            AccessTools.TypeByName("DD.Bill_LoadIncubator");

        private static readonly RecipeDef recipeDef =
            DefDatabase<RecipeDef>.GetNamedSilentFail(RecipeDefName);
        private static readonly WorkGiverDef workGiverDef =
            DefDatabase<WorkGiverDef>.GetNamedSilentFail(WorkGiverDefName);
        private static readonly HashSet<ThingDef> incubatorDefs =
            new HashSet<ThingDef>(IncubatorDefNames
                .Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .Where(def => def != null));

        private static bool warnedMissingApi;
        private static Bill capturedBill;
        private static bool capturedEligible;

        private static bool IsAvailable()
        {
            if (recipeDef != null && workGiverDef != null && billLoadIncubatorType != null
                && incubatorDefs.Count == IncubatorDefNames.Length)
            {
                return true;
            }

            if (ModsConfig.IsActive(PackageId) && !warnedMissingApi)
            {
                warnedMissingApi = true;
                Logger.Warning(
                    "Dragons Descent is active but its expected members (" + RecipeDefName + "/" +
                    WorkGiverDefName + " defs, incubator defs, or DD.Bill_LoadIncubator) could not be " +
                    "resolved (API likely changed). Network-held dragon eggs will only be usable for " +
                    "incubator bills if Matter Network's generic bill-ingredient compatibility still " +
                    "applies; the dedicated incubator fallback is disabled.");
            }

            return false;
        }

        [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
        public static class Patch_JobOnThing
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static void Prefix(WorkGiver_DoBill __instance, Pawn pawn, Thing thing)
            {
                capturedBill = null;
                capturedEligible = false;

                if (__instance == null || __instance.def != workGiverDef) return;
                if (!(thing is IBillGiver billGiver) || !incubatorDefs.Contains(thing.def)) return;

                for (int i = 0; i < billGiver.BillStack.Count; i++)
                {
                    Bill candidate = billGiver.BillStack[i];
                    if (candidate.recipe != recipeDef) continue;
                    if (!billLoadIncubatorType.IsInstanceOfType(candidate)) continue;

                    capturedBill = candidate;
                    capturedEligible = FloatMenuMakerMap.makingFor == pawn
                        || Find.TickManager.TicksGame > candidate.nextTickToSearchForIngredients;
                    break;
                }
            }

            public static void Postfix(WorkGiver_DoBill __instance, Pawn pawn, Thing thing, bool forced, ref Job __result)
            {
                Bill snapshotBill = capturedBill;
                bool eligible = capturedEligible;
                capturedBill = null;

                if (__result != null) return;
                if (__instance == null || __instance.def != workGiverDef) return;
                if (!eligible || snapshotBill == null) return;

                if (!TryGetLoadableIncubatorBill(__instance, pawn, thing, forced, snapshotBill, out IBillGiver billGiver, out Bill bill))
                {
                    return;
                }

                if (!TryFindNetworkEgg(pawn, bill, out Thing egg))
                {
                    return;
                }

                List<ThingCount> chosenIngThings = new List<ThingCount> { new ThingCount(egg, 1) };
                Job job = WorkGiver_DoBill.TryStartNewDoBillJob(pawn, bill, billGiver, chosenIngThings, out Job _);
                __result = job;

                if (job != null)
                {
                    Logger.Message("[Dragons Descent] Fallback incubator job: " + pawn.LabelShort +
                        " loading " + egg.LabelShort + " into " + thing.LabelShort + " from the network.");
                }
            }
        }

        private static bool TryGetLoadableIncubatorBill(
            WorkGiver_DoBill workGiver, Pawn pawn, Thing thing, bool forced, Bill candidateBill,
            out IBillGiver billGiver, out Bill bill)
        {
            billGiver = null;
            bill = null;

            if (!(thing is IBillGiver giver) || !incubatorDefs.Contains(thing.def)) return false;
            if (thing.IsForbidden(pawn) || thing.IsBurning()) return false;
            if (thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Deconstruct) != null) return false;
            if (!giver.BillStack.AnyShouldDoNow) return false;
            if (!giver.UsableForBillsAfterFueling()) return false;
            if (!pawn.CanReserve(thing, 1, -1, null, forced)) return false;
            if (thing.def.hasInteractionCell && !pawn.CanReserveSittableOrSpot(thing.InteractionCell, thing, forced)) return false;

            CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
            if (refuelable != null && !refuelable.HasFuel) return false;

            if (!candidateBill.ShouldDoNow()) return false;
            if (!candidateBill.PawnAllowedToStartAnew(pawn)) return false;
            if (candidateBill.recipe.requiredGiverWorkType != null
                && candidateBill.recipe.requiredGiverWorkType != workGiver.def.workType)
            {
                return false;
            }

            billGiver = giver;
            bill = candidateBill;
            return true;
        }

        private static bool TryFindNetworkEgg(Pawn pawn, Bill bill, out Thing egg)
        {
            egg = null;
            float bestDistanceSquared = float.MaxValue;

            foreach (DataNetwork network in NetworkItemSearchUtility.Networks(pawn.Map))
            {
                float distanceSquared = NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn.Position, pawn, network);
                if (distanceSquared == float.MaxValue) continue;

                foreach (Thing item in network.StoredItems)
                {
                    if (item == null || item.Destroyed || item.stackCount <= 0) continue;
                    if (!bill.IsFixedOrAllowedIngredient(item)) continue;
                    if (!NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(
                        pawn, item, forced: false, NetworkBillIngredientUtility.SharedIngredientReservationMaxPawns))
                    {
                        continue;
                    }

                    bool closer = distanceSquared < bestDistanceSquared;
                    bool tiedButLowerId = distanceSquared == bestDistanceSquared && (egg == null || item.thingIDNumber < egg.thingIDNumber);
                    if (!closer && !tiedButLowerId) continue;

                    bestDistanceSquared = distanceSquared;
                    egg = item;
                }
            }

            return egg != null;
        }
    }
}
