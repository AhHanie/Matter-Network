using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace SK_Matter_Network.Patches
{
    [HarmonyPatch(typeof(Map), nameof(Map.ExposeData))]
    public static class Patch_Map
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo allThingsGetter = AccessTools.PropertyGetter(typeof(ListerThings), nameof(ListerThings.AllThings));
            MethodInfo shouldSkipSavingThingMethod = AccessTools.Method(typeof(Patch_Map), nameof(ShouldSkipSavingThing));

            int allThingsGetterIndex = -1;
            int currentThingStoreIndex = -1;
            int loopContinueIndex = -1;

            for (int i = 0; i < codes.Count; i++)
            {
                if (allThingsGetterIndex == -1 && codes[i].Calls(allThingsGetter))
                {
                    allThingsGetterIndex = i;
                    continue;
                }

                if (allThingsGetterIndex != -1 &&
                    currentThingStoreIndex == -1 &&
                    i > 0 &&
                    IsThingEnumeratorCurrentGetter(codes[i - 1]) &&
                    IsStlocInstruction(codes[i], 3))
                {
                    currentThingStoreIndex = i;
                    continue;
                }

                if (currentThingStoreIndex != -1 &&
                    i > 0 &&
                    IsThingEnumeratorMoveNext(codes[i]))
                {
                    loopContinueIndex = i - 1;
                    break;
                }
            }

            if (allThingsGetterIndex == -1 || currentThingStoreIndex == -1 || loopContinueIndex == -1)
            {
                Logger.Error("Map.ExposeData transpiler failed: could not find the network-save skip insertion point.");
                return codes;
            }

            int insertIndex = currentThingStoreIndex + 1;
            Label continueSavingLabel = generator.DefineLabel();
            Label loopContinueLabel = generator.DefineLabel();

            codes[insertIndex].labels.Add(continueSavingLabel);
            codes[loopContinueIndex].labels.Add(loopContinueLabel);

            List<CodeInstruction> injected = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Call, shouldSkipSavingThingMethod),
                    new CodeInstruction(OpCodes.Brfalse, continueSavingLabel),
                    new CodeInstruction(OpCodes.Leave, loopContinueLabel)
            };

            injected[0].blocks.AddRange(codes[insertIndex].blocks);
            codes[insertIndex].blocks.Clear();

            codes.InsertRange(insertIndex, injected);
            Logger.Message("Map.ExposeData transpiler patched successfully to skip saving network-backed things.");

            return codes;
        }

        public static bool ShouldSkipSavingThing(Map map, Thing thing)
        {
            if (map == null || thing == null)
            {
                return false;
            }

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp == null)
            {
                return false;
            }

            return mapComp.TryGetItemNetwork(thing, out _);
        }

        private static bool IsThingEnumeratorCurrentGetter(CodeInstruction instruction)
        {
            if (!(instruction.operand is MethodInfo method))
            {
                return false;
            }

            return method.Name == "get_Current" &&
                   method.DeclaringType != null &&
                   method.DeclaringType.FullName != null &&
                   method.DeclaringType.FullName.Contains("List`1+Enumerator") &&
                   method.DeclaringType.FullName.Contains("Verse.Thing");
        }

        private static bool IsThingEnumeratorMoveNext(CodeInstruction instruction)
        {
            if (!(instruction.operand is MethodInfo method))
            {
                return false;
            }

            return method.Name == nameof(System.Collections.IEnumerator.MoveNext) &&
                   method.DeclaringType != null &&
                   method.DeclaringType.FullName != null &&
                   method.DeclaringType.FullName.Contains("List`1+Enumerator") &&
                   method.DeclaringType.FullName.Contains("Verse.Thing");
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
