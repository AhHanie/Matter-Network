using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    // Soft compat: makes Gravship Blueprints' "Purchase with silver" button able to spend silver
    // held in a usable Matter Network, not just silver in powered orbital trade beacon cells.
    //
    // BTD_GravshipBroker.ExecuteSilverPurchase(price) calls vanilla TradeUtility.LaunchSilver, which
    // only searches powered beacon cells and logs "Could not find any Silver to transfer to trader"
    // and returns without paying if beacon silver runs out - even though the button was enabled
    // because BTD_GravshipPurchaseUtility.AvailableSilver already counts Matter Network silver via
    // this mod's own AllLaunchableThingsForTrade patch. Left unpatched, network silver can enable
    // the button while the purchase consumes too little (or no) silver and still delivers the ship.
    //
    // This prefix takes over payment only when beacon silver alone cannot cover the price: it pays
    // the beacon share first (matching vanilla's beacon search), then the remainder from eligible
    // Matter Network storage, then runs the broker's own completion steps (goodwill charge,
    // delivery) itself and skips the original method so LaunchSilver never runs a second time.
    // Beacon-only and barter purchases are left completely untouched.
    //
    // No compile-time reference to Gravship Blueprints; everything is resolved via reflection and
    // the patch is skipped entirely if the mod isn't loaded or its broker internals have changed.
    public static class GravshipBlueprintsCompat
    {
        private const string PackageId = "btd.remix.gravshipblueprints";
        private const string AssemblyName = "RWM_BTD_Remix_GravshipBlueprints";

        private static readonly Type BrokerType =
            AccessTools.TypeByName("RWM_BTD_Remix_GravshipBlueprints.BTD_GravshipBroker");
        private static readonly Type PurchaseUtilityType =
            AccessTools.TypeByName("RWM_BTD_Remix_GravshipBlueprints.BTD_GravshipPurchaseUtility");
        private static readonly Type DesignType =
            AccessTools.TypeByName("RWM_BTD_Remix_GravshipBlueprints.BTD_GravshipDesign");

        private static readonly MethodInfo ExecuteSilverPurchaseMethod =
            BrokerType != null ? AccessTools.Method(BrokerType, "ExecuteSilverPurchase", new[] { typeof(int) }) : null;

        private static readonly FieldInfo NegotiatorField =
            BrokerType != null ? AccessTools.Field(BrokerType, "negotiator") : null;
        private static readonly FieldInfo DeliveredField =
            BrokerType != null ? AccessTools.Field(BrokerType, "delivered") : null;

        private static readonly MethodInfo ChargeFeeAndRecoverMethod =
            BrokerType != null ? AccessTools.Method(BrokerType, "ChargeFeeAndRecover", new[] { typeof(float) }) : null;
        private static readonly MethodInfo DesignGetter =
            BrokerType != null ? AccessTools.PropertyGetter(BrokerType, "Design") : null;
        private static readonly MethodInfo BarebonesModeGetter =
            BrokerType != null ? AccessTools.PropertyGetter(BrokerType, "BarebonesMode") : null;

        private static readonly MethodInfo DeliverMethod =
            PurchaseUtilityType != null && DesignType != null
                ? AccessTools.Method(PurchaseUtilityType, "Deliver", new[] { DesignType, typeof(bool), typeof(Pawn) })
                : null;

        private static bool _warnedUnavailable;

        private static bool IsAvailable()
        {
            bool available = ModsConfig.IsActive(PackageId)
                && BrokerType != null
                && BrokerType.Assembly.GetName().Name == AssemblyName
                && ExecuteSilverPurchaseMethod != null
                && NegotiatorField != null && DeliveredField != null
                && ChargeFeeAndRecoverMethod != null
                && DesignGetter != null && BarebonesModeGetter != null
                && DeliverMethod != null;

            if (ModsConfig.IsActive(PackageId) && !available && !_warnedUnavailable)
            {
                _warnedUnavailable = true;
                Logger.Warning("Gravship Blueprints compat: expected BTD_GravshipBroker API not found or changed. " +
                    "Direct silver purchases will fall back to vanilla behavior, which cannot see Matter Network storage " +
                    "and may allow a purchase to under-pay for its price.");
            }

            return available;
        }

        private struct DetachedBeaconPiece
        {
            public Thing Piece;
            public IntVec3 Cell;
            public Map Map;
        }

        private struct NetworkSilverStack
        {
            public Thing Thing;
            public DataNetwork Network;
        }

        private struct DetachedNetworkPiece
        {
            public Thing Piece;
            public DataNetwork Network;
        }

        // Mirrors TradeUtility.AllLaunchableThingsForTrade / LaunchThingsOfType: powered orbital
        // trade beacons, deduplicated by Thing reference because their tradeable cells can overlap.
        private static List<Thing> SnapshotBeaconSilverStacks(Map map)
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
        private static List<NetworkSilverStack> SnapshotNetworkSilverStacks(Map map)
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

        private static int SumBeaconStacks(List<Thing> stacks)
        {
            int total = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                total += stacks[i].stackCount;
            }
            return total;
        }

        private static int SumNetworkStacks(List<NetworkSilverStack> stacks)
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
        private static int DetachBeaconSilver(List<Thing> stacks, int amount, List<DetachedBeaconPiece> detached)
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

        private static int DetachNetworkSilver(List<NetworkSilverStack> stacks, int amount, List<DetachedNetworkPiece> detached, HashSet<DataNetwork> touchedNetworks)
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

        private static void RestoreBeaconPieces(List<DetachedBeaconPiece> pieces)
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
                        "after an aborted gravship purchase; that silver may be lost.");
                }
            }
        }

        private static void RestoreNetworkPieces(List<DetachedNetworkPiece> pieces)
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
                        $"{piece.Network?.NetworkId} after an aborted gravship purchase; that silver may be lost.");
                    continue;
                }

                touched.Add(piece.Network);
            }

            foreach (DataNetwork network in touched)
            {
                network.MarkBytesDirty();
            }
        }

        // Preflight and spending happen synchronously within this one Harmony prefix call - the same
        // confirmation callback that computed `price` - so the snapshots taken above cannot go stale
        // between the affordability check and the detach loops below; no ticks pass in between.
        private static bool TryPay(Map map, int price, out int beaconSpent, out int networkSpent)
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

        [HarmonyPatch]
        public static class Patch_ExecuteSilverPurchase
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() => ExecuteSilverPurchaseMethod;

            public static bool Prefix(object __instance, int price, ref bool ___delivered)
            {
                if (price <= 0 || ___delivered)
                {
                    return true;
                }

                Pawn negotiator = NegotiatorField.GetValue(__instance) as Pawn;
                Map map = negotiator?.Map;
                if (map == null)
                {
                    // Unlike the original method (which sets delivered = true here and silently
                    // no-ops), reject cleanly and leave delivered untouched so the purchase can be
                    // retried once the negotiator/map situation is sane again.
                    Logger.Error("Gravship silver purchase attempted with no negotiator map; rejecting.");
                    return false;
                }

                List<Thing> beaconStacks = SnapshotBeaconSilverStacks(map);
                int beaconSilver = SumBeaconStacks(beaconStacks);
                if (beaconSilver >= price)
                {
                    // Beacon silver alone covers it - leave the existing vanilla payment path alone.
                    return true;
                }

                List<NetworkSilverStack> networkStacks = SnapshotNetworkSilverStacks(map);
                int networkSilver = SumNetworkStacks(networkStacks);
                if (beaconSilver + networkSilver < price)
                {
                    Messages.Message(
                        "MN_BTD_NotEnoughSilverForPurchase".Translate(price, beaconSilver + networkSilver),
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                    return false;
                }

                if (!TryPay(map, price, out int beaconSpent, out int networkSpent))
                {
                    Logger.Warning($"Gravship purchase payment shortfall: paid {beaconSpent + networkSpent}/{price} " +
                        $"silver (beacon {beaconSpent}, network {networkSpent}). Rejecting purchase; no goodwill " +
                        "change or delivery will occur.");
                    Messages.Message(
                        "MN_BTD_NotEnoughSilverForPurchase".Translate(price, beaconSpent + networkSpent),
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                    return false;
                }

                ___delivered = true;

                object design = DesignGetter.Invoke(__instance, null);
                bool barebonesMode = (bool)BarebonesModeGetter.Invoke(__instance, null);

                ChargeFeeAndRecoverMethod.Invoke(__instance, new object[] { 0f });
                DeliverMethod.Invoke(null, new object[] { design, barebonesMode, negotiator });

                return false;
            }
        }
    }
}
