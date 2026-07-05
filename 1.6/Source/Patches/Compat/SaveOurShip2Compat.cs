using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace SK_Matter_Network.Patches
{
    // Save Our Ship 2 moves ship contents with its own map-transfer pipeline
    // (ShipInteriorMod2.MoveShip) instead of vanilla gravship's: it never calls
    // Thing.PreSwapMap(), and it despawns buildings with the default DestroyMode.Vanish rather
    // than DestroyMode.WillReplace. Without this compat, Matter Network never learns the full set
    // of network buildings moving together, misclassifies the move as partial, removes buildings
    // from their DataNetwork mid-move, and disk drives/chutes run their removal side effects
    // (archiving/dropping stored items) as if the buildings were being destroyed.
    //
    // A prefix on MoveShip scans the moving ship's area for NetworkBuilding instances and
    // registers them as an external transfer via NetworkBuildingTransferTracker (the same
    // registration set NetworkBuilding.PreSwapMap uses for vanilla gravship). A finalizer then
    // refreshes the affected DataNetworks - or rolls back to the source map if MoveShip left
    // buildings there - and always clears the registration, even if MoveShip threw.
    //
    // SOS2 has no compile-time reference here; every SOS2 member is resolved via AccessTools and
    // the patch simply doesn't activate if anything expected can't be found.
    public static class SaveOurShip2Compat
    {
        private const string PackageId = "kentington.saveourship2";
        private const string SourceModLabel = "Save Our Ship 2";

        private static readonly Type shipInteriorMod2Type =
            AccessTools.TypeByName("SaveOurShip2.ShipInteriorMod2");
        private static readonly Type shipMapCompType =
            AccessTools.TypeByName("SaveOurShip2.ShipMapComp");
        private static readonly Type spaceShipCacheType =
            AccessTools.TypeByName("SaveOurShip2.SpaceShipCache");

        private static readonly MethodInfo moveShipMethod = shipInteriorMod2Type == null ? null :
            AccessTools.Method(shipInteriorMod2Type, "MoveShip", new[]
            {
                typeof(Building), typeof(Map), typeof(IntVec3), typeof(Faction), typeof(byte), typeof(bool), typeof(bool)
            });
        private static readonly MethodInfo shipIndexOnVecMethod = shipMapCompType == null ? null :
            AccessTools.Method(shipMapCompType, "ShipIndexOnVec", new[] { typeof(IntVec3) });
        private static readonly PropertyInfo shipsOnMapProperty = shipMapCompType == null ? null :
            AccessTools.Property(shipMapCompType, "ShipsOnMap");
        private static readonly FieldInfo areaField = spaceShipCacheType == null ? null :
            AccessTools.Field(spaceShipCacheType, "Area");

        private static bool _warnedMissingApi;

        private static bool IsAvailable()
        {
            return shipInteriorMod2Type != null
                && shipMapCompType != null
                && spaceShipCacheType != null
                && moveShipMethod != null
                && shipIndexOnVecMethod != null
                && shipsOnMapProperty != null
                && areaField != null;
        }

        private static bool Prepare()
        {
            bool active = ModsConfig.IsActive(PackageId);
            bool available = active && IsAvailable();
            if (active && !available && !_warnedMissingApi)
            {
                _warnedMissingApi = true;
                Logger.Warning("[SOS2 Compat] Save Our Ship 2 is loaded but expected ShipInteriorMod2.MoveShip API was not found. SOS2 network-transfer compat disabled.");
            }
            return available;
        }

        [HarmonyPatch]
        public static class Patch_MoveShip
        {
            [HarmonyPrepare]
            public static bool Prepare() => SaveOurShip2Compat.Prepare();

            public static MethodBase TargetMethod() => moveShipMethod;

            // Runs before SOS2 touches anything: captures every NetworkBuilding currently standing
            // in the moving ship's area, before SOS2 sets beingTransportedOnGravship or despawns it.
            public static void Prefix(Building core, Map targetMap, out NetworkBuildingTransferTracker.ExternalTransferContext __state)
            {
                __state = null;

                Map sourceMap = core?.Map;
                if (sourceMap == null) return;

                MapComponent shipMapComp = sourceMap.GetComponent(shipMapCompType);
                if (shipMapComp == null) return;

                int shipIndex = (int)shipIndexOnVecMethod.Invoke(shipMapComp, new object[] { core.Position });
                if (shipIndex < 0) return;

                if (!(shipsOnMapProperty.GetValue(shipMapComp, null) is IDictionary shipsOnMap)) return;
                if (!shipsOnMap.Contains(shipIndex)) return;

                object shipCache = shipsOnMap[shipIndex];
                if (shipCache == null) return;

                if (!(areaField.GetValue(shipCache) is IEnumerable areaCells)) return;

                List<NetworkBuilding> buildings = new List<NetworkBuilding>();
                foreach (object cellObj in areaCells)
                {
                    IntVec3 cell = (IntVec3)cellObj;
                    foreach (Thing t in cell.GetThingList(sourceMap))
                    {
                        if (t is NetworkBuilding nb)
                            buildings.Add(nb);
                    }
                }

                if (buildings.Count == 0) return;

                __state = NetworkBuildingTransferTracker.BeginExternalTransfer(buildings, sourceMap, targetMap, SourceModLabel);
            }

            // MoveShip has multiple early-exit branches and is not exception-safe internally, so a
            // finalizer (not just a postfix) is required to guarantee the transfer registration is
            // always cleared, even if MoveShip throws partway through despawning buildings.
            public static Exception Finalizer(NetworkBuildingTransferTracker.ExternalTransferContext __state, Exception __exception)
            {
                if (__state != null)
                {
                    NetworkBuildingTransferTracker.CompleteExternalTransfer(__state, success: __exception == null);
                }

                return __exception;
            }
        }
    }
}
