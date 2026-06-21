using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class EmpireCompat
    {
        private const string PackageId = "matathias.empire";

        // ── Reflected Empire types ────────────────────────────────────────────
        private static readonly Type paymentUtilType =
            AccessTools.TypeByName("FactionColonies.PaymentUtil");

        private static readonly Type silverPaymentContextType =
            AccessTools.TypeByName("FactionColonies.SilverPaymentContext");

        private static readonly Type silverPaymentRegistryType =
            AccessTools.TypeByName("FactionColonies.SilverPaymentRegistry");

        private static readonly Type worldSettlementType =
            AccessTools.TypeByName("FactionColonies.WorldSettlementFC");

        // ── Reflected Empire members ──────────────────────────────────────────
        private static readonly MethodInfo getSilverMethod =
            AccessTools.Method(paymentUtilType, "GetSilver");

        private static readonly MethodInfo paySilverMethod =
            AccessTools.Method(paymentUtilType, "PaySilver");

        private static readonly ConstructorInfo silverPaymentContextCtor =
            FindSilverPaymentContextCtor();

        private static readonly MethodInfo invokeModifiersMethod =
            AccessTools.Method(silverPaymentRegistryType, "InvokeModifiers");

        private static readonly FieldInfo contextAmountField =
            AccessTools.Field(silverPaymentContextType, "Amount");

        private static ConstructorInfo FindSilverPaymentContextCtor()
        {
            if (silverPaymentContextType == null) return null;
            if (worldSettlementType != null)
            {
                ConstructorInfo ctor = AccessTools.Constructor(silverPaymentContextType,
                    new[] { typeof(int), typeof(string), worldSettlementType });
                if (ctor != null) return ctor;
            }
            // Fallback: any ctor whose first two params are int, string
            foreach (ConstructorInfo ctor in silverPaymentContextType.GetConstructors())
            {
                ParameterInfo[] ps = ctor.GetParameters();
                if (ps.Length >= 2
                    && ps[0].ParameterType == typeof(int)
                    && ps[1].ParameterType == typeof(string))
                    return ctor;
            }
            return null;
        }

        // ── Availability guards ───────────────────────────────────────────────
        private static bool IsAvailable() =>
            ModsConfig.IsActive(PackageId)
            && paymentUtilType != null
            && getSilverMethod != null
            && paySilverMethod != null;

        private static bool IsPaymentReplacementAvailable() =>
            IsAvailable()
            && silverPaymentContextType != null
            && silverPaymentRegistryType != null
            && silverPaymentContextCtor != null
            && invokeModifiersMethod != null
            && contextAmountField != null;

        // ── Network silver helpers ────────────────────────────────────────────

        private static int CountNetworkSilver()
        {
            int total = 0;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp == null) continue;
                foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
                {
                    foreach (Thing thing in network.StoredItems)
                    {
                        if (thing == null || thing.Destroyed || thing.stackCount <= 0) continue;
                        if (thing.def != ThingDefOf.Silver) continue;
                        total += thing.stackCount;
                    }
                }
            }
            return total;
        }

        private static int ConsumeSpawnedStoredSilver(int amount)
        {
            if (amount <= 0) return 0;
            int consumed = 0;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                List<Thing> stacks = new List<Thing>(
                    map.listerThings.ThingsOfDef(ThingDefOf.Silver));
                foreach (Thing stack in stacks)
                {
                    if (stack.Destroyed || !stack.IsInAnyStorage()) continue;
                    int take = Math.Min(amount - consumed, stack.stackCount);
                    if (take <= 0) break;
                    if (take >= stack.stackCount)
                        stack.Destroy(DestroyMode.Vanish);
                    else
                        stack.SplitOff(take).Destroy(DestroyMode.Vanish);
                    consumed += take;
                    if (consumed >= amount) return consumed;
                }
            }
            return consumed;
        }

        private static int ConsumeNetworkSilver(int amount)
        {
            if (amount <= 0) return 0;
            int consumed = 0;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp == null) continue;
                foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
                {
                    bool networkChanged = false;
                    List<Thing> items = new List<Thing>(network.StoredItems);
                    items.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
                    foreach (Thing thing in items)
                    {
                        if (thing == null || thing.Destroyed || thing.stackCount <= 0) continue;
                        if (thing.def != ThingDefOf.Silver) continue;
                        int take = Math.Min(amount - consumed, thing.stackCount);
                        if (take <= 0) break;
                        Thing taken = thing.SplitOff(take);
                        if (taken != null && !taken.Destroyed)
                        {
                            consumed += taken.stackCount;
                            taken.Destroy(DestroyMode.Vanish);
                            networkChanged = true;
                        }
                        if (consumed >= amount) break;
                    }
                    if (networkChanged)
                        network.MarkBytesDirty();
                    if (consumed >= amount) return consumed;
                }
            }
            return consumed;
        }

        private static int ApplyPaymentModifiers(int amount, string reason, object settlement)
        {
            try
            {
                object context = silverPaymentContextCtor.Invoke(
                    new object[] { amount, reason, settlement });
                invokeModifiersMethod.Invoke(null, new object[] { context });
                return (int)contextAmountField.GetValue(context);
            }
            catch (Exception ex)
            {
                Log.Warning("[Matter Network] EmpireCompat.ApplyPaymentModifiers: " + ex.Message);
                return amount;
            }
        }

        // ── Patch 1: Add network silver to Empire affordability ───────────────
        [HarmonyPatch]
        public static class Patch_GetSilver
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() => getSilverMethod;

            public static void Postfix(ref int __result)
            {
                __result += CountNetworkSilver();
            }
        }

        // ── Patch 2: Replace Empire silver consumption with network fallback ──
        [HarmonyPatch]
        public static class Patch_PaySilver
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsPaymentReplacementAvailable();

            public static MethodBase TargetMethod() => paySilverMethod;

            public static bool Prefix(object[] __args, ref bool __result)
            {
                int amount = __args != null && __args.Length > 0 && __args[0] is int rawAmount
                    ? rawAmount : 0;
                string reason = __args != null && __args.Length > 1 ? __args[1] as string : null;
                object settlement = __args != null && __args.Length > 2 ? __args[2] : null;

                amount = ApplyPaymentModifiers(amount, reason, settlement);
                if (amount <= 0)
                {
                    __result = true;
                    return false;
                }

                int remaining = amount;
                remaining -= ConsumeSpawnedStoredSilver(remaining);
                if (remaining > 0)
                    remaining -= ConsumeNetworkSilver(remaining);

                __result = true;
                return false;
            }
        }
    }
}
