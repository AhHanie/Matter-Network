using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_StoreUtility
    {
        [HarmonyPatch(typeof(StoreUtility), "IsInValidBestStorage")]
        public static class IsInValidBestStorage
        {
            public static bool Prefix(Thing t, ref bool __result)
            {
                NetworksMapComponent mapComp = t.MapHeld.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(t, out DataNetwork network))
                {
                    __result = true;
                    if (!network.AcceptsItem(t) || network.Faction != Faction.OfPlayer)
                    {
                        __result = false;
                    }
                    if (StoreUtility.TryFindBestBetterStorageFor(t, null, t.MapHeld, network.StorageSettings.Priority, Faction.OfPlayer, out var _, out var _, needAccurateResult: false))
                    {
                        __result = false;
                    }
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(StoreUtility), "TryFindBestBetterStoreCellForWorker")]
        public static class TryFindBestBetterStoreCellForWorker
        {
            public static bool Prefix(Thing t, Pawn carrier, Map map, Faction faction, ISlotGroup slotGroup, bool needAccurateResult, ref IntVec3 closestSlot, ref float closestDistSquared, ref StoragePriority foundPriority)
            {
                NetworksMapComponent mapComp = t.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(t, out DataNetwork network))
                {
                    return true;
                }
                if (slotGroup == null || !slotGroup.Settings.AllowedToAccept(t))
                {
                    return false;
                }
                List<NetworkBuildingNetworkInterface> interfaces = network.NetworkInterfaces;
                List<IntVec3> cellsList = slotGroup.CellsList;
                int count = cellsList.Count;
                int num = (needAccurateResult ? Mathf.FloorToInt((float)count * Rand.Range(0.005f, 0.018f)) : 0);
                bool found = false;
                for (int i = 0; i < count; i++)
                {
                    foreach (NetworkBuildingNetworkInterface interf in interfaces)
                    {
                        IntVec3 intVec2 = cellsList[i];
                        float num2 = (interf.InteractionCell - intVec2).LengthHorizontalSquared;
                        if (!(num2 > closestDistSquared) && StoreUtility.IsGoodStoreCell(intVec2, map, t, carrier, faction))
                        {
                            closestSlot = intVec2;
                            closestDistSquared = num2;
                            foundPriority = slotGroup.Settings.Priority;
                            if (i >= num)
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    if (found)
                    {
                        break;
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(StoreUtility), "TryFindBestBetterNonSlotGroupStorageFor")]
        public static class TryFindBestBetterNonSlotGroupStorageFor
        {
            public static bool Prefix(Thing t, Pawn carrier, Map map, StoragePriority currentPriority, Faction faction, out IHaulDestination haulDestination, bool acceptSamePriority, bool requiresDestReservation, ref bool __result)
            {
                NetworksMapComponent mapComp = t.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(t, out DataNetwork network))
                {
                    haulDestination = null;
                    return true;
                }
                List<NetworkBuildingNetworkInterface> interfaces = network.NetworkInterfaces;
                List<IHaulDestination> allHaulDestinationsListInPriorityOrder = map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder;
                float num = float.MaxValue;
                StoragePriority storagePriority = StoragePriority.Unstored;
                haulDestination = null;
                for (int i = 0; i < allHaulDestinationsListInPriorityOrder.Count; i++)
                {
                    foreach (NetworkBuildingNetworkInterface interf in interfaces)
                    {
                        IHaulDestination haulDestination2 = allHaulDestinationsListInPriorityOrder[i];
                        if (haulDestination2 is ISlotGroupParent || (haulDestination2 is Building_Grave && !t.CanBeBuried()) || !haulDestination2.HaulDestinationEnabled)
                        {
                            continue;
                        }
                        StoragePriority priority = haulDestination2.GetStoreSettings().Priority;
                        if ((int)priority < (int)storagePriority || (acceptSamePriority && (int)priority < (int)currentPriority) || (!acceptSamePriority && (int)priority <= (int)currentPriority))
                        {
                            break;
                        }
                        float num2 = interf.InteractionCell.DistanceToSquared(haulDestination2.Position);
                        if (num2 > num || !haulDestination2.Accepts(t))
                        {
                            continue;
                        }
                        Thing thing = haulDestination2 as Thing;
                        if (thing != null && thing.Faction != faction)
                        {
                            continue;
                        }
                        if (thing != null && faction != null && thing.IsForbidden(faction))
                        {
                            continue;
                        }
                        if (thing != null && requiresDestReservation)
                        {
                            if (thing is IHaulEnroute enroute)
                            {
                                if (!map.reservationManager.OnlyReservationsForJobDef(thing, JobDefOf.HaulToContainer) || enroute.GetSpaceRemainingWithEnroute(t.def) <= 0)
                                {
                                    continue;
                                }
                            }
                            else if (faction != null && map.reservationManager.IsReservedByAnyoneOf(thing, faction))
                            {
                                continue;
                            }
                        }
                        num = num2;
                        storagePriority = priority;
                        haulDestination = haulDestination2;
                    }
                }
                __result = haulDestination != null;
                return false;
            }
        }

        [HarmonyPatch(typeof(StoreUtility), "CurrentStoragePriorityOf")]
        public static class CurrentStoragePriorityOf
        {
            public static bool Prefix(Thing t, bool forced, ref StoragePriority __result)
            {
                NetworksMapComponent mapComp = t.MapHeld.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(t, out DataNetwork network))
                {
                    if (!network.AcceptsItem(t) || (forced && network.Faction != Faction.OfPlayer)) {
                        __result = StoragePriority.Unstored;
                        return false;
                    }
                    __result = network.StorageSettings.Priority;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(StoreUtility), "IsInAnyStorage")]
        public static class IsInAnyStorage
        {
            public static bool Prefix(Thing t, ref bool __result)
            {
                NetworksMapComponent mapComp = t.MapHeld.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(t, out DataNetwork network))
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(StoreUtility), "IsInValidStorage")]
        public static class IsInValidStorage
        {
            public static bool Prefix(Thing t, ref bool __result)
            {
                NetworksMapComponent mapComp = t.MapHeld.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(t, out DataNetwork network))
                {
                    __result = network.AcceptsItem(t);
                    return false;
                }
                return true;
            }
        }
    }
}
