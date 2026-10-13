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

        // Beacon/network snapshotting and the transactional detach/restore/pay sequence now live in
        // SilverPaymentUtility, shared with UniversalTradeHubCompat.

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

                List<Thing> beaconStacks = SilverPaymentUtility.SnapshotBeaconSilverStacks(map);
                int beaconSilver = SilverPaymentUtility.SumBeaconStacks(beaconStacks);
                if (beaconSilver >= price)
                {
                    // Beacon silver alone covers it - leave the existing vanilla payment path alone.
                    return true;
                }

                List<SilverPaymentUtility.NetworkSilverStack> networkStacks = SilverPaymentUtility.SnapshotNetworkSilverStacks(map);
                int networkSilver = SilverPaymentUtility.SumNetworkStacks(networkStacks);
                if (beaconSilver + networkSilver < price)
                {
                    Messages.Message(
                        "MN_BTD_NotEnoughSilverForPurchase".Translate(price, beaconSilver + networkSilver),
                        MessageTypeDefOf.RejectInput,
                        historical: false);
                    return false;
                }

                if (!SilverPaymentUtility.TryPayCombinedSilver(map, price, out int beaconSpent, out int networkSpent))
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
