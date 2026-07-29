using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    // Composite Loadouts (Wiri.compositableloadouts) resolves its required equipment via
    // Inventory.Item.ThingsOnMap(Map), which only enumerates map.listerThings.ThingsOfDef,
    // and its apparel path via map.listerThings.ThingsInGroup(Apparel). Matter Network's
    // items are held unspawned inside the active controller's ControllerItemOwner, so
    // neither lister ever sees them - none of Matter Network's own GenClosest/ListerThings
    // patches help here, since Composite Loadouts never calls those RimWorld helpers.
    //
    // Verified against the mod's public source (simplyWiri/Loadout-Compositing,
    // Source/AI/ThinkNode_LoadoutRealisation.cs and Source/Utility/Utility.cs).
    public static class CompositeLoadoutsCompat
    {
        private const string PackageId = "wiri.compositableloadouts";

        private static readonly Type ItemType = AccessTools.TypeByName("Inventory.Item");
        private static readonly Type LoadoutType = AccessTools.TypeByName("Inventory.Loadout");
        private static readonly Type ThinkNodeLoadoutRealisationType =
            AccessTools.TypeByName("Inventory.ThinkNode_LoadoutRealisation");
        private static readonly Type UtilityType = AccessTools.TypeByName("Inventory.Utility");

        private static readonly MethodInfo ItemAllowsMethod =
            AccessTools.Method(ItemType, "Allows", new[] { typeof(Thing) });
        private static readonly MethodInfo ItemThingsOnMapMethod =
            AccessTools.Method(ItemType, "ThingsOnMap", new[] { typeof(Map) });
        private static readonly MethodInfo SatisfyLoadoutClothingJobMethod =
            LoadoutType != null && ThinkNodeLoadoutRealisationType != null
                ? AccessTools.Method(ThinkNodeLoadoutRealisationType, "SatisfyLoadoutClothingJob", new[] { typeof(Pawn), LoadoutType })
                : null;
        private static readonly PropertyInfo LoadoutItemsProperty =
            LoadoutType != null ? AccessTools.Property(LoadoutType, "Items") : null;
        private static readonly MethodInfo ShouldAttemptToEquipMethod =
            UtilityType != null
                ? AccessTools.Method(UtilityType, "ShouldAttemptToEquip", new[] { typeof(Pawn), typeof(Thing), typeof(bool) })
                : null;

        private static readonly MethodInfo ApparelScoreGainMethod =
            AccessTools.Method(typeof(JobGiver_OptimizeApparel), "ApparelScoreGain");
        private static readonly FieldInfo NeededWarmthField =
            AccessTools.Field(typeof(JobGiver_OptimizeApparel), "neededWarmth");
        private static readonly MethodInfo ThingInteractionCellGetter =
            AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.InteractionCell));

        private static bool _warnedMissingApi;

        private static bool IsAvailable()
        {
            return ItemType != null
                && LoadoutType != null
                && ThinkNodeLoadoutRealisationType != null
                && UtilityType != null
                && ItemAllowsMethod != null
                && ItemThingsOnMapMethod != null
                && SatisfyLoadoutClothingJobMethod != null
                && LoadoutItemsProperty != null
                && ShouldAttemptToEquipMethod != null
                && ApparelScoreGainMethod != null
                && NeededWarmthField != null
                && ThingInteractionCellGetter != null;
        }

        private static bool CheckAvailable()
        {
            bool available = IsAvailable();
            if (ModsConfig.IsActive(PackageId) && !available && !_warnedMissingApi)
            {
                _warnedMissingApi = true;
                Logger.Warning("[Composite Loadouts Compat] Composite Loadouts is loaded but expected API was not found. Network inventory compat disabled.");
            }
            return available;
        }

        // Postfix: append network items to Item.ThingsOnMap so Composite Loadouts' non-apparel
        // resolver (FindItem) can see and select them, respecting the item's own def/stuff/
        // quality/hitpoint Filter. Reachability, reservation, forbiddance, and haul toils for
        // the resulting TakeInventory/Equip job are already covered by Matter Network's
        // existing generic patches (ForbidUtility, Reachability, Toils_Goto, Toils_Haul,
        // JobDriver_TakeInventory, JobDriver_Equip).
        [HarmonyPatch]
        public static class Patch_ThingsOnMap
        {
            [HarmonyPrepare]
            public static bool Prepare() => CheckAvailable();

            public static MethodBase TargetMethod() => ItemThingsOnMapMethod;

            public static IEnumerable<Thing> Postfix(IEnumerable<Thing> values, object __instance, Map map)
            {
                HashSet<Thing> yielded = new HashSet<Thing>();
                if (values != null)
                {
                    foreach (Thing t in values)
                    {
                        if (t != null && yielded.Add(t))
                        {
                            yield return t;
                        }
                    }
                }

                if (map == null)
                {
                    yield break;
                }

                foreach (Thing t in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (t == null || t.Destroyed || yielded.Contains(t))
                    {
                        continue;
                    }

                    if (!(bool)ItemAllowsMethod.Invoke(__instance, new object[] { t }))
                    {
                        continue;
                    }

                    yielded.Add(t);
                    yield return t;
                }
            }
        }

        // FindItem sorts single-item candidates via DecideItemPriority, whose final fallback
        // sorts by t.InteractionCell.DistanceToSquared(pawn.InteractionCell). That's a lambda
        // capturing `pawn`, so the compiler emits it as its own method on a generated display
        // class (Inventory.ThinkNode_LoadoutRealisation+<>c__DisplayClassNN_0.<DecideItemPriority>b__2),
        // not as part of DecideItemPriority's own IL - patching DecideItemPriority itself replaces
        // nothing. InteractionCell resolves from the thing's raw Position/Map, which are
        // invalid/null for our unspawned network items, and crashes with a NullReferenceException
        // deep in GenGrid.Walkable. Rather than patch the (extremely hot) vanilla
        // Thing.InteractionCell getter globally, find that specific generated lambda by scanning
        // ThinkNode_LoadoutRealisation's nested display-class methods for the one that actually
        // calls InteractionCell, and replace just that call site with a safe equivalent.
        [HarmonyPatch]
        public static class Patch_DecideItemPriorityLambda
        {
            [HarmonyPrepare]
            public static bool Prepare() => CheckAvailable() && FindDecideItemPriorityLambda() != null;

            public static MethodBase TargetMethod() => FindDecideItemPriorityLambda();

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo replacement = AccessTools.Method(typeof(CompositeLoadoutsCompat), nameof(SafeInteractionCell));

                int replaced = 0;
                foreach (CodeInstruction instr in instructions)
                {
                    if (instr.Calls(ThingInteractionCellGetter))
                    {
                        replaced++;
                        yield return new CodeInstruction(OpCodes.Call, replacement);
                        continue;
                    }

                    yield return instr;
                }

                if (replaced == 0)
                {
                    Logger.Warning("[Composite Loadouts Compat] DecideItemPriority lambda transpiler made no replacements; network item sorting may crash.");
                }
            }
        }

        private static MethodInfo _decideItemPriorityLambda;
        private static bool _decideItemPriorityLambdaSearched;

        private static MethodInfo FindDecideItemPriorityLambda()
        {
            if (_decideItemPriorityLambdaSearched)
            {
                return _decideItemPriorityLambda;
            }
            _decideItemPriorityLambdaSearched = true;

            if (ThinkNodeLoadoutRealisationType == null || ThingInteractionCellGetter == null)
            {
                return null;
            }

            foreach (Type nested in ThinkNodeLoadoutRealisationType.GetNestedTypes(AccessTools.all))
            {
                foreach (MethodInfo method in nested.GetMethods(AccessTools.all))
                {
                    if (!method.Name.Contains("<DecideItemPriority>b__"))
                    {
                        continue;
                    }

                    if (method.GetMethodBody() == null)
                    {
                        continue;
                    }

                    foreach (CodeInstruction instr in PatchProcessor.GetCurrentInstructions(method))
                    {
                        if (instr.Calls(ThingInteractionCellGetter))
                        {
                            _decideItemPriorityLambda = method;
                            return method;
                        }
                    }
                }
            }

            return null;
        }

        public static IntVec3 SafeInteractionCell(Thing t)
        {
            if (t.Spawned)
            {
                return t.InteractionCell;
            }

            Map map = t.MapHeld;
            if (map == null)
            {
                return t.PositionHeld;
            }

            return ThingUtility.InteractionCellWhenAt(t.def, t.PositionHeld, t.Rotation, map);
        }

        // Postfix: when Composite Loadouts' own clothing search (over listerThings.ThingsInGroup)
        // finds nothing - it never will for network apparel, since network items are never spawned -
        // fall back to a network-aware search reproducing SatisfyLoadoutClothingJob/ValidApparelFor's
        // own selection (outfit policy + loadout item filter + ShouldAttemptToEquip + ApparelScoreGain).
        [HarmonyPatch]
        public static class Patch_SatisfyLoadoutClothingJob
        {
            [HarmonyPrepare]
            public static bool Prepare() => CheckAvailable();

            public static MethodBase TargetMethod() => SatisfyLoadoutClothingJobMethod;

            public static void Postfix(ref Job __result, Pawn pawn, object loadout)
            {
                if (__result != null)
                {
                    return;
                }

                __result = TryFindNetworkApparelJob(pawn, loadout);
            }
        }

        private static Job TryFindNetworkApparelJob(Pawn pawn, object loadout)
        {
            if (pawn?.Map == null || pawn.outfits == null || pawn.apparel == null)
            {
                return null;
            }

            IEnumerable loadoutItems = LoadoutItemsProperty.GetValue(loadout) as IEnumerable;
            if (loadoutItems == null)
            {
                return null;
            }

            NeededWarmthField.SetValue(null, PawnApparelGenerator.CalculateNeededWarmth(
                pawn, pawn.Map.TileInfo.tile, GenLocalDate.Twelfth(pawn)));

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            List<float> wornApparelScores = new List<float>(wornApparel.Count);
            for (int i = 0; i < wornApparel.Count; i++)
            {
                wornApparelScores.Add(JobGiver_OptimizeApparel.ApparelScoreRaw(pawn, wornApparel[i]));
            }

            Apparel best = null;
            float bestScore = 0f;

            foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(pawn.Map))
            {
                if (!(thing is Apparel apparel) || apparel.Destroyed)
                {
                    continue;
                }

                if (!AnyLoadoutItemAllows(loadoutItems, apparel))
                {
                    continue;
                }

                if (!ValidNetworkApparelFor(apparel, pawn))
                {
                    continue;
                }

                float score = (float)ApparelScoreGainMethod.Invoke(null, new object[] { pawn, apparel, wornApparelScores });
                if (score < 0.05f || score < bestScore)
                {
                    continue;
                }

                if (!pawn.CanReserveAndReach(apparel, PathEndMode.OnCell, pawn.NormalMaxDanger()))
                {
                    continue;
                }

                best = apparel;
                bestScore = score;
            }

            return best != null ? JobMaker.MakeJob(JobDefOf.Wear, best) : null;
        }

        private static bool AnyLoadoutItemAllows(IEnumerable loadoutItems, Apparel apparel)
        {
            foreach (object item in loadoutItems)
            {
                if (item != null && (bool)ItemAllowsMethod.Invoke(item, new object[] { apparel }))
                {
                    return true;
                }
            }

            return false;
        }

        // Mirrors Composite Loadouts' own ValidApparelFor (outfit policy, gender, HasPartsToWear,
        // ShouldAttemptToEquip), substituting network extraction viability for a plain reach check.
        private static bool ValidNetworkApparelFor(Apparel apparel, Pawn pawn)
        {
            if (!pawn.outfits.CurrentApparelPolicy.filter.Allows(apparel))
            {
                return false;
            }

            if (apparel.def.apparel.gender != Gender.None && apparel.def.apparel.gender != pawn.gender)
            {
                return false;
            }

            if (!ApparelUtility.HasPartsToWear(pawn, apparel.def))
            {
                return false;
            }

            if (!NetworkItemSearchUtility.IsUsableNetworkItemForExtraction(pawn, apparel, out _))
            {
                return false;
            }

            return (bool)ShouldAttemptToEquipMethod.Invoke(null, new object[] { pawn, apparel, false });
        }
    }
}
