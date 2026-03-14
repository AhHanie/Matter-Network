using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace SK_Matter_Network.Patches
{
    [HarmonyPatch(typeof(ThingOverlays), nameof(ThingOverlays.ThingOverlaysOnGUI))]
    public static class Patch_ThingOverlays
    {
        private static readonly HashSet<IntVec3> EmptyInterfacePositions = new HashSet<IntVec3>();

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            LocalBuilder networkInterfacePositionsLocal = generator.DeclareLocal(typeof(HashSet<IntVec3>));

            MethodInfo thingsInGroupMethod = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsInGroup));
            MethodInfo currentViewContainsMethod = AccessTools.Method(typeof(CellRect), nameof(CellRect.Contains), new[] { typeof(IntVec3) });
            MethodInfo thingPositionGetter = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Position));
            MethodInfo interfaceContainsMethod = AccessTools.Method(typeof(HashSet<IntVec3>), nameof(HashSet<IntVec3>.Contains), new[] { typeof(IntVec3) });
            MethodInfo getNetworkInterfacePositionsMethod = AccessTools.Method(typeof(Patch_ThingOverlays), nameof(GetNetworkInterfacePositions));

            int cacheInsertIndex = -1;
            int positionCheckInsertIndex = -1;
            Label skipOverlayLabel = default;

            for (int i = 0; i < codes.Count; i++)
            {
                if (cacheInsertIndex == -1 &&
                    codes[i].Calls(thingsInGroupMethod) &&
                    i + 1 < codes.Count &&
                    IsStlocInstruction(codes[i + 1], 1))
                {
                    cacheInsertIndex = i + 2;
                    continue;
                }

                if (positionCheckInsertIndex == -1 &&
                    codes[i].Calls(currentViewContainsMethod) &&
                    i + 1 < codes.Count &&
                    TryGetBranchLabel(codes[i + 1], out skipOverlayLabel))
                {
                    positionCheckInsertIndex = i + 2;
                }
            }

            if (cacheInsertIndex == -1 || positionCheckInsertIndex == -1)
            {
                Logger.Error("ThingOverlays.ThingOverlaysOnGUI transpiler failed: could not find the network overlay insertion points.");
                return codes;
            }

            List<CodeInstruction> cacheInstructions = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Find), nameof(Find.CurrentMap))),
                new CodeInstruction(OpCodes.Call, getNetworkInterfacePositionsMethod),
                new CodeInstruction(OpCodes.Stloc, networkInterfacePositionsLocal)
            };

            codes.InsertRange(cacheInsertIndex, cacheInstructions);

            if (positionCheckInsertIndex >= cacheInsertIndex)
            {
                positionCheckInsertIndex += cacheInstructions.Count;
            }

            List<CodeInstruction> positionCheckInstructions = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc, networkInterfacePositionsLocal),
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Callvirt, thingPositionGetter),
                new CodeInstruction(OpCodes.Callvirt, interfaceContainsMethod),
                new CodeInstruction(OpCodes.Brtrue, skipOverlayLabel)
            };

            codes.InsertRange(positionCheckInsertIndex, positionCheckInstructions);
            Logger.Message("ThingOverlays.ThingOverlaysOnGUI transpiler patched successfully.");

            return codes;
        }

        public static HashSet<IntVec3> GetNetworkInterfacePositions(Map map)
        {
            NetworksMapCache cache = map.GetComponent<NetworksMapComponent>().Cache;
            return cache.NetworkInterfacePositions;
        }

        private static bool TryGetBranchLabel(CodeInstruction instruction, out Label label)
        {
            label = default;

            if ((instruction.opcode == OpCodes.Brfalse || instruction.opcode == OpCodes.Brfalse_S) &&
                instruction.operand is Label branchLabel)
            {
                label = branchLabel;
                return true;
            }

            return false;
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
