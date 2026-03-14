using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Pawn_TradeTracker
    {
        [HarmonyPatch(typeof(Pawn_TraderTracker), "ReachableForTrade")]
        public static class ReachableForTrade_Patch
        {
            public static bool Prefix(Thing thing, ref bool __result)
            {
                if (thing.MapHeld == null)
                {
                    return true;
                }

                NetworksMapComponent mapComp = thing.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(thing, out DataNetwork _))
                {
                    return true;
                }

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_TraderTracker), "GiveSoldThingToTrader")]
        public static class GiveSoldThingToTrader
        {
            private static DataNetwork currentNetwork;

            public static void Prefix(Thing toGive, int countToGive, Pawn playerNegotiator)
            {
                NetworksMapComponent mapComp = playerNegotiator.Map.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(toGive, out DataNetwork network))
                {
                    currentNetwork = network;
                    Log.Message($"Removing {countToGive} of {toGive.def.defName} from network due to trade deal");
                    network.RemoveItem(toGive, countToGive, countToGive >= toGive.stackCount);
                }
            }

            public static void Postfix()
            {
                currentNetwork.ValidateNetwork();
                currentNetwork = null;
            }
        }

        [HarmonyPatch]
        public static class ColonyThingsWillingToBuy
        {
            public static MethodBase TargetMethod()
            {
                Type stateMachineType = AccessTools.Inner(typeof(Pawn_TraderTracker), "<ColonyThingsWillingToBuy>d__13");
                return AccessTools.Method(stateMachineType, nameof(System.Collections.IEnumerator.MoveNext));
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                MethodInfo allThingsGetter = AccessTools.PropertyGetter(typeof(ListerThings), nameof(ListerThings.AllThings));
                MethodInfo buildEnumerableMethod = AccessTools.Method(typeof(Patch_Pawn_TradeTracker), nameof(BuildColonyThingsWillingToBuyEnumerable));

                int getterIndex = -1;
                int storeEnumerableIndex = -1;

                for (int i = 0; i < codes.Count; i++)
                {
                    if (getterIndex == -1 && codes[i].Calls(allThingsGetter))
                    {
                        getterIndex = i;
                        continue;
                    }

                    if (getterIndex != -1 && IsStlocInstruction(codes[i], 3))
                    {
                        storeEnumerableIndex = i;
                        break;
                    }
                }

                if (getterIndex == -1 || storeEnumerableIndex == -1 || getterIndex < 4)
                {
                    Logger.Error("Pawn_TraderTracker.ColonyThingsWillingToBuy transpiler failed: could not find enumerable initialization.");
                    return codes;
                }

                int startIndex = getterIndex - 4;
                int removeCount = storeEnumerableIndex - startIndex + 1;

                codes.RemoveRange(startIndex, removeCount);
                codes.InsertRange(startIndex, new[]
                {
                    new CodeInstruction(OpCodes.Ldloc_2),
                    new CodeInstruction(OpCodes.Call, buildEnumerableMethod),
                    new CodeInstruction(OpCodes.Stloc_3)
                });

                Logger.Message("Pawn_TraderTracker.ColonyThingsWillingToBuy transpiler patched successfully.");
                return codes;
            }
        }

        public static IEnumerable<Thing> BuildColonyThingsWillingToBuyEnumerable(Pawn_TraderTracker tracker)
        {
            Pawn pawn = tracker.pawn;

            IEnumerable<Thing> outsideNetworks = pawn.Map.listerThings
                .AllThingsOutsideNetworks()
                .Where(thing => OutsideNetworkTradeValidator(tracker, thing));

            IEnumerable<Thing> inNetworks = pawn.Map.listerThings
                .AllThingsInNetworks()
                .Where(thing => InNetworkTradeValidator(tracker, thing));

            return outsideNetworks.Concat(inNetworks);
        }

        private static bool OutsideNetworkTradeValidator(Pawn_TraderTracker tracker, Thing thing)
        {
            Pawn pawn = tracker.pawn;
            return thing.def.category == ThingCategory.Item &&
                   TradeUtility.PlayerSellableNow(thing, pawn) &&
                   !thing.Position.Fogged(thing.Map) &&
                   (pawn.Map.areaManager.Home[thing.Position] || thing.IsInAnyStorage()) &&
                   tracker.ReachableForTrade(thing);
        }

        private static bool InNetworkTradeValidator(Pawn_TraderTracker tracker, Thing thing)
        {
            Pawn pawn = tracker.pawn;
            return thing.def.category == ThingCategory.Item &&
                   TradeUtility.PlayerSellableNow(thing, pawn) &&
                   tracker.ReachableForTrade(thing);
        }

        private static bool IsStlocInstruction(CodeInstruction instruction, int localIndex)
        {
            if (instruction.opcode == OpCodes.Stloc_0) return localIndex == 0;
            if (instruction.opcode == OpCodes.Stloc_1) return localIndex == 1;
            if (instruction.opcode == OpCodes.Stloc_2) return localIndex == 2;
            if (instruction.opcode == OpCodes.Stloc_3) return localIndex == 3;

            if (instruction.opcode == OpCodes.Stloc_S || instruction.opcode == OpCodes.Stloc)
            {
                if (instruction.operand is LocalBuilder localBuilder)
                {
                    return localBuilder.LocalIndex == localIndex;
                }

                if (instruction.operand is byte byteIndex)
                {
                    return byteIndex == localIndex;
                }

                if (instruction.operand is ushort ushortIndex)
                {
                    return ushortIndex == localIndex;
                }

                if (instruction.operand is int intIndex)
                {
                    return intIndex == localIndex;
                }
            }

            return false;
        }
    }
}
