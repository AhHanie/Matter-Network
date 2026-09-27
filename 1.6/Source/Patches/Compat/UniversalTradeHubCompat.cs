using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network.Patches
{
    // Soft compat: lets Universal Trade Hub (UTH) orders and subscriptions spend silver held in an
    // extractable Matter Network, including payments split between powered orbital trade beacon
    // cells and network storage.
    //
    // UTH_UIUtility.CalculateTotalAvailableSilver sums TradeUtility.AllLaunchableThingsForTrade,
    // which this mod's own patch already extends with network silver - so UTH's menus show network
    // silver as spendable. But UTH_FinalOrderMenu.PlaceOrderAndScheduleDelivery and
    // UTH_SubscriptionMenu.ProcessPurchase both pay via vanilla TradeUtility.LaunchSilver, which only
    // searches powered beacon cells; if beacon silver runs out mid-payment, vanilla logs and returns
    // silently, and both UTH methods schedule the order / activate the subscription regardless.
    //
    // Each payment method gets a narrow transpiler that swaps its single LaunchSilver call for
    // TryPaySilver (beacon-first, then network, atomically) and returns early on failure, so nothing
    // is scheduled/activated without full payment. DoWindowContents on both menus gets a prefix that
    // refreshes the stale totalAvailableSilver field so the confirm button's affordability check
    // matches what TryPaySilver can actually pay. DoWindowContents also gets a narrow transpiler that
    // skips the order menu's Clear()/Close() (and the subscription menu's active-subscription
    // bookkeeping) when the payment it just triggered failed, so a rejected purchase doesn't wipe the
    // player's cart or falsely flip the subscription UI. The optimistic success sound/message UTH
    // plays before calling into payment can still fire in the rare case the refreshed balance check
    // passes but the atomic payment then fails (e.g. another consumer touched the same silver in
    // between); TryPaySilver's own rejection message still fires immediately after, and no order or
    // subscription is actually created.
    //
    // No compile-time reference to Universal Trade Hub; everything is resolved via reflection and
    // each patch is skipped (with a warning) if UTH isn't loaded or its API has changed.
    public static class UniversalTradeHubCompat
    {
        private const string AssemblyName = "Universal Trade Hub";

        private static readonly Type FinalOrderMenuType =
            AccessTools.TypeByName("Universal_Trade_Hub.UTH_FinalOrderMenu");
        private static readonly Type SubscriptionMenuType =
            AccessTools.TypeByName("Universal_Trade_Hub.UTH_SubscriptionMenu");
        private static readonly Type UIUtilityType =
            AccessTools.TypeByName("Universal_Trade_Hub.UTH_UIUtility");

        private static readonly MethodInfo PlaceOrderMethod =
            FinalOrderMenuType != null
                ? AccessTools.Method(FinalOrderMenuType, "PlaceOrderAndScheduleDelivery", new[] { typeof(float), typeof(float), typeof(float) })
                : null;
        private static readonly MethodInfo ProcessPurchaseMethod =
            SubscriptionMenuType != null
                ? AccessTools.Method(SubscriptionMenuType, "ProcessPurchase", new[] { typeof(float), typeof(float) })
                : null;
        private static readonly MethodInfo DoWindowContentsOrderMethod =
            FinalOrderMenuType != null
                ? AccessTools.Method(FinalOrderMenuType, "DoWindowContents", new[] { typeof(Rect) })
                : null;
        private static readonly MethodInfo DoWindowContentsSubMethod =
            SubscriptionMenuType != null
                ? AccessTools.Method(SubscriptionMenuType, "DoWindowContents", new[] { typeof(Rect) })
                : null;
        private static readonly MethodInfo CalculateTotalAvailableSilverMethod =
            UIUtilityType != null
                ? AccessTools.Method(UIUtilityType, "CalculateTotalAvailableSilver", new[] { typeof(Map) })
                : null;

        private static readonly FieldInfo ConsoleField_Order =
            FinalOrderMenuType != null ? AccessTools.Field(FinalOrderMenuType, "console") : null;
        private static readonly FieldInfo TotalAvailableSilverField_Order =
            FinalOrderMenuType != null ? AccessTools.Field(FinalOrderMenuType, "totalAvailableSilver") : null;
        private static readonly FieldInfo ConsoleField_Sub =
            SubscriptionMenuType != null ? AccessTools.Field(SubscriptionMenuType, "console") : null;
        private static readonly FieldInfo TotalAvailableSilverField_Sub =
            SubscriptionMenuType != null ? AccessTools.Field(SubscriptionMenuType, "totalAvailableSilver") : null;

        private static readonly MethodInfo LaunchSilverMethod =
            AccessTools.Method(typeof(TradeUtility), "LaunchSilver", new[] { typeof(Map), typeof(int) });
        private static readonly MethodInfo TryPaySilverMethod =
            AccessTools.Method(typeof(UniversalTradeHubCompat), nameof(TryPaySilver), new[] { typeof(Map), typeof(int), typeof(object) });
        private static readonly MethodInfo ConsumeLastPaymentResultMethod =
            AccessTools.Method(typeof(UniversalTradeHubCompat), nameof(ConsumeLastPaymentResult), new[] { typeof(object) });

        private static bool _warnedUnavailable;

        private static bool CoreAvailable()
        {
            bool available = FinalOrderMenuType != null
                && FinalOrderMenuType.Assembly.GetName().Name == AssemblyName
                && SubscriptionMenuType != null
                && UIUtilityType != null
                && PlaceOrderMethod != null
                && ProcessPurchaseMethod != null
                && DoWindowContentsOrderMethod != null
                && DoWindowContentsSubMethod != null
                && CalculateTotalAvailableSilverMethod != null
                && ConsoleField_Order != null && TotalAvailableSilverField_Order != null
                && ConsoleField_Sub != null && TotalAvailableSilverField_Sub != null
                && LaunchSilverMethod != null && TryPaySilverMethod != null && ConsumeLastPaymentResultMethod != null;

            if (FinalOrderMenuType != null && !available && !_warnedUnavailable)
            {
                _warnedUnavailable = true;
                Logger.Warning("Universal Trade Hub compat: expected UTH_FinalOrderMenu/UTH_SubscriptionMenu API " +
                    "not found or changed. Orders and subscriptions will fall back to vanilla beacon-only payment, " +
                    "which cannot see Matter Network storage and may under-pay for silver shown as available.");
            }

            return available;
        }

        // Per-menu-instance payment outcome, set by TryPaySilver and consumed once by the
        // DoWindowContents UI guard, so a stale/prior result can never authorize a later attempt and
        // a menu instance that's garbage collected doesn't leak an entry.
        private static readonly ConditionalWeakTable<object, StrongBox<bool>> LastPaymentResult =
            new ConditionalWeakTable<object, StrongBox<bool>>();

        // Called from the transpiled PlaceOrderAndScheduleDelivery/ProcessPurchase in place of their
        // original TradeUtility.LaunchSilver(map, fee) call.
        private static bool TryPaySilver(Map map, int fee, object menu)
        {
            bool result = TryPaySilverCore(map, fee);
            if (menu != null)
            {
                LastPaymentResult.Remove(menu);
                LastPaymentResult.Add(menu, new StrongBox<bool>(result));
            }
            return result;
        }

        private static bool ConsumeLastPaymentResult(object menu)
        {
            if (menu != null && LastPaymentResult.TryGetValue(menu, out StrongBox<bool> box))
            {
                LastPaymentResult.Remove(menu);
                return box.Value;
            }

            // No recorded attempt: fail open so this purely cosmetic UI guard never blocks a purchase
            // that TryPaySilver itself never rejected.
            return true;
        }

        private static bool TryPaySilverCore(Map map, int fee)
        {
            if (map == null)
            {
                Logger.Error("Universal Trade Hub purchase attempted with no map; rejecting.");
                return false;
            }

            if (fee <= 0)
            {
                return true;
            }

            List<Thing> beaconStacks = SilverPaymentUtility.SnapshotBeaconSilverStacks(map);
            int beaconSilver = SilverPaymentUtility.SumBeaconStacks(beaconStacks);
            if (beaconSilver >= fee)
            {
                // Beacon silver alone covers it - keep vanilla's own payment path.
                TradeUtility.LaunchSilver(map, fee);
                return true;
            }

            List<SilverPaymentUtility.NetworkSilverStack> networkStacks = SilverPaymentUtility.SnapshotNetworkSilverStacks(map);
            int networkSilver = SilverPaymentUtility.SumNetworkStacks(networkStacks);
            if (beaconSilver + networkSilver < fee)
            {
                Messages.Message(
                    "MN_UTH_NotEnoughSilverForPurchase".Translate(fee, beaconSilver + networkSilver),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return false;
            }

            if (!SilverPaymentUtility.TryPayCombinedSilver(map, fee, out int beaconSpent, out int networkSpent))
            {
                Logger.Warning($"Universal Trade Hub payment shortfall: paid {beaconSpent + networkSpent}/{fee} " +
                    $"silver (beacon {beaconSpent}, network {networkSpent}). Rejecting purchase; nothing will be " +
                    "scheduled or activated.");
                Messages.Message(
                    "MN_UTH_NotEnoughSilverForPurchase".Translate(fee, beaconSpent + networkSpent),
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return false;
            }

            return true;
        }

        private static void RefreshTotalAvailableSilver(object menuInstance, FieldInfo consoleField, FieldInfo totalAvailableSilverField)
        {
            Thing console = consoleField.GetValue(menuInstance) as Thing;
            Map map = console?.Map;
            if (map == null)
            {
                return;
            }

            int available = (int)CalculateTotalAvailableSilverMethod.Invoke(null, new object[] { map });
            totalAvailableSilverField.SetValue(menuInstance, (float)available);
        }

        // Replaces the single call to TradeUtility.LaunchSilver(Map,int) in `originalMethod` with a
        // call to TryPaySilver(Map,int,object) (pushing `this` as the menu instance), followed by a
        // branch back to the caller on failure so ScheduleOrder/subscription activation never runs
        // without full payment. Stack-neutral: the map/fee args already on the stack for the removed
        // call are reused as-is.
        private static IEnumerable<CodeInstruction> ReplaceLaunchSilverWithGuardedPay(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase originalMethod)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            int callIndex = -1;
            int matchCount = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(LaunchSilverMethod))
                {
                    matchCount++;
                    callIndex = i;
                }
            }

            if (matchCount != 1)
            {
                Logger.Error("Universal Trade Hub compat: expected exactly one TradeUtility.LaunchSilver call in " +
                    $"{originalMethod.DeclaringType?.Name}.{originalMethod.Name}, found {matchCount}. Skipping the " +
                    "payment guard for this method; it will keep vanilla beacon-only payment and may under-pay.");
                return code;
            }

            CodeInstruction original = code[callIndex];
            Label continueLabel = generator.DefineLabel();

            List<CodeInstruction> replacement = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, TryPaySilverMethod),
                new CodeInstruction(OpCodes.Brtrue, continueLabel),
                new CodeInstruction(OpCodes.Ret),
            };
            replacement[0].labels.AddRange(original.labels);

            code.RemoveAt(callIndex);
            code.InsertRange(callIndex, replacement);

            int continueIndex = callIndex + replacement.Count;
            if (continueIndex < code.Count)
            {
                code[continueIndex].labels.Add(continueLabel);
            }
            else
            {
                code.Add(new CodeInstruction(OpCodes.Nop));
                code[code.Count - 1].labels.Add(continueLabel);
            }

            return code;
        }

        // Guards everything after the single call to `call` with a check of its recorded payment
        // result, branching around it on failure. The branch target is either the join point the
        // compiler already emits right after this call's enclosing if-branch (the next unconditional
        // branch instruction, reused as-is) or, if this call's statement is the last thing in the
        // method, a fresh label on the method's final instruction.
        private static IEnumerable<CodeInstruction> GuardPostCallOnFailure(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodInfo call, string context)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);

            int callIndex = -1;
            int matchCount = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(call))
                {
                    matchCount++;
                    callIndex = i;
                }
            }

            if (matchCount != 1)
            {
                Logger.Error($"Universal Trade Hub compat: expected exactly one call to {call.Name} in {context}, " +
                    $"found {matchCount}. Skipping the confirmation-UI guard for this menu; a failed payment may " +
                    "still close the window/clear the cart or flip the subscription flag this build.");
                return code;
            }

            int brIndex = -1;
            for (int i = callIndex + 1; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Br || code[i].opcode == OpCodes.Br_S)
                {
                    brIndex = i;
                    break;
                }
            }

            Label targetLabel;
            if (brIndex >= 0)
            {
                targetLabel = (Label)code[brIndex].operand;
            }
            else
            {
                targetLabel = generator.DefineLabel();
                code[code.Count - 1].labels.Add(targetLabel);
            }

            List<CodeInstruction> guard = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, ConsumeLastPaymentResultMethod),
                new CodeInstruction(OpCodes.Brfalse, targetLabel),
            };

            code.InsertRange(callIndex + 1, guard);
            return code;
        }

        [HarmonyPatch]
        public static class Patch_PlaceOrderAndScheduleDelivery
        {
            [HarmonyPrepare]
            public static bool Prepare() => CoreAvailable();

            public static MethodBase TargetMethod() => PlaceOrderMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                return ReplaceLaunchSilverWithGuardedPay(instructions, generator, PlaceOrderMethod);
            }
        }

        [HarmonyPatch]
        public static class Patch_ProcessPurchase
        {
            [HarmonyPrepare]
            public static bool Prepare() => CoreAvailable();

            public static MethodBase TargetMethod() => ProcessPurchaseMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                return ReplaceLaunchSilverWithGuardedPay(instructions, generator, ProcessPurchaseMethod);
            }
        }

        [HarmonyPatch]
        public static class Patch_DoWindowContents_Order
        {
            [HarmonyPrepare]
            public static bool Prepare() => CoreAvailable();

            public static MethodBase TargetMethod() => DoWindowContentsOrderMethod;

            public static void Prefix(object __instance)
            {
                RefreshTotalAvailableSilver(__instance, ConsoleField_Order, TotalAvailableSilverField_Order);
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                return GuardPostCallOnFailure(instructions, generator, PlaceOrderMethod, "UTH_FinalOrderMenu.DoWindowContents");
            }
        }

        [HarmonyPatch]
        public static class Patch_DoWindowContents_Subscription
        {
            [HarmonyPrepare]
            public static bool Prepare() => CoreAvailable();

            public static MethodBase TargetMethod() => DoWindowContentsSubMethod;

            public static void Prefix(object __instance)
            {
                RefreshTotalAvailableSilver(__instance, ConsoleField_Sub, TotalAvailableSilverField_Sub);
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                return GuardPostCallOnFailure(instructions, generator, ProcessPurchaseMethod, "UTH_SubscriptionMenu.DoWindowContents");
            }
        }
    }
}
