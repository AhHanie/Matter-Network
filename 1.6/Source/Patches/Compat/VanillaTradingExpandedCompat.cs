using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class VanillaTradingExpandedCompat
    {
        private const string PackageId = "VanillaExpanded.VanillaTradingExpanded";

        private static readonly Type bankType = AccessTools.TypeByName("VanillaTradingExpanded.Bank");
        private static readonly Type contractType = AccessTools.TypeByName("VanillaTradingExpanded.Contract");
        private static readonly Type windowBankType = AccessTools.TypeByName("VanillaTradingExpanded.Window_Bank");
        private static readonly Type windowStockMarketType = AccessTools.TypeByName("VanillaTradingExpanded.Window_StockMarket");
        private static readonly Type windowPerformTransactionCostsType = AccessTools.TypeByName("VanillaTradingExpanded.Window_PerformTransactionCosts");
        private static readonly Type transactionProcessType = AccessTools.TypeByName("VanillaTradingExpanded.TransactionProcess");
        private static readonly Type tradingManagerType = AccessTools.TypeByName("VanillaTradingExpanded.TradingManager");

        private static readonly MethodInfo depositSilverMethod = AccessTools.Method(bankType, "DepositSilver", new[] { typeof(List<Thing>), typeof(int) });

        private static readonly FieldInfo contractItemField = AccessTools.Field(contractType, "item");
        private static readonly FieldInfo contractStuffField = AccessTools.Field(contractType, "stuff");
        private static readonly FieldInfo contractAmountField = AccessTools.Field(contractType, "amount");
        private static readonly FieldInfo contractMapToTakeItemsField = AccessTools.Field(contractType, "mapToTakeItems");
        private static readonly MethodInfo foundItemsInMapMethod = AccessTools.Method(contractType, "FoundItemsInMap", new[] { typeof(Map) });

        private static readonly FieldInfo windowBankNegotiatorField = AccessTools.Field(windowBankType, "negotiator");
        private static readonly MethodInfo availableSilverGetter = AccessTools.PropertyGetter(windowBankType, "AvailableSilver");

        private static readonly MethodInfo doWindowContentsStockMarketMethod = AccessTools.Method(windowStockMarketType, "DoWindowContents", new[] { typeof(Rect) });
        private static readonly MethodInfo doWindowContentsCostsMethod = AccessTools.Method(windowPerformTransactionCostsType, "DoWindowContents", new[] { typeof(Rect) });

        private static readonly FieldInfo amountToSpendField = AccessTools.Field(transactionProcessType, "amountToSpend");
        private static readonly FieldInfo transactionCostField = AccessTools.Field(transactionProcessType, "transactionCost");
        private static readonly FieldInfo allMoneyInBanksField = AccessTools.Field(transactionProcessType, "allMoneyInBanks");
        private static readonly MethodInfo performTransactionMethod = AccessTools.Method(transactionProcessType, "PerformTransaction");

        private static readonly MethodInfo completeContractMethod = contractType != null
            ? AccessTools.Method(tradingManagerType, "CompleteContract", new[] { contractType })
            : null;

        private static readonly MethodInfo networkSilverForCurrentMapMethod =
            AccessTools.Method(typeof(VanillaTradingExpandedCompat), nameof(NetworkSilverForCurrentMap));
        private static readonly MethodInfo canPayWithNetworkMethod =
            AccessTools.Method(typeof(VanillaTradingExpandedCompat), nameof(CanPayWithNetwork));

        private static bool IsModActive() => ModsConfig.IsActive(PackageId);

        private static bool IsBankAvailable() =>
            IsModActive() && bankType != null && windowBankType != null
            && depositSilverMethod != null && windowBankNegotiatorField != null && availableSilverGetter != null;

        private static bool IsContractAvailable() =>
            IsModActive() && contractType != null && foundItemsInMapMethod != null
            && contractItemField != null && contractStuffField != null && contractAmountField != null;

        private static bool IsTradingManagerAvailable() =>
            IsModActive() && tradingManagerType != null && completeContractMethod != null && contractMapToTakeItemsField != null;

        private static bool IsStockMarketAvailable() =>
            IsModActive() && windowStockMarketType != null && doWindowContentsStockMarketMethod != null
            && allMoneyInBanksField != null && networkSilverForCurrentMapMethod != null;

        private static bool IsTransactionCostsAvailable() =>
            IsModActive() && windowPerformTransactionCostsType != null && doWindowContentsCostsMethod != null
            && transactionCostField != null && canPayWithNetworkMethod != null;

        private static bool IsTransactionAvailable() =>
            IsModActive() && transactionProcessType != null && performTransactionMethod != null
            && transactionCostField != null && amountToSpendField != null;

        // ── Shared network helpers ──────────────────────────────────────────

        private static Map ResolveDefaultMap()
        {
            Map current = Find.CurrentMap;
            if (current != null && current.IsPlayerHome) return current;
            return Find.AnyPlayerHomeMap;
        }

        private static int CountNetworkSilver(Map map)
        {
            if (map == null) return 0;

            int total = 0;
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp == null) return 0;

            foreach (DataNetwork network in mapComp.Networks)
            {
                foreach (Thing thing in network.StoredItems)
                {
                    if (thing != null && !thing.Destroyed && thing.def == ThingDefOf.Silver)
                    {
                        total += thing.stackCount;
                    }
                }
            }

            return total;
        }

        private static int ConsumeNetworkSilver(Map map, int amount)
        {
            if (map == null || amount <= 0) return 0;

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp == null) return 0;

            int consumed = 0;
            foreach (DataNetwork network in mapComp.Networks)
            {
                List<Thing> items = new List<Thing>(network.StoredItems);
                items.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

                bool networkChanged = false;
                foreach (Thing thing in items)
                {
                    if (thing == null || thing.Destroyed || thing.def != ThingDefOf.Silver) continue;

                    int take = Math.Min(amount - consumed, thing.stackCount);
                    if (take <= 0) continue;

                    thing.SplitOff(take).Destroy(DestroyMode.Vanish);
                    consumed += take;
                    networkChanged = true;

                    if (consumed >= amount) break;
                }

                if (networkChanged) network.MarkBytesDirty();
                if (consumed >= amount) break;
            }

            return consumed;
        }

        private static void MarkAllNetworksDirty(Map map)
        {
            if (map == null) return;
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp == null) return;

            foreach (DataNetwork network in mapComp.Networks)
            {
                network.MarkBytesDirty();
            }
        }

        // Injected via transpiler into Window_StockMarket.DoWindowContents; must take no args
        // since it is spliced directly into the existing sum expression.
        private static int NetworkSilverForCurrentMap()
        {
            return CountNetworkSilver(ResolveDefaultMap());
        }

        // Replaces the `ceq` in Window_PerformTransactionCosts' `allMoneyToSpend == transactionCost`
        // check; same two-int-args-to-bool stack shape as ceq, so the swap is a drop-in.
        private static bool CanPayWithNetwork(int allMoneyToSpend, int transactionCost)
        {
            if (allMoneyToSpend >= transactionCost) return true;

            int shortfall = transactionCost - allMoneyToSpend;
            return CountNetworkSilver(ResolveDefaultMap()) >= shortfall;
        }

        // ── Feature 1: Bank deposits can use network silver ─────────────────

        [HarmonyPatch]
        public static class Patch_AvailableSilver
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsBankAvailable();

            public static MethodBase TargetMethod() => availableSilverGetter;

            public static void Postfix(object __instance, ref List<Thing> __result)
            {
                Pawn negotiator = windowBankNegotiatorField.GetValue(__instance) as Pawn;
                Map map = negotiator?.Map;
                if (map == null) return;

                List<Thing> combined = __result != null ? new List<Thing>(__result) : new List<Thing>();
                HashSet<Thing> existing = new HashSet<Thing>(combined);

                foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (thing == null || thing.Destroyed || thing.stackCount <= 0) continue;
                    if (thing.def != ThingDefOf.Silver) continue;
                    if (!existing.Add(thing)) continue;

                    combined.Add(thing);
                }

                __result = combined;
            }
        }

        [HarmonyPatch]
        public static class Patch_DepositSilver
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsBankAvailable();

            public static MethodBase TargetMethod() => depositSilverMethod;

            public static void Prefix(List<Thing> silvers, out List<Map> __state)
            {
                __state = new List<Map>();
                if (silvers == null) return;

                foreach (Thing thing in silvers)
                {
                    Map map = thing?.MapHeld;
                    if (map != null && !__state.Contains(map))
                    {
                        __state.Add(map);
                    }
                }
            }

            public static void Postfix(List<Map> __state)
            {
                if (__state == null) return;
                foreach (Map map in __state)
                {
                    MarkAllNetworksDirty(map);
                }
            }
        }

        // ── Feature 2/3: Stock purchases and transaction-cost windows can spend network silver ─

        [HarmonyPatch]
        public static class Patch_StockMarketDoWindowContents
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsStockMarketAvailable();

            public static MethodBase TargetMethod() => doWindowContentsStockMarketMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> list = new List<CodeInstruction>(instructions);
                bool patched = false;

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].opcode == OpCodes.Stfld && Equals(list[i].operand, allMoneyInBanksField))
                    {
                        list.Insert(i, new CodeInstruction(OpCodes.Add));
                        list.Insert(i, new CodeInstruction(OpCodes.Call, networkSilverForCurrentMapMethod));
                        patched = true;
                        break;
                    }
                }

                if (!patched)
                {
                    Logger.Warning("[Matter Network] VTE Window_StockMarket.allMoneyInBanks assignment not found; network silver will not count toward stock purchases.");
                }

                return list;
            }
        }

        [HarmonyPatch]
        public static class Patch_PerformTransactionCostsDoWindowContents
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsTransactionCostsAvailable();

            public static MethodBase TargetMethod() => doWindowContentsCostsMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> list = new List<CodeInstruction>(instructions);
                bool patched = false;

                for (int i = 0; i < list.Count - 1; i++)
                {
                    if (list[i].opcode == OpCodes.Ldfld && Equals(list[i].operand, transactionCostField)
                        && list[i + 1].opcode == OpCodes.Ceq)
                    {
                        list[i + 1] = new CodeInstruction(OpCodes.Call, canPayWithNetworkMethod);
                        patched = true;
                        break;
                    }
                }

                if (!patched)
                {
                    Logger.Warning("[Matter Network] VTE Window_PerformTransactionCosts canPay check not found; network silver will not count toward contract/stock payments.");
                }

                return list;
            }
        }

        [HarmonyPatch]
        public static class Patch_PerformTransaction
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsTransactionAvailable();

            public static MethodBase TargetMethod() => performTransactionMethod;

            public static bool Prefix(object __instance)
            {
                int transactionCost = (int)transactionCostField.GetValue(__instance);
                if (transactionCost <= 0) return true;

                IDictionary amountToSpend = amountToSpendField.GetValue(__instance) as IDictionary;
                int allMoneyToSpend = 0;
                if (amountToSpend != null)
                {
                    foreach (object value in amountToSpend.Values)
                    {
                        allMoneyToSpend += (int)value;
                    }
                }

                if (allMoneyToSpend >= transactionCost) return true;

                int shortfall = transactionCost - allMoneyToSpend;
                Map map = ResolveDefaultMap();
                if (map == null || CountNetworkSilver(map) < shortfall)
                {
                    Messages.Message("Not enough silver (including Matter Network storage) to complete this transaction.", MessageTypeDefOf.RejectInput, historical: false);
                    return false;
                }

                ConsumeNetworkSilver(map, shortfall);
                return true;
            }
        }

        // ── Feature 4: NPC contract completion sees network items ───────────

        [HarmonyPatch]
        public static class Patch_FoundItemsInMap
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsContractAvailable();

            public static MethodBase TargetMethod() => foundItemsInMapMethod;

            public static void Postfix(object __instance, Map map, ref List<Thing> __result)
            {
                if (map == null) return;

                ThingDef item = contractItemField.GetValue(__instance) as ThingDef;
                if (item == null) return;

                ThingDef stuff = contractStuffField.GetValue(__instance) as ThingDef;
                int amount = (int)contractAmountField.GetValue(__instance);

                List<Thing> things = __result ?? new List<Thing>();
                int covered = 0;
                foreach (Thing t in things) covered += t.stackCount;
                if (covered >= amount) return;

                HashSet<Thing> existing = new HashSet<Thing>(things);
                List<Thing> combined = new List<Thing>(things);

                foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (covered >= amount) break;
                    if (thing == null || thing.Destroyed || thing.stackCount <= 0) continue;
                    if (thing.def != item) continue;
                    if (stuff != null && thing.Stuff != stuff) continue;
                    if (!existing.Add(thing)) continue;

                    combined.Add(thing);
                    covered += thing.stackCount;
                }

                __result = combined;
            }
        }

        [HarmonyPatch]
        public static class Patch_CompleteContract
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsTradingManagerAvailable();

            public static MethodBase TargetMethod() => completeContractMethod;

            public static void Postfix(object contract)
            {
                Map map = contractMapToTakeItemsField.GetValue(contract) as Map ?? Find.AnyPlayerHomeMap;
                MarkAllNetworksDirty(map);
            }
        }
    }
}
