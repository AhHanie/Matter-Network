using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class XVstarCompat
    {
        private const string PackageId = "spacycuttingwings.madeinxvstar2";

        // ─── Null-safe reflection helpers ────────────────────────────────────
        private static FieldInfo F(Type t, string n) => t == null ? null : AccessTools.Field(t, n);
        private static MethodInfo M(Type t, string n, Type[] p = null) => t == null ? null : AccessTools.Method(t, n, p);
        private static PropertyInfo P(Type t, string n) => t == null ? null : AccessTools.Property(t, n);
        private static object E(FieldInfo f, string v) => f == null ? null : Enum.Parse(f.FieldType, v);

        // ─── Type handles ────────────────────────────────────────────────────
        private static readonly Type manufacturingCenterType = AccessTools.TypeByName("MadeInXVstar.Building_ManufacturingCenter");
        private static readonly Type agricultureSatelliteType = AccessTools.TypeByName("MadeInXVstar.Building_AgricultureSatellite");
        private static readonly Type miningHubType = AccessTools.TypeByName("MadeInXVstar.Building_MiningHub");
        private static readonly Type xvstarLogisticsType = AccessTools.TypeByName("MadeInXVstar.XVstar_Logistics");

        // ─── ManufacturingCenter reflection ─────────────────────────────────
        private static readonly FieldInfo mc_currentBillField = F(manufacturingCenterType, "currentBill");
        private static readonly FieldInfo mc_innerContainerField = F(manufacturingCenterType, "innerContainer");
        private static readonly MethodInfo mc_ingredientAllowsThing = M(manufacturingCenterType, "IngredientAllows", new[] { typeof(IngredientCount), typeof(Thing) });
        private static readonly MethodInfo mc_countAvailableForIngredient = M(manufacturingCenterType, "CountAvailableForIngredient", new[] { typeof(IngredientCount) });
        private static readonly MethodInfo mc_getNeededMaterialCount = M(manufacturingCenterType, "GetNeededMaterialCount", new[] { typeof(ThingDef) });
        private static readonly MethodInfo mc_findClosestAvailableMaterial = M(manufacturingCenterType, "FindClosestAvailableMaterial", new[] { typeof(bool) });
        private static readonly MethodInfo mc_receiveMaterial = M(manufacturingCenterType, "ReceiveMaterial", new[] { typeof(Thing) });
        private static readonly MethodInfo mc_tryStartNewBill = M(manufacturingCenterType, "TryStartNewBill");
        private static readonly MethodInfo mc_hasAllMaterials = M(manufacturingCenterType, "HasAllMaterials");

        // ─── AgricultureSatellite reflection ────────────────────────────────
        private static readonly FieldInfo as_stateField = F(agricultureSatelliteType, "state");
        private static readonly MethodInfo as_findItemToHaul = M(agricultureSatelliteType, "FindItemToHaul");
        private static readonly object as_idleState = E(as_stateField, "Idle");

        // ─── MiningHub reflection ─────────────────────────────────────────────
        private static readonly FieldInfo mh_stateField = F(miningHubType, "state");
        private static readonly FieldInfo mh_currentResourceDefField = F(miningHubType, "currentResourceDef");
        private static readonly FieldInfo mh_currentResourceCountField = F(miningHubType, "currentResourceCount");
        private static readonly FieldInfo mh_productsToHaulField = F(miningHubType, "productsToHaul");
        private static readonly FieldInfo mh_activeDroneField = F(miningHubType, "activeDrone");
        private static readonly MethodInfo mh_startNewProspecting = M(miningHubType, "StartNewProspecting");
        private static readonly object mh_miningState = E(mh_stateField, "Mining");

        // ─── XVstar_Logistics reflection ────────────────────────────────────
        private static readonly PropertyInfo xl_isTranscendentActive = P(xvstarLogisticsType, "IsTranscendentActive");

        // ─── Availability guards ─────────────────────────────────────────────
        private static bool IsActive() => ModsConfig.IsActive(PackageId);

        private static bool IsMCPullAvailable() =>
            IsActive() && mc_findClosestAvailableMaterial != null
            && mc_currentBillField != null && mc_ingredientAllowsThing != null
            && mc_countAvailableForIngredient != null;

        private static bool IsMCTranscendentPullAvailable() =>
            IsActive() && manufacturingCenterType != null
            && mc_findClosestAvailableMaterial != null && mc_currentBillField != null
            && mc_getNeededMaterialCount != null && mc_receiveMaterial != null
            && mc_hasAllMaterials != null;

        private static bool IsMCPushAvailable() =>
            IsActive() && manufacturingCenterType != null
            && mc_innerContainerField != null && mc_currentBillField != null
            && mc_tryStartNewBill != null;

        private static bool IsASAvailable() =>
            IsActive() && agricultureSatelliteType != null
            && as_stateField != null && as_findItemToHaul != null && as_idleState != null;

        private static bool IsMHAvailable() =>
            IsActive() && miningHubType != null
            && mh_stateField != null && mh_productsToHaulField != null
            && mh_startNewProspecting != null && mh_miningState != null;

        private static bool IsTranscendent()
        {
            try { return xl_isTranscendentActive != null && (bool)xl_isTranscendentActive.GetValue(null); }
            catch { return false; }
        }

        // ════════════════════════════════════════════════════════════════════
        // SHARED HELPERS
        // ════════════════════════════════════════════════════════════════════

        // Find the best network endpoint to push an item into.
        private static bool TryFindBestNetworkDestination(
            Building origin, Thing item, bool checkReachability, bool allowChutes,
            out DataNetwork bestNetwork, out IntVec3 endpointCell, out StoragePriority networkPriority)
        {
            bestNetwork = null;
            endpointCell = IntVec3.Invalid;
            networkPriority = StoragePriority.Unstored;
            float bestDist = float.MaxValue;
            string bestNetworkId = null;

            NetworksMapComponent mapComp = origin.Map?.GetComponent<NetworksMapComponent>();
            if (mapComp == null) return false;

            foreach (DataNetwork network in mapComp.Networks)
            {
                if (!network.IsOperational) continue;
                if (network.ControllerCanAcceptCount(item) <= 0) continue;

                StoragePriority pri = network.StorageSettings.Priority;

                foreach (NetworkBuildingNetworkInterface iface in network.NetworkInterfaces)
                {
                    if (iface == null || iface.Destroyed) continue;
                    if (checkReachability && !origin.Map.reachability.CanReach(
                        origin.Position, iface.InteractionCell, PathEndMode.OnCell,
                        TraverseParms.For(TraverseMode.PassDoors, Danger.None)))
                        continue;
                    EvalEndpoint(origin, network, iface.Position, pri,
                        ref bestNetwork, ref endpointCell, ref networkPriority, ref bestDist, ref bestNetworkId);
                }

                if (!allowChutes) continue;
                foreach (NetworkBuildingNetworkChute chute in network.NetworkChutes)
                {
                    if (chute == null || chute.Destroyed) continue;
                    if (checkReachability && !origin.Map.reachability.CanReach(
                        origin.Position, chute.Position, PathEndMode.OnCell,
                        TraverseParms.For(TraverseMode.PassDoors, Danger.None)))
                        continue;
                    EvalEndpoint(origin, network, chute.Position, pri,
                        ref bestNetwork, ref endpointCell, ref networkPriority, ref bestDist, ref bestNetworkId);
                }
            }

            return bestNetwork != null;
        }

        private static void EvalEndpoint(
            Building origin, DataNetwork network, IntVec3 cell, StoragePriority priority,
            ref DataNetwork bestNetwork, ref IntVec3 bestCell, ref StoragePriority bestPri,
            ref float bestDist, ref string bestId)
        {
            float dist = (cell - origin.Position).LengthHorizontalSquared;
            bool better;
            if (priority > bestPri) better = true;
            else if (priority < bestPri) better = false;
            else if (dist < bestDist) better = true;
            else if (dist > bestDist) better = false;
            else better = string.Compare(network.NetworkId, bestId, StringComparison.Ordinal) < 0;
            if (!better) return;
            bestNetwork = network;
            bestCell = cell;
            bestPri = priority;
            bestDist = dist;
            bestId = network.NetworkId;
        }

        // True if the network destination is at least as good as the best physical stockpile.
        private static bool NetworkBeatsPhysical(
            Building origin, Thing item, bool checkReachability,
            IntVec3 networkEndpoint, StoragePriority networkPriority)
        {
            StoragePriority physPri = BestPhysicalPriority(origin, item, checkReachability);
            if (networkPriority > physPri) return true;
            if (networkPriority < physPri) return false;
            float netDist = (networkEndpoint - origin.Position).LengthHorizontalSquared;
            float physDist = BestPhysicalDist(origin, item, physPri, checkReachability);
            return netDist <= physDist;
        }

        private static StoragePriority BestPhysicalPriority(Building origin, Thing item, bool checkReachability)
        {
            StoragePriority best = StoragePriority.Unstored;
            foreach (SlotGroup sg in origin.Map.haulDestinationManager.AllGroups)
            {
                if (sg.Settings.Priority <= best) continue;
                if (!sg.Settings.AllowedToAccept(item)) continue;
                foreach (IntVec3 cell in sg.CellsList)
                {
                    if (!cell.IsValidStorageFor(origin.Map, item)) continue;
                    if (checkReachability && !origin.Map.reachability.CanReach(
                        origin.Position, cell, PathEndMode.OnCell,
                        TraverseParms.For(TraverseMode.PassDoors, Danger.None)))
                        continue;
                    best = sg.Settings.Priority;
                    break;
                }
            }
            return best;
        }

        private static float BestPhysicalDist(Building origin, Thing item, StoragePriority atPriority, bool checkReachability)
        {
            float best = float.MaxValue;
            foreach (SlotGroup sg in origin.Map.haulDestinationManager.AllGroups)
            {
                if (sg.Settings.Priority != atPriority) continue;
                if (!sg.Settings.AllowedToAccept(item)) continue;
                foreach (IntVec3 cell in sg.CellsList)
                {
                    if (!cell.IsValidStorageFor(origin.Map, item)) continue;
                    if (checkReachability && !origin.Map.reachability.CanReach(
                        origin.Position, cell, PathEndMode.OnCell,
                        TraverseParms.For(TraverseMode.PassDoors, Danger.None)))
                        continue;
                    float d = (cell - origin.Position).LengthHorizontalSquared;
                    if (d < best) best = d;
                }
            }
            return best;
        }

        // Move up to as many units of source as the network accepts.
        // Returns true if at least some units were moved.
        private static bool TryMoveThingToNetwork(Thing source, DataNetwork network, IntVec3 fallback, Map map, out int moved)
        {
            moved = 0;
            if (source == null || source.Destroyed || source.stackCount <= 0) return false;
            if (!network.IsOperational) return false;
            ControllerItemOwner container = network.ActiveController?.innerContainer;
            if (container == null) return false;

            int accepted = Math.Min(source.stackCount, network.ControllerCanAcceptCount(source));
            if (accepted <= 0) return false;

            Thing toMove;
            if (accepted < source.stackCount)
            {
                toMove = source.SplitOff(accepted);
            }
            else
            {
                toMove = source;
                if (source.Spawned)
                    source.DeSpawn();
                else if (source.holdingOwner != null)
                    source.holdingOwner.Remove(source);
            }

            toMove.SetForbidden(false, false);
            if (!container.TryAdd(toMove, canMergeWithExistingStacks: true))
            {
                if (!toMove.Spawned && !toMove.Destroyed)
                    GenPlace.TryPlaceThing(toMove, fallback, map, ThingPlaceMode.Near);
                return false;
            }

            moved = accepted;
            network.MarkBytesDirty();
            return true;
        }

        // Extract count units from a network-held item and place them near dropCell.
        private static bool TryExtractNetworkThing(Thing source, int count, IntVec3 dropCell, Map map, out Thing extracted)
        {
            extracted = null;
            NetworksMapComponent mapComp = map?.GetComponent<NetworksMapComponent>();
            if (mapComp == null || !mapComp.TryGetItemNetwork(source, out DataNetwork network)) return false;
            if (!network.CanExtractItems) return false;
            ControllerItemOwner container = network.ActiveController?.innerContainer;
            if (container == null) return false;

            count = Math.Min(count, source.stackCount);
            if (count <= 0) return false;

            Thing toPlace;
            if (count >= source.stackCount)
            {
                container.Remove(source);
                toPlace = source;
            }
            else
            {
                toPlace = source.SplitOff(count);
            }

            network.MarkBytesDirty();
            GenPlace.TryPlaceThing(toPlace, dropCell, map, ThingPlaceMode.Near);
            extracted = toPlace;
            return true;
        }

        private static void CleanList(List<Thing> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Thing t = list[i];
                if (t == null || t.Destroyed || t.stackCount <= 0) list.RemoveAt(i);
            }
        }

        // After all mining products are hauled, resume mining or start new prospecting.
        private static void ResetMiningAfterHaul(object instance)
        {
            ThingDef resDef = (ThingDef)mh_currentResourceDefField.GetValue(instance);
            int resCount = (int)mh_currentResourceCountField.GetValue(instance);
            if (resDef != null && resCount > 0)
                mh_stateField.SetValue(instance, mh_miningState);
            else
                mh_startNewProspecting.Invoke(instance, null);
        }

        // ════════════════════════════════════════════════════════════════════
        // PATCH 1 — Manufacturing Pull
        // Postfix on FindClosestAvailableMaterial to include network items.
        // The existing Toils_Goto.GotoThing patch redirects the drone to the
        // nearest network interface when the job's TargetA is network-held.
        // ════════════════════════════════════════════════════════════════════
        [HarmonyPatch]
        public static class Patch_ManufacturingPull
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsMCPullAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(manufacturingCenterType, "FindClosestAvailableMaterial", new[] { typeof(bool) });

            public static void Postfix(object __instance, bool checkReachability, ref Thing __result)
            {
                Bill currentBill = (Bill)mc_currentBillField.GetValue(__instance);
                if (currentBill?.recipe?.ingredients == null) return;

                Building mc = __instance as Building;
                Map map = mc?.Map;
                if (map == null) return;

                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();

                float bestDist = __result != null && __result.Spawned
                    ? (__result.Position - mc.Position).LengthHorizontalSquared
                    : float.MaxValue;

                foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
                {
                    float ifaceDist = float.MaxValue;
                    foreach (NetworkBuildingNetworkInterface iface in network.NetworkInterfaces)
                    {
                        if (iface == null || iface.Destroyed) continue;
                        if (checkReachability && !map.reachability.CanReach(
                            mc.Position, iface.InteractionCell, PathEndMode.OnCell,
                            TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly)))
                            continue;
                        float d = (iface.Position - mc.Position).LengthHorizontalSquared;
                        if (d < ifaceDist) ifaceDist = d;
                    }
                    if (ifaceDist == float.MaxValue) continue;

                    foreach (Thing item in network.StoredItems)
                    {
                        if (item == null || item.Destroyed) continue;
                        if (item.IsForbidden(Faction.OfPlayer)) continue;

                        foreach (IngredientCount ing in currentBill.recipe.ingredients)
                        {
                            float available = (float)mc_countAvailableForIngredient.Invoke(__instance, new object[] { ing });
                            if (available >= ing.GetBaseCount()) continue;
                            bool allowed = (bool)mc_ingredientAllowsThing.Invoke(__instance, new object[] { ing, item });
                            if (!allowed) continue;

                            if (ifaceDist < bestDist)
                            {
                                bestDist = ifaceDist;
                                __result = item;
                            }
                            break;
                        }
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PATCH 2 — Manufacturing Transcendent Pull
        // Prefix on TeleportMaterialsToCenter — reimplemented to handle items
        // that are network-held (which the original would break trying to DeSpawn).
        // Always intercepts when active because PATCH 1 may return network items
        // to TeleportMaterialsToCenter's internal FindClosestAvailableMaterial call.
        // ════════════════════════════════════════════════════════════════════
        [HarmonyPatch]
        public static class Patch_ManufacturingTranscendentPull
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsMCTranscendentPullAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(manufacturingCenterType, "TeleportMaterialsToCenter");

            public static bool Prefix(object __instance, ref bool __result)
            {
                Building mc = __instance as Building;
                Map map = mc?.Map;
                if (map == null) return true;

                // If no extraction-enabled networks exist, PATCH 1 cannot return network items,
                // so the original TeleportMaterialsToCenter is safe to run unchanged.
                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                bool anyNetwork = false;
                foreach (DataNetwork n in mapComp.Networks)
                {
                    if (n.CanExtractItems) { anyNetwork = true; break; }
                }
                if (!anyNetwork) return true;

                // Full reimplementation that handles both network-held and spawned materials.
                int safety = 0;
                while (!(bool)mc_hasAllMaterials.Invoke(__instance, null) && safety < 50)
                {
                    Thing material = (Thing)mc_findClosestAvailableMaterial.Invoke(__instance, new object[] { false });
                    if (material == null) break;

                    int neededCount = (int)mc_getNeededMaterialCount.Invoke(__instance, new object[] { material.def });
                    if (neededCount <= 0) break;

                    int takeCount = Math.Min(material.stackCount, neededCount);

                    if (mapComp.TryGetItemNetwork(material, out DataNetwork _))
                    {
                        // Network-held: extract and place near the center, then call ReceiveMaterial.
                        if (!TryExtractNetworkThing(material, takeCount, mc.Position, map, out Thing extracted))
                            break;
                        mc_receiveMaterial.Invoke(__instance, new object[] { extracted });
                        if (extracted.Spawned)
                            FleckMaker.ThrowMicroSparks(extracted.DrawPos, map);
                    }
                    else
                    {
                        // Spawned: same behavior as original TeleportMaterialsToCenter.
                        Thing toTeleport = material;
                        if (material.stackCount > takeCount)
                            toTeleport = material.SplitOff(takeCount);
                        if (toTeleport.Spawned) toTeleport.DeSpawn();
                        GenPlace.TryPlaceThing(toTeleport, mc.Position, map, ThingPlaceMode.Near);
                        mc_receiveMaterial.Invoke(__instance, new object[] { toTeleport });
                        FleckMaker.ThrowMicroSparks(toTeleport.DrawPos, map);
                    }

                    safety++;
                }

                __result = (bool)mc_hasAllMaterials.Invoke(__instance, null);
                return false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PATCH 3 — Manufacturing Push (Normal Drone)
        // Prefix on SpawnDroneForProduct — move products into network before
        // XVstar spawns a drone. If all products are moved, skip the drone.
        // ════════════════════════════════════════════════════════════════════
        [HarmonyPatch]
        public static class Patch_ManufacturingPushDrone
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsMCPushAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(manufacturingCenterType, "SpawnDroneForProduct");

            public static bool Prefix(object __instance)
            {
                List<Thing> inner = (List<Thing>)mc_innerContainerField.GetValue(__instance);
                CleanList(inner);
                if (inner.Count == 0) return true;

                Building mc = __instance as Building;
                bool anyMoved = PushListToNetwork(mc, inner, checkReachability: true);
                if (!anyMoved) return true;

                CleanList(inner);
                if (inner.Count > 0) return true; // remainder handled by XVstar drone

                // All products moved to network — advance the bill.
                mc_currentBillField.SetValue(__instance, null);
                mc_tryStartNewBill.Invoke(__instance, null);
                return false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PATCH 4 — Manufacturing Push (Transcendent)
        // Prefix on TeleportProductsToStorage (MC) — move products into network
        // before XVstar teleports them to physical stockpiles.
        // ════════════════════════════════════════════════════════════════════
        [HarmonyPatch]
        public static class Patch_ManufacturingPushTeleport
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsMCPushAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(manufacturingCenterType, "TeleportProductsToStorage");

            public static bool Prefix(object __instance, ref bool __result)
            {
                List<Thing> inner = (List<Thing>)mc_innerContainerField.GetValue(__instance);
                CleanList(inner);
                if (inner.Count == 0) return true;

                Building mc = __instance as Building;
                PushListToNetwork(mc, inner, checkReachability: false);
                CleanList(inner);

                if (inner.Count == 0)
                {
                    // All products moved — TickState will call TryStartNewBill when it sees __result=true.
                    __result = true;
                    return false;
                }
                return true; // remaining products deferred to original physical-storage path
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PATCH 5 — Agriculture Push
        // Prefix on TryStartHaulItems — directly move the best haul candidate
        // into the network when the network wins over physical stockpiles.
        // ════════════════════════════════════════════════════════════════════
        [HarmonyPatch]
        public static class Patch_AgriculturePush
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsASAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(agricultureSatelliteType, "TryStartHaulItems");

            public static bool Prefix(object __instance)
            {
                Building origin = __instance as Building;
                Map map = origin?.Map;
                if (map == null) return true;

                Thing item = (Thing)as_findItemToHaul.Invoke(__instance, null);
                if (item == null) return true;

                const bool checkReach = true;
                if (!TryFindBestNetworkDestination(origin, item, checkReach, allowChutes: true,
                    out DataNetwork network, out IntVec3 endpoint, out StoragePriority netPri))
                    return true;
                if (!NetworkBeatsPhysical(origin, item, checkReach, endpoint, netPri))
                    return true;
                if (!TryMoveThingToNetwork(item, network, origin.Position, map, out _))
                    return true;

                as_stateField.SetValue(__instance, as_idleState);
                return false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PATCH 6 — Mining Push (Normal Drone)
        // Prefix on SpawnDroneForHauling — move products into network first.
        // For transcendent mode, if products remain after our move, the original
        // calls TeleportProductsToStorage which PATCH 7 then intercepts.
        // ════════════════════════════════════════════════════════════════════
        [HarmonyPatch]
        public static class Patch_MiningPushDrone
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsMHAvailable() && mh_activeDroneField != null;

            public static MethodBase TargetMethod() =>
                AccessTools.Method(miningHubType, "SpawnDroneForHauling");

            public static bool Prefix(object __instance)
            {
                // SpawnDroneForHauling exits early when a drone is alive — mirror that.
                Pawn drone = (Pawn)mh_activeDroneField.GetValue(__instance);
                if (drone != null && !drone.Destroyed) return true;

                List<Thing> products = (List<Thing>)mh_productsToHaulField.GetValue(__instance);
                CleanList(products);
                if (products.Count == 0) return true;

                Building mh = __instance as Building;
                bool transcendent = IsTranscendent();
                bool anyMoved = PushListToNetwork(mh, products, checkReachability: !transcendent);
                if (!anyMoved) return true;

                CleanList(products);
                if (products.Count > 0) return true; // remainder handled by XVstar

                ResetMiningAfterHaul(__instance);
                return false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PATCH 7 — Mining Push (Transcendent)
        // Prefix on TeleportProductsToStorage (MH) — move products into network
        // before XVstar teleports them to physical stockpiles.
        // ════════════════════════════════════════════════════════════════════
        [HarmonyPatch]
        public static class Patch_MiningPushTeleport
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsMHAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(miningHubType, "TeleportProductsToStorage");

            public static bool Prefix(object __instance)
            {
                List<Thing> products = (List<Thing>)mh_productsToHaulField.GetValue(__instance);
                CleanList(products);
                if (products.Count == 0) return true;

                Building mh = __instance as Building;
                PushListToNetwork(mh, products, checkReachability: false);
                CleanList(products);

                if (products.Count == 0)
                {
                    ResetMiningAfterHaul(__instance);
                    return false;
                }
                return true; // remaining products deferred to original physical-storage path
            }
        }

        // ─── Shared push helper used by push patches ─────────────────────────
        // Iterates the list backwards, moves items to network when network wins,
        // removes fully-moved items from the list. Returns true if any item was moved.
        private static bool PushListToNetwork(Building origin, List<Thing> list, bool checkReachability)
        {
            bool anyMoved = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Thing product = list[i];
                if (product == null || product.Destroyed) { list.RemoveAt(i); continue; }

                if (!TryFindBestNetworkDestination(origin, product, checkReachability, allowChutes: true,
                    out DataNetwork network, out IntVec3 endpoint, out StoragePriority netPri))
                    continue;
                if (!NetworkBeatsPhysical(origin, product, checkReachability, endpoint, netPri))
                    continue;
                if (!TryMoveThingToNetwork(product, network, origin.Position, origin.Map, out _))
                    continue;

                // Remove from list if the item is no longer spawned (full-stack move).
                // Partial moves leave the item spawned with reduced count — keep it.
                if (!product.Spawned || product.Destroyed || product.stackCount <= 0)
                    list.RemoveAt(i);
                anyMoved = true;
            }
            return anyMoved;
        }
    }
}
