using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace SK_Matter_Network.Patches
{
    // A network interface presents its active controller's shared container from
    // GetDirectlyHeldThings() while online (see NetworkBuildingNetworkInterface.GetDirectlyHeldThings),
    // so vanilla's per-haul-source lister recalculation redundantly re-checks every stored stack once
    // per interface, on top of the controller's own pass over the same container. With many
    // interfaces on one network this turns an O(stacks) sweep into O(stacks x interfaces).
    //
    // These patches skip that duplicate enumeration for interfaces only, and only when the
    // controller is already registered as a haul source to cover the same scan itself. Disk drives,
    // chutes, offline/fallback interfaces, and topology transitions where the controller isn't
    // registered yet all fall through to vanilla behavior unchanged.
    public static class Patch_ListerHaulables
    {
        [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.RecalculateAllInHaulSource))]
        public static class RecalculateAllInHaulSource
        {
            public static bool Prefix(IHaulSource source)
            {
                return !IsDuplicateOfControllerPass(source);
            }
        }

        [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.Notify_HaulSourceChanged))]
        public static class Notify_HaulSourceChanged
        {
            public static bool Prefix(IHaulSource holder)
            {
                return !IsDuplicateOfControllerPass(holder);
            }
        }

        // RecalculateAllInHaulSources enumerates each source's GetDirectlyHeldThings() directly in
        // its own body instead of delegating to RecalculateAllInHaulSource, so it can't be skipped
        // with the same kind of prefix as the two patches above without reimplementing the vanilla
        // loop in C#. Instead, transpile the compiled loop in place.
        //
        // Decompiled shape (RimWorld 1.6, Assembly-CSharp.dll, verified via ilspycmd -il):
        //   foreach (IHaulSource source in sources)
        //   {
        //       foreach (Thing item in (IEnumerable<Thing>)source.GetDirectlyHeldThings())
        //           Check(item);
        //   }
        // Right before the call that fetches a source's held-things enumerable, this inserts a test
        // of whether the source is a duplicate interface pass; if so, control jumps straight to the
        // outer loop's condition check (its MoveNext), skipping that source's inner loop entirely
        // instead of enumerating and Check()-ing every item it would report.
        [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.RecalculateAllInHaulSources))]
        public static class RecalculateAllInHaulSources
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

                MethodInfo getDirectlyHeldThingsMethod = AccessTools.Method(typeof(IThingHolder), nameof(IThingHolder.GetDirectlyHeldThings));
                MethodInfo isDuplicateMethod = AccessTools.Method(typeof(Patch_ListerHaulables), nameof(IsDuplicateOfControllerPass));

                if (getDirectlyHeldThingsMethod == null || isDuplicateMethod == null)
                {
                    Logger.Error("Patch_ListerHaulables.RecalculateAllInHaulSources transpiler FAILED: could not resolve a required method via reflection. Leaving vanilla method unpatched; duplicate controller passes will not be skipped here (perf-only, behavior is still correct).");
                    return codes;
                }

                int getDirectlyHeldThingsIdx = codes.FindIndex(ci => ci.Calls(getDirectlyHeldThingsMethod));
                if (getDirectlyHeldThingsIdx < 0)
                {
                    Logger.Error("Patch_ListerHaulables.RecalculateAllInHaulSources transpiler FAILED: could not find IThingHolder.GetDirectlyHeldThings() call. Leaving vanilla method unpatched; duplicate controller passes will not be skipped here (perf-only, behavior is still correct).");
                    return codes;
                }

                int outerLoopHeadBranchIdx = -1;
                for (int i = 0; i < getDirectlyHeldThingsIdx; i++)
                {
                    if (codes[i].opcode == OpCodes.Br || codes[i].opcode == OpCodes.Br_S)
                    {
                        outerLoopHeadBranchIdx = i;
                        break;
                    }
                }

                if (outerLoopHeadBranchIdx < 0)
                {
                    Logger.Error("Patch_ListerHaulables.RecalculateAllInHaulSources transpiler FAILED: could not find the outer foreach's loop-head branch. Leaving vanilla method unpatched; duplicate controller passes will not be skipped here (perf-only, behavior is still correct).");
                    return codes;
                }

                object outerLoopHeadLabel = codes[outerLoopHeadBranchIdx].operand;
                Label continueLabel = generator.DefineLabel();

                // Move any label already targeting the original GetDirectlyHeldThings-call
                // instruction onto our first inserted instruction, so anything that used to jump
                // straight into this point in the loop still lands before the new check runs.
                CodeInstruction dupSource = new CodeInstruction(OpCodes.Dup);
                dupSource.labels.AddRange(codes[getDirectlyHeldThingsIdx].labels);
                codes[getDirectlyHeldThingsIdx].labels.Clear();
                codes[getDirectlyHeldThingsIdx].labels.Add(continueLabel);

                List<CodeInstruction> toInsert = new List<CodeInstruction>
                {
                    dupSource,                                                    // [source, source]
                    new CodeInstruction(OpCodes.Call, isDuplicateMethod),          // [source, isDuplicate]
                    new CodeInstruction(OpCodes.Brfalse_S, continueLabel),         // [source] -> continue if not a duplicate
                    new CodeInstruction(OpCodes.Pop),                              // [] -> drop the leftover source
                    new CodeInstruction(OpCodes.Br, outerLoopHeadLabel),           // jump to the outer loop's MoveNext check
                };

                codes.InsertRange(getDirectlyHeldThingsIdx, toInsert);

                Logger.Message("Patch_ListerHaulables.RecalculateAllInHaulSources transpiler SUCCEEDED: duplicate controller passes will be skipped.");
                return codes;
            }
        }

        // True only when: source is a network interface, it is currently presenting its active
        // controller's shared container (i.e. online and routed through that controller), the
        // controller is spawned on the same map, and the controller is itself registered as a haul
        // source - so the controller's own pass already covers every item this interface would
        // report.
        internal static bool IsDuplicateOfControllerPass(IHaulSource source)
        {
            if (!(source is NetworkBuildingNetworkInterface iface) || !iface.Spawned)
            {
                return false;
            }

            NetworkBuildingController controller = iface.ParentNetwork?.ActiveController;
            if (controller == null || !controller.Spawned || controller.Map != iface.Map)
            {
                return false;
            }

            if (!ReferenceEquals(iface.GetDirectlyHeldThings(), controller.innerContainer))
            {
                return false;
            }

            return iface.Map.haulDestinationManager.AllHaulSourcesListForReading.Contains(controller);
        }
    }
}
