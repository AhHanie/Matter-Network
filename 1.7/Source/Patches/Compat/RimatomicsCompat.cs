using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class RimatomicsCompat
    {
        private const string PackageId = "Dubwise.Rimatomics";

        // --- Punisher railgun magazine (WorkGiver_LoadMagazine.FindAmmo) ---

        private static readonly Type workGiverLoadMagazineType =
            AccessTools.TypeByName("Rimatomics.WorkGiver_LoadMagazine");
        private static readonly Type buildingRailgunType =
            AccessTools.TypeByName("Rimatomics.Building_Railgun");

        private static readonly MethodInfo findAmmoMethod =
            AccessTools.Method(workGiverLoadMagazineType, "FindAmmo");
        private static readonly FieldInfo railgunMagazineField =
            AccessTools.Field(buildingRailgunType, "magazine");
        private static readonly FieldInfo railgunGunField =
            AccessTools.Field(buildingRailgunType, "gun");
        private static readonly PropertyInfo railgunMagazineCapProperty =
            AccessTools.Property(buildingRailgunType, "MagazineCap");

        private static bool _warnedMissingPunisherApi;

        private static bool IsPunisherAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && workGiverLoadMagazineType != null
                && buildingRailgunType != null
                && findAmmoMethod != null
                && railgunMagazineField != null
                && railgunGunField != null
                && railgunMagazineCapProperty != null;
        }

        // --- Reactor fuel/MOX module loading (WorkGiver_LoadFuelModule.FindAssembly) ---

        private static readonly Type workGiverLoadFuelModuleType =
            AccessTools.TypeByName("Rimatomics.WorkGiver_LoadFuelModule");
        private static readonly Type reactorCoreType =
            AccessTools.TypeByName("Rimatomics.reactorCore");
        private static readonly Type itemNuclearFuelType =
            AccessTools.TypeByName("Rimatomics.Item_NuclearFuel");

        private static readonly MethodInfo findAssemblyFuelModuleMethod =
            AccessTools.Method(workGiverLoadFuelModuleType, "FindAssembly");
        private static readonly MethodInfo reactorCoreGetStoreSettingsMethod =
            AccessTools.Method(reactorCoreType, "GetStoreSettings");
        private static readonly PropertyInfo reactorCoreFuelLifeFilterProperty =
            AccessTools.Property(reactorCoreType, "FuelLifeFilter");
        private static readonly PropertyInfo nuclearFuelMoxProperty =
            AccessTools.Property(itemNuclearFuelType, "MOX");
        private static readonly FieldInfo nuclearFuelFuelLevelField =
            AccessTools.Field(itemNuclearFuelType, "FuelLevel");

        private static bool _warnedMissingFuelModuleApi;

        private static bool IsFuelModuleAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && workGiverLoadFuelModuleType != null
                && reactorCoreType != null
                && itemNuclearFuelType != null
                && findAssemblyFuelModuleMethod != null
                && reactorCoreGetStoreSettingsMethod != null
                && reactorCoreFuelLifeFilterProperty != null
                && nuclearFuelMoxProperty != null
                && nuclearFuelFuelLevelField != null;
        }

        // --- Plutonium processor spent-fuel loading (WorkGiver_LoadPlutoniumProc.FindAssembly) ---

        private static readonly Type workGiverLoadPlutoniumProcType =
            AccessTools.TypeByName("Rimatomics.WorkGiver_LoadPlutoniumProc");
        private static readonly Type buildingPlutoniumProcType =
            AccessTools.TypeByName("Rimatomics.Building_PlutoniumProc");

        private static readonly MethodInfo findAssemblyPlutoniumProcMethod =
            AccessTools.Method(workGiverLoadPlutoniumProcType, "FindAssembly");
        private static readonly PropertyInfo plutoniumProcFuelLifeFilterProperty =
            AccessTools.Property(buildingPlutoniumProcType, "FuelLifeFilter");
        private static readonly PropertyInfo nuclearFuelReprocessableProperty =
            AccessTools.Property(itemNuclearFuelType, "Reprocessable");

        private static bool _warnedMissingPlutoniumProcApi;

        private static bool IsPlutoniumProcAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && workGiverLoadPlutoniumProcType != null
                && buildingPlutoniumProcType != null
                && itemNuclearFuelType != null
                && findAssemblyPlutoniumProcMethod != null
                && plutoniumProcFuelLifeFilterProperty != null
                && nuclearFuelReprocessableProperty != null
                && nuclearFuelFuelLevelField != null;
        }

        // Postfix: Rimatomics' FindAmmo only searches the map (ThingRequest.ForUndefined with a
        // custom map-only search set), so Matter Network's generic single-def ClosestThingReachable
        // patch never sees it. Search network storage directly and take it if closer (or the only option).
        [HarmonyPatch]
        public static class Patch_FindAmmo
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                bool available = IsPunisherAvailable();
                if (ModsConfig.IsActive(PackageId) && !available && !_warnedMissingPunisherApi)
                {
                    _warnedMissingPunisherApi = true;
                    Logger.Warning("[Rimatomics Compat] Dubs Rimatomics is loaded but expected API was not found. Punisher railgun network ammo compat disabled.");
                }
                return available;
            }

            public static MethodBase TargetMethod()
            {
                if (!IsPunisherAvailable()) return null;
                return findAmmoMethod;
            }

            // railgun declared as object because Building_Railgun is a Rimatomics type; Harmony matches by name.
            public static void Postfix(Pawn pawn, object railgun, ref Thing __result)
            {
                Thing gun = railgunGunField.GetValue(railgun) as Thing;
                CompChangeableProjectile comp = gun?.TryGetComp<CompChangeableProjectile>();
                ThingFilter filter = comp?.allowedShellsSettings?.filter;
                if (filter == null)
                {
                    return;
                }

                int capacity = (int)railgunMagazineCapProperty.GetValue(railgun, null);
                List<ThingDef> magazine = railgunMagazineField.GetValue(railgun) as List<ThingDef>;
                int desiredCount = Math.Max(1, capacity - (magazine?.Count ?? 0));

                float currentBestDistanceSquared = __result != null
                    ? NetworkItemSearchUtility.GetThingDistanceSquared(pawn, __result)
                    : float.MaxValue;

                Thing bestNetworkAmmo = NetworkItemSearchUtility.FindClosestReachableThing(
                    pawn,
                    item => filter.Allows(item)
                        && NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(pawn, item, forced: false)
                        && NetworkItemSearchUtility.GetReservableNetworkStackCount(pawn, item, 1, desiredCount) > 0,
                    out float bestDistanceSquared);

                if (bestNetworkAmmo != null && (__result == null || bestDistanceSquared < currentBestDistanceSquared))
                {
                    __result = bestNetworkAmmo;
                }
            }
        }

        // Postfix: WorkGiver_LoadFuelModule.FindAssembly searches map fuel rods via
        // ThingRequest.ForGroup(HaulableEver) plus a custom search set (DubDef.NuclearFuel), which the
        // generic single-def ClosestThingReachable patch does not cover. Mirror Rimatomics' own predicate
        // (store settings, MOX flag, fuel-life filter) against network storage.
        [HarmonyPatch]
        public static class Patch_FindAssemblyFuelModule
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                bool available = IsFuelModuleAvailable();
                if (ModsConfig.IsActive(PackageId) && !available && !_warnedMissingFuelModuleApi)
                {
                    _warnedMissingFuelModuleApi = true;
                    Logger.Warning("[Rimatomics Compat] Dubs Rimatomics is loaded but expected reactor fuel module API was not found. Reactor fuel network compat disabled.");
                }
                return available;
            }

            public static MethodBase TargetMethod()
            {
                if (!IsFuelModuleAvailable()) return null;
                return findAssemblyFuelModuleMethod;
            }

            // core declared as object because reactorCore is a Rimatomics type; Harmony matches by name.
            public static void Postfix(Pawn pawn, object core, bool MOX, ref Thing __result)
            {
                StorageSettings settings = reactorCoreGetStoreSettingsMethod.Invoke(core, null) as StorageSettings;
                if (settings == null)
                {
                    return;
                }

                FloatRange fuelLifeFilter = (FloatRange)reactorCoreFuelLifeFilterProperty.GetValue(core, null);

                float currentBestDistanceSquared = __result != null
                    ? NetworkItemSearchUtility.GetThingDistanceSquared(pawn, __result)
                    : float.MaxValue;

                Thing bestNetworkFuel = NetworkItemSearchUtility.FindClosestReachableThing(
                    pawn,
                    item => itemNuclearFuelType.IsInstanceOfType(item)
                        && (bool)nuclearFuelMoxProperty.GetValue(item, null) == MOX
                        && settings.AllowedToAccept(item)
                        && fuelLifeFilter.Includes((float)nuclearFuelFuelLevelField.GetValue(item))
                        && NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(pawn, item, forced: false),
                    out float bestDistanceSquared);

                if (bestNetworkFuel != null && (__result == null || bestDistanceSquared < currentBestDistanceSquared))
                {
                    __result = bestNetworkFuel;
                }
            }
        }

        // Postfix: WorkGiver_LoadPlutoniumProc.FindAssembly searches map fuel rods via
        // ThingRequest.ForUndefined() plus the same DubDef.NuclearFuel search set, so it hits the same
        // gap as the fuel module search. Mirror Rimatomics' predicate (Reprocessable, fuel-life filter).
        [HarmonyPatch]
        public static class Patch_FindAssemblyPlutoniumProc
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                bool available = IsPlutoniumProcAvailable();
                if (ModsConfig.IsActive(PackageId) && !available && !_warnedMissingPlutoniumProcApi)
                {
                    _warnedMissingPlutoniumProcApi = true;
                    Logger.Warning("[Rimatomics Compat] Dubs Rimatomics is loaded but expected plutonium processor API was not found. Plutonium processor network compat disabled.");
                }
                return available;
            }

            public static MethodBase TargetMethod()
            {
                if (!IsPlutoniumProcAvailable()) return null;
                return findAssemblyPlutoniumProcMethod;
            }

            public static void Postfix(Pawn pawn, Thing proc, ref Thing __result)
            {
                if (!buildingPlutoniumProcType.IsInstanceOfType(proc))
                {
                    return;
                }

                FloatRange fuelLifeFilter = (FloatRange)plutoniumProcFuelLifeFilterProperty.GetValue(proc, null);

                float currentBestDistanceSquared = __result != null
                    ? NetworkItemSearchUtility.GetThingDistanceSquared(pawn, __result)
                    : float.MaxValue;

                Thing bestNetworkFuel = NetworkItemSearchUtility.FindClosestReachableThing(
                    pawn,
                    item => itemNuclearFuelType.IsInstanceOfType(item)
                        && (bool)nuclearFuelReprocessableProperty.GetValue(item, null)
                        && fuelLifeFilter.Includes((float)nuclearFuelFuelLevelField.GetValue(item))
                        && NetworkItemSearchUtility.PawnCanUseNetworkItemForHaul(pawn, item, forced: false),
                    out float bestDistanceSquared);

                if (bestNetworkFuel != null && (__result == null || bestDistanceSquared < currentBestDistanceSquared))
                {
                    __result = bestNetworkFuel;
                }
            }
        }
    }
}
