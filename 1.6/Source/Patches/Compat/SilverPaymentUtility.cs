using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    // Shared beacon+network transactional silver payment. Snapshots powered orbital trade beacon
    // cells (vanilla TradeUtility.LaunchSilver's own search space) and eligible Matter Network
    // storage, detaches beacon silver first then network silver, and rolls back cleanly if the full
    // price can't be reached. Extracted from GravshipBlueprintsCompat so other soft-compat patches
    // that need the same "spend beacon silver first, then network silver, atomically" behavior (e.g.
    // UniversalTradeHubCompat) don't duplicate its detach/rollback logic.
    internal static class SilverPaymentUtility
    {
        public struct DetachedBeaconPiece
        {
            public Thing Piece;
            public IntVec3 Cell;
            public Map Map;
        }

        public struct NetworkSilverStack
        {
            public Thing Thing;
            public DataNetwork Network;
        }

        public struct DetachedNetworkPiece
        {
            public Thing Piece;
            public DataNetwork Network;
        }

        // Mirrors TradeUtility.AllLaunchableThingsForTrade / LaunchThingsOfType: powered orbital
        // trade beacons, deduplicated by Thing reference because their tradeable cells can overlap.
        public static List<Thing> SnapshotBeaconSilverStacks(Map map)
        {
            List<Thing> stacks = new List<Thing>();
            HashSet<Thing> seen = new HashSet<Thing>();

            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(map))
            {
                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    List<Thing> thingsAtCell = cell.GetThingList(map);
                    for (int i = 0; i < thingsAtCell.Count; i++)
                    {
                        Thing t = thingsAtCell[i];
                        if (t == null || t.Destroyed || t.stackCount <= 0 || t.def != ThingDefOf.Silver)
                        {
                            continue;
                        }

                        if (seen.Add(t))
                        {
                            stacks.Add(t);
                        }
                    }
                }
            }

            return stacks;
        }

        // Only operational networks with an extraction interface (NetworkItemSearchUtility.Networks
        // already applies ExtractionEnabledNetworks), items currently resident in the network's
        // active controller (DataNetwork.StoredItems), deduplicated by Thing reference.
        public static List<NetworkSilverStack> SnapshotNetworkSilverStacks(Map map)
        {
            List<NetworkSilverStack> stacks = new List<NetworkSilverStack>();
            HashSet<Thing> seen = new HashSet<Thing>();

            foreach (DataNetwork network in NetworkItemSearchUtility.Networks(map))
            {
                foreach (Thing t in network.StoredItems)
                {
                    if (t == null || t.Destroyed || t.stackCount <= 0 || t.def != ThingDefOf.Silver)
                    {
                        continue;
                    }

                    if (seen.Add(t))
                    {
                        stacks.Add(new NetworkSilverStack { Thing = t, Network = network });
                    }
                }
            }

            return stacks;
        }

        public static int SumBeaconStacks(List<Thing> stacks)
        {
            int total = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                total += stacks[i].stackCount;
            }
            return total;
        }

        public static int SumNetworkStacks(List<NetworkSilverStack> stacks)
        {
            int total = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                total += stacks[i].Thing.stackCount;
            }
            return total;
        }

        // Detaches up to `amount` from the snapshotted beacon stacks, in vanilla's own search order.
        // Revalidates each stack immediately before splitting it, since a snapshot can only be trusted
        // up to the moment something else in this same synchronous call touches it.
        public static int DetachBeaconSilver(List<Thing> stacks, int amount, List<DetachedBeaconPiece> detached)
        {
            int remaining = amount;
            for (int i = 0; i < stacks.Count && remaining > 0; i++)
            {
                Thing stack = stacks[i];
                if (stack == null || stack.Destroyed || stack.stackCount <= 0 || stack.def != ThingDefOf.Silver)
                {
                    continue;
                }

                IntVec3 cell = stack.PositionHeld;
                Map stackMap = stack.MapHeld;
                int take = Math.Min(remaining, stack.stackCount);

                Thing piece = stack.SplitOff(take);
                if (piece == null || piece.stackCount <= 0)
                {
                    continue;
                }

                detached.Add(new DetachedBeaconPiece { Piece = piece, Cell = cell, Map = stackMap });
                remaining -= piece.stackCount;
            }

            return amount - remaining;
        }

        public static int DetachNetworkSilver(List<NetworkSilverStack> stacks, int amount, List<DetachedNetworkPiece> detached, HashSet<DataNetwork> touchedNetworks)
        {
            int remaining = amount;
            for (int i = 0; i < stacks.Count && remaining > 0; i++)
            {
                Thing stack = stacks[i].Thing;
                DataNetwork network = stacks[i].Network;
                if (stack == null || stack.Destroyed || stack.stackCount <= 0 || stack.def != ThingDefOf.Silver
                    || !network.ItemInNetwork(stack))
                {
                    continue;
                }

                int take = Math.Min(remaining, stack.stackCount);
                Thing piece = stack.SplitOff(take);
                if (piece == null || piece.stackCount <= 0)
                {
                    continue;
                }

                detached.Add(new DetachedNetworkPiece { Piece = piece, Network = network });
                touchedNetworks.Add(network);
                remaining -= piece.stackCount;
            }

            return amount - remaining;
        }

        public static void RestoreBeaconPieces(List<DetachedBeaconPiece> pieces)
        {
            foreach (DetachedBeaconPiece piece in pieces)
            {
                if (piece.Piece == null || piece.Piece.Destroyed)
                {
                    continue;
                }

                if (!GenPlace.TryPlaceThing(piece.Piece, piece.Cell, piece.Map, ThingPlaceMode.Near))
                {
                    Logger.Error($"Failed to restore {piece.Piece.stackCount} silver to beacon cell {piece.Cell} " +
                        "after an aborted purchase; that silver may be lost.");
                }
            }
        }

        public static void RestoreNetworkPieces(List<DetachedNetworkPiece> pieces)
        {
            HashSet<DataNetwork> touched = new HashSet<DataNetwork>();

            foreach (DetachedNetworkPiece piece in pieces)
            {
                if (piece.Piece == null || piece.Piece.Destroyed)
                {
                    continue;
                }

                NetworkBuildingController controller = piece.Network?.ActiveController;
                if (controller == null || !controller.innerContainer.TryAddExistingNetworkItem(piece.Piece))
                {
                    Logger.Error($"Failed to restore {piece.Piece.stackCount} silver to network " +
                        $"{piece.Network?.NetworkId} after an aborted purchase; that silver may be lost.");
                    continue;
                }

                touched.Add(piece.Network);
            }

            foreach (DataNetwork network in touched)
            {
                network.MarkBytesDirty();
            }
        }

        // Preflight and spending happen synchronously within one call - the same confirmation
        // callback that computed `price` - so the snapshots taken above cannot go stale between the
        // affordability check and the detach loops below; no ticks pass in between.
        public static bool TryPayCombinedSilver(Map map, int price, out int beaconSpent, out int networkSpent)
        {
            List<Thing> beaconStacks = SnapshotBeaconSilverStacks(map);
            int beaconSilver = SumBeaconStacks(beaconStacks);
            int beaconShare = Math.Min(price, beaconSilver);

            List<DetachedBeaconPiece> beaconPieces = new List<DetachedBeaconPiece>();
            beaconSpent = beaconShare > 0 ? DetachBeaconSilver(beaconStacks, beaconShare, beaconPieces) : 0;

            int networkShare = price - beaconSpent;
            List<NetworkSilverStack> networkStacks = SnapshotNetworkSilverStacks(map);
            List<DetachedNetworkPiece> networkPieces = new List<DetachedNetworkPiece>();
            HashSet<DataNetwork> touchedNetworks = new HashSet<DataNetwork>();
            networkSpent = networkShare > 0 ? DetachNetworkSilver(networkStacks, networkShare, networkPieces, touchedNetworks) : 0;

            if (beaconSpent + networkSpent != price)
            {
                // beaconSpent/networkSpent are left as the amounts actually detached (and now
                // restored) so the caller can log what was attempted, even though the net effect
                // on the game world after the restores below is "nothing was paid".
                RestoreBeaconPieces(beaconPieces);
                RestoreNetworkPieces(networkPieces);
                return false;
            }

            foreach (DetachedBeaconPiece piece in beaconPieces)
            {
                piece.Piece.Destroy();
            }
            foreach (DetachedNetworkPiece piece in networkPieces)
            {
                piece.Piece.Destroy();
            }
            foreach (DataNetwork network in touchedNetworks)
            {
                network.MarkBytesDirty();
            }

            return true;
        }
    }
}
