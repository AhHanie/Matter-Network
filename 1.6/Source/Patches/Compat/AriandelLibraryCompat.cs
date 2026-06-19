using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Reflection;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class AriandelLibraryCompat
    {
        private const string PackageId = "ariandel.ariandellibrary";

        private static readonly Type _workGiverEmptyType =
            AccessTools.TypeByName("AriandelLibrary.WorkGiver_EmptyPeriodicGenerator");
        private static readonly Type _workGiverSelectableType =
            AccessTools.TypeByName("AriandelLibrary.WorkGiver_EmptyPeriodicGenerator_Selectable");
        private static readonly Type _workGiverFillType =
            AccessTools.TypeByName("AriandelLibrary.WorkGiver_FillPeriodicGenerator");
        private static readonly Type _compGeneratorWithResourcesType =
            AccessTools.TypeByName("AriandelLibrary.CompGenerator_Periodic_WithResources");
        private static readonly Type _compPropsWithResourcesType =
            AccessTools.TypeByName("AriandelLibrary.CompProperties_PeriodicGenerator_WithResources");
        private static readonly Type _resourceRequirementType =
            AccessTools.TypeByName("AriandelLibrary.ResourceRequirement");

        private static readonly FieldInfo _allowResourceFillingField =
            AccessTools.Field(_compGeneratorWithResourcesType, "allowResourceFilling");
        private static readonly FieldInfo _resourcesField =
            AccessTools.Field(_compPropsWithResourcesType, "resources");
        private static readonly FieldInfo _resourceDefField =
            AccessTools.Field(_resourceRequirementType, "resourceDef");
        private static readonly FieldInfo _maxResourceInventoryField =
            AccessTools.Field(_resourceRequirementType, "maxResourceInventory");

        private static readonly bool _isFullyAvailable = CheckAvailability();

        private static bool CheckAvailability()
        {
            if (!ModsConfig.IsActive(PackageId))
                return false;

            if (_workGiverEmptyType == null || _workGiverSelectableType == null || _workGiverFillType == null ||
                _compGeneratorWithResourcesType == null || _compPropsWithResourcesType == null || _resourceRequirementType == null ||
                _allowResourceFillingField == null || _resourcesField == null ||
                _resourceDefField == null || _maxResourceInventoryField == null)
            {
                Log.Warning("[Matter Network] Ariandel Library compatibility disabled: expected Ariandel members not found.");
                return false;
            }

            return true;
        }

        // ── Shared helpers ────────────────────────────────────────────────────

        private static bool IsMatterNetworkEndpoint(IHaulDestination dest, out Thing endpointThing)
        {
            endpointThing = dest as NetworkBuildingNetworkInterface;
            if (endpointThing != null) return true;
            endpointThing = dest as NetworkBuildingNetworkChute;
            return endpointThing != null;
        }

        private static void TryRetargetEmptyJobDestination(Pawn pawn, Job job)
        {
            if (job == null) return;
            Thing product = job.GetTarget(TargetIndex.B).Thing;
            if (product == null) return;
            if (!StoreUtility.TryFindBestBetterStorageFor(
                product, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction,
                out IntVec3 foundCell, out IHaulDestination haulDestination))
                return;
            if (foundCell.IsValid) return;
            if (IsMatterNetworkEndpoint(haulDestination, out Thing endpointThing))
                job.SetTarget(TargetIndex.C, endpointThing);
        }

        private static bool CheckFillPreconditions(Pawn pawn, Thing t, bool forced, out ThingComp comp, out CompThingContainer container)
        {
            comp = null;
            container = null;
            ThingComp tempComp = GetCompOfType(t, _compGeneratorWithResourcesType);
            if (tempComp == null) return false;
            container = t.TryGetComp<CompThingContainer>();
            if (container == null || container.Full) return false;
            if (!(bool)_allowResourceFillingField.GetValue(tempComp)) return false;
            if (t.IsForbidden(pawn)) return false;
            if (!pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some, 1, -1, null, forced)) return false;
            comp = tempComp;
            return true;
        }

        private static ThingComp GetCompOfType(Thing t, Type compType)
        {
            if (compType == null) return null;
            ThingWithComps twc = t as ThingWithComps;
            if (twc?.AllComps == null) return null;
            foreach (ThingComp c in twc.AllComps)
                if (compType.IsInstanceOfType(c))
                    return c;
            return null;
        }

        private static IList GetResources(ThingComp comp)
        {
            CompProperties propsBase = comp.props;
            if (propsBase == null) return null;
            return _resourcesField?.GetValue(propsBase) as IList;
        }

        // ── PATCH 1: Empty periodic generator job retargeting ─────────────────
        [HarmonyPatch]
        public static class Patch_WorkGiver_EmptyPeriodicGenerator_JobOnThing
        {
            [HarmonyPrepare]
            public static bool Prepare() => _isFullyAvailable;

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_workGiverEmptyType, "JobOnThing");

            public static void Postfix(Pawn pawn, ref Job __result)
            {
                TryRetargetEmptyJobDestination(pawn, __result);
            }
        }

        // ── PATCH 2: Selectable empty periodic generator job retargeting ──────
        [HarmonyPatch]
        public static class Patch_WorkGiver_EmptyPeriodicGenerator_Selectable_JobOnThing
        {
            [HarmonyPrepare]
            public static bool Prepare() => _isFullyAvailable;

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_workGiverSelectableType, "JobOnThing");

            public static void Postfix(Pawn pawn, ref Job __result)
            {
                TryRetargetEmptyJobDestination(pawn, __result);
            }
        }

        // ── PATCH 3: Fill periodic generator HasJobOnThing ────────────────────
        // Ariandel's HasJobOnThing only considers spawned map items. When the resource
        // exists only in the Matter Network, this postfix returns true so that the
        // scanner advances to JobOnThing where the network ingredient is selected.
        [HarmonyPatch]
        public static class Patch_WorkGiver_FillPeriodicGenerator_HasJobOnThing
        {
            [HarmonyPrepare]
            public static bool Prepare() => _isFullyAvailable;

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_workGiverFillType, "HasJobOnThing");

            public static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
            {
                if (__result) return;
                if (!CheckFillPreconditions(pawn, t, forced, out ThingComp comp, out CompThingContainer container)) return;

                IList resources = GetResources(comp);
                if (resources == null) return;

                foreach (object req in resources)
                {
                    ThingDef resourceDef = _resourceDefField.GetValue(req) as ThingDef;
                    if (resourceDef == null) continue;

                    int maxInventory = (int)_maxResourceInventoryField.GetValue(req);
                    int spaceLeft = maxInventory - container.innerContainer.TotalStackCountOfDef(resourceDef);
                    if (spaceLeft <= 0) continue;

                    Thing found = NetworkItemSearchUtility.FindClosestReachableThing(
                        pawn,
                        item => item.def == resourceDef && NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(pawn, item, forced),
                        out _);

                    if (found != null)
                    {
                        __result = true;
                        return;
                    }
                }
            }
        }

        // ── PATCH 4: Fill periodic generator JobOnThing ───────────────────────
        // When Ariandel returns null (no spawned ingredient found), supply a network
        // ingredient so vanilla JobDriver_HaulToContainer can carry it to the generator.
        [HarmonyPatch]
        public static class Patch_WorkGiver_FillPeriodicGenerator_JobOnThing
        {
            [HarmonyPrepare]
            public static bool Prepare() => _isFullyAvailable;

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_workGiverFillType, "JobOnThing");

            public static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
            {
                if (__result != null) return;
                if (!CheckFillPreconditions(pawn, t, forced, out ThingComp comp, out CompThingContainer container)) return;

                IList resources = GetResources(comp);
                if (resources == null) return;

                foreach (object req in resources)
                {
                    ThingDef resourceDef = _resourceDefField.GetValue(req) as ThingDef;
                    if (resourceDef == null) continue;

                    int maxInventory = (int)_maxResourceInventoryField.GetValue(req);
                    int spaceLeft = maxInventory - container.innerContainer.TotalStackCountOfDef(resourceDef);
                    if (spaceLeft <= 0) continue;

                    Thing networkItem = NetworkItemSearchUtility.FindClosestReachableThing(
                        pawn,
                        item => item.def == resourceDef && NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(pawn, item, forced),
                        out _);

                    if (networkItem == null) continue;

                    JobDef fillJobDef = DefDatabase<JobDef>.GetNamedSilentFail("AL_Job_FillPeriodicGeneratorJob");
                    if (fillJobDef == null) return;

                    Job job = JobMaker.MakeJob(fillJobDef, networkItem, t);
                    job.count = Math.Min(spaceLeft, networkItem.stackCount);
                    __result = job;
                    return;
                }
            }
        }
    }
}
