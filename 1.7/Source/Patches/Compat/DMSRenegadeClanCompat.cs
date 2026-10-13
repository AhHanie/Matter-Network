using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    // Soft compat: makes The Dead Man's Switch - Renegade Clan (DMSRC) charge the full silver price
    // for trade requests, bandwidth subscriptions, and sales even when the affordable amount includes
    // silver held in Matter Network storage rather than only powered orbital trade beacon cells.
    //
    // DMSRC's own AllLaunchableThingsForTrade-derived silver display already counts Matter Network
    // silver (via this mod's Patches\TradeUtility.cs postfix), so its dialogs can show/allow a
    // purchase that network silver alone (or beacon+network combined) can afford. But DMSRC.TradeRequest
    // .TrySave, DMSRC.BandwidthRequest.TrySave/Tick all pay via vanilla TradeUtility.LaunchSilver, which
    // only searches powered beacon cells: if beacon silver runs short, it logs
    // "Could not find any Silver to transfer to trader" and silently returns without paying the
    // remainder, while DMSRC still proceeds as if payment succeeded (schedules the request, sets
    // ticksPayed/arrived, applies the mechanitor hediff). Left unpatched this lets network silver
    // enable a purchase the game then under-pays or doesn't pay for at all.
    //
    // Each affected method gets a narrow transpiler that swaps its single (or, for
    // BandwidthRequest.Tick, exactly two) TradeUtility.ColonyHasEnoughSilver call(s) for TryPaySilver -
    // same (Map, int) -> bool signature, but a true result means the fee has already been taken from
    // beacon silver first, then Matter Network silver. The corresponding TradeUtility.LaunchSilver
    // call(s) further down the same method are swapped for a no-op with the same (Map, int) signature,
    // so vanilla's own beacon-only payment never runs a second time. Because the affordability check in
    // all three methods already gates every stock/state mutation that follows it (goods moved out of
    // renegades.things, ticksPayed/arrived/hediff set), moving the actual payment into that same check
    // means a failed payment leaves everything - stock, request registration, goodwill, bandwidth -
    // exactly as if the check itself had failed, with no separate rollback needed. Each existing
    // "NeedSilverLaunchable"/expiry rejection path is reused unchanged.
    //
    // DMSRC.SellRequest.TrySave has no silver-affordability check at all (it's a sale: the colony
    // receives silver, doesn't spend it), but its full-stack sell path calls Thing.DeSpawn() on the
    // item unconditionally; a network-held item is never Spawned, so this logs a
    // "Tried to despawn ... which is not spawned" error. A narrow transpiler swaps that DeSpawn call for
    // a guarded helper that only despawns when the thing is actually spawned (Thing.Destroy() right
    // after it already tolerates unspawned things). A postfix then marks every network on the sale's map
    // dirty once the sale is accepted, matching the "mark all networks on the map dirty after a batch
    // destroy/split" convention this mod's TradeNetworkCompat already uses, rather than resolving each
    // sold item back to a specific network - Matter Network map networks are few and MarkBytesDirty()
    // is a cheap lazy-invalidation flag, so the coarser approach costs nothing observable.
    //
    // No compile-time reference to DMSRenegadeClan.dll or a mandatory About.xml dependency; everything
    // is resolved via reflection and every patch is independently gated so a change to one DMSRC method
    // does not disable the fixes for the others.
    public static class DMSRenegadeClanCompat
    {
        private const string PackageId = "GoGaTio.DeadManSwitch.RenegadeClan";
        private const string AssemblyName = "DMSRenegadeClan";

        private static readonly Type TradeRequestType = AccessTools.TypeByName("DMSRC.TradeRequest");
        private static readonly Type BandwidthRequestType = AccessTools.TypeByName("DMSRC.BandwidthRequest");
        private static readonly Type SellRequestType = AccessTools.TypeByName("DMSRC.SellRequest");
        private static readonly Type GameComponentRenegadesType = AccessTools.TypeByName("DMSRC.GameComponent_Renegades");
        private static readonly Type RenegadesRequestType = AccessTools.TypeByName("DMSRC.RenegadesRequest");

        private static readonly MethodInfo TradeRequestTrySaveMethod =
            TradeRequestType != null && GameComponentRenegadesType != null
                ? AccessTools.Method(TradeRequestType, "TrySave", new[] { GameComponentRenegadesType })
                : null;

        private static readonly MethodInfo BandwidthRequestTrySaveMethod =
            BandwidthRequestType != null && GameComponentRenegadesType != null
                ? AccessTools.Method(BandwidthRequestType, "TrySave", new[] { GameComponentRenegadesType })
                : null;

        private static readonly MethodInfo BandwidthRequestTickMethod =
            BandwidthRequestType != null ? AccessTools.Method(BandwidthRequestType, "Tick", Type.EmptyTypes) : null;

        private static readonly MethodInfo SellRequestTrySaveMethod =
            SellRequestType != null && GameComponentRenegadesType != null
                ? AccessTools.Method(SellRequestType, "TrySave", new[] { GameComponentRenegadesType })
                : null;

        private static readonly MethodInfo MapGetter =
            RenegadesRequestType != null ? AccessTools.PropertyGetter(RenegadesRequestType, "Map") : null;

        private static readonly MethodInfo ColonyHasEnoughSilverMethod =
            AccessTools.Method(typeof(TradeUtility), "ColonyHasEnoughSilver", new[] { typeof(Map), typeof(int) });
        private static readonly MethodInfo LaunchSilverMethod =
            AccessTools.Method(typeof(TradeUtility), "LaunchSilver", new[] { typeof(Map), typeof(int) });
        // DMSRC's shipped build was compiled against a RimWorld version where this call's embedded IL
        // token resolves to the abstract Entity.DeSpawn declaration rather than Thing's override of it
        // (confirmed by decompiling the installed DMSRenegadeClan.dll with ilspycmd, which shows the
        // call as `((Entity)item.thing).DeSpawn(...)`) - Thing.DeSpawn is a different MethodInfo at the
        // reflection level even though it's the same override chain, so the anchor must target Entity.
        private static readonly MethodInfo DeSpawnMethod =
            AccessTools.Method(typeof(Entity), "DeSpawn", new[] { typeof(DestroyMode) });

        private static readonly MethodInfo TryPaySilverMethod =
            AccessTools.Method(typeof(DMSRenegadeClanCompat), nameof(TryPaySilver), new[] { typeof(Map), typeof(int) });
        private static readonly MethodInfo NoOpLaunchSilverMethod =
            AccessTools.Method(typeof(DMSRenegadeClanCompat), nameof(NoOpLaunchSilver), new[] { typeof(Map), typeof(int) });
        private static readonly MethodInfo SafeDeSpawnMethod =
            AccessTools.Method(typeof(DMSRenegadeClanCompat), nameof(SafeDeSpawn), new[] { typeof(Thing), typeof(DestroyMode) });

        private static bool _warnedUnavailable;

        // Cross-cutting members every patch below relies on. Each patch additionally checks its own
        // target type/method so a change to one DMSRC class doesn't disable compat for the others.
        private static bool CommonAvailable()
        {
            bool available = ModsConfig.IsActive(PackageId)
                && GameComponentRenegadesType != null
                && GameComponentRenegadesType.Assembly.GetName().Name == AssemblyName
                && RenegadesRequestType != null
                && MapGetter != null
                && ColonyHasEnoughSilverMethod != null
                && LaunchSilverMethod != null
                && DeSpawnMethod != null
                && TryPaySilverMethod != null
                && NoOpLaunchSilverMethod != null
                && SafeDeSpawnMethod != null;

            if (ModsConfig.IsActive(PackageId) && !available && !_warnedUnavailable)
            {
                _warnedUnavailable = true;
                Logger.Warning("Dead Man's Switch - Renegade Clan compat: expected DMSRC API not found or changed. " +
                    "Trade requests, bandwidth payments, and item sales will fall back to Renegade Clan's own " +
                    "vanilla-only behavior, which cannot see Matter Network storage and may under-pay for silver " +
                    "shown as available, or log despawn errors for network-held items sold to the clan.");
            }

            return available;
        }

        private static bool TradeRequestAvailable() =>
            CommonAvailable() && TradeRequestType != null && TradeRequestTrySaveMethod != null;

        private static bool BandwidthTrySaveAvailable() =>
            CommonAvailable() && BandwidthRequestType != null && BandwidthRequestTrySaveMethod != null;

        private static bool BandwidthTickAvailable() =>
            CommonAvailable() && BandwidthRequestType != null && BandwidthRequestTickMethod != null;

        private static bool SellRequestAvailable() =>
            CommonAvailable() && SellRequestType != null && SellRequestTrySaveMethod != null;

        // Called in place of TradeUtility.ColonyHasEnoughSilver at each DMSRC call site: beyond
        // checking affordability, it also spends the fee atomically (beacon silver first, then Matter
        // Network silver), so a true result means the fee has already been paid in full. The
        // corresponding TradeUtility.LaunchSilver call at the same call site is neutralized via
        // NoOpLaunchSilver so payment happens exactly once.
        private static bool TryPaySilver(Map map, int fee)
        {
            if (map == null)
            {
                Logger.Error("Dead Man's Switch - Renegade Clan payment attempted with no map; rejecting.");
                return false;
            }

            if (fee == 0)
            {
                return true;
            }

            if (fee < 0)
            {
                Logger.Error($"Dead Man's Switch - Renegade Clan payment attempted with a negative fee ({fee}); rejecting.");
                return false;
            }

            List<Thing> beaconStacks = SilverPaymentUtility.SnapshotBeaconSilverStacks(map);
            int beaconSilver = SilverPaymentUtility.SumBeaconStacks(beaconStacks);
            if (beaconSilver >= fee)
            {
                // Beacon silver alone covers it - keep vanilla's own beacon-only payment path.
                TradeUtility.LaunchSilver(map, fee);
                return true;
            }

            List<SilverPaymentUtility.NetworkSilverStack> networkStacks = SilverPaymentUtility.SnapshotNetworkSilverStacks(map);
            int networkSilver = SilverPaymentUtility.SumNetworkStacks(networkStacks);
            if (beaconSilver + networkSilver < fee)
            {
                return false;
            }

            if (!SilverPaymentUtility.TryPayCombinedSilver(map, fee, out int beaconSpent, out int networkSpent))
            {
                Logger.Warning($"Dead Man's Switch - Renegade Clan payment shortfall: paid {beaconSpent + networkSpent}/{fee} " +
                    $"silver (beacon {beaconSpent}, network {networkSpent}). Rejecting; nothing was scheduled or charged.");
                return false;
            }

            return true;
        }

        // Replaces the now-redundant TradeUtility.LaunchSilver call at each DMSRC call site once
        // TryPaySilver has already taken payment for the same fee earlier in the same method.
        private static void NoOpLaunchSilver(Map map, int fee)
        {
        }

        // Replaces Thing.DeSpawn() in SellRequest.TrySave's whole-stack sell branch. A network-held
        // thing is never Spawned; vanilla DeSpawn logs an error when called on something that isn't.
        private static void SafeDeSpawn(Thing thing, DestroyMode mode)
        {
            if (thing != null && thing.Spawned)
            {
                thing.DeSpawn(mode);
            }
        }

        // Swaps every call to `from` for a call to `to` (same parameter list) in one method's IL,
        // requiring the call to occur exactly `expectedCount` times. If the count doesn't match, the
        // method is returned untouched and a warning names the target and both counts, so a future
        // DMSRC update that changes this IL shape fails loud instead of mispaying silently.
        private static IEnumerable<CodeInstruction> ReplaceCalls(IEnumerable<CodeInstruction> instructions, MethodInfo from, MethodInfo to, int expectedCount, string context)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            int matchCount = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(from))
                {
                    matchCount++;
                }
            }

            if (matchCount != expectedCount)
            {
                Logger.Error($"Dead Man's Switch - Renegade Clan compat: expected {expectedCount} call(s) to " +
                    $"{from.DeclaringType?.Name}.{from.Name} in {context}, found {matchCount}. Skipping this " +
                    "compat patch for that target; it will keep vanilla beacon-only behavior.");
                return code;
            }

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(from))
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = to;
                }
            }

            return code;
        }

        private static void MarkAllNetworksDirty(Map map)
        {
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp == null)
            {
                return;
            }

            foreach (DataNetwork network in mapComp.Networks)
            {
                network.MarkBytesDirty();
            }
        }

        [HarmonyPatch]
        public static class Patch_TradeRequest_TrySave
        {
            [HarmonyPrepare]
            public static bool Prepare() => TradeRequestAvailable();

            public static MethodBase TargetMethod() => TradeRequestTrySaveMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                const string context = "DMSRC.TradeRequest.TrySave";
                IEnumerable<CodeInstruction> paid = ReplaceCalls(instructions, ColonyHasEnoughSilverMethod, TryPaySilverMethod, 1, context);
                return ReplaceCalls(paid, LaunchSilverMethod, NoOpLaunchSilverMethod, 1, context);
            }
        }

        [HarmonyPatch]
        public static class Patch_BandwidthRequest_TrySave
        {
            [HarmonyPrepare]
            public static bool Prepare() => BandwidthTrySaveAvailable();

            public static MethodBase TargetMethod() => BandwidthRequestTrySaveMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                const string context = "DMSRC.BandwidthRequest.TrySave";
                IEnumerable<CodeInstruction> paid = ReplaceCalls(instructions, ColonyHasEnoughSilverMethod, TryPaySilverMethod, 1, context);
                return ReplaceCalls(paid, LaunchSilverMethod, NoOpLaunchSilverMethod, 1, context);
            }
        }

        [HarmonyPatch]
        public static class Patch_BandwidthRequest_Tick
        {
            [HarmonyPrepare]
            public static bool Prepare() => BandwidthTickAvailable();

            public static MethodBase TargetMethod() => BandwidthRequestTickMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                const string context = "DMSRC.BandwidthRequest.Tick";
                IEnumerable<CodeInstruction> paid = ReplaceCalls(instructions, ColonyHasEnoughSilverMethod, TryPaySilverMethod, 2, context);
                return ReplaceCalls(paid, LaunchSilverMethod, NoOpLaunchSilverMethod, 2, context);
            }
        }

        [HarmonyPatch]
        public static class Patch_SellRequest_TrySave
        {
            [HarmonyPrepare]
            public static bool Prepare() => SellRequestAvailable();

            public static MethodBase TargetMethod() => SellRequestTrySaveMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return ReplaceCalls(instructions, DeSpawnMethod, SafeDeSpawnMethod, 1, "DMSRC.SellRequest.TrySave");
            }

            public static void Postfix(object __instance, AcceptanceReport __result)
            {
                if (!__result.Accepted)
                {
                    return;
                }

                Map map = MapGetter.Invoke(__instance, null) as Map;
                if (map == null)
                {
                    return;
                }

                MarkAllNetworksDirty(map);
            }
        }
    }
}
