using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class VanillaRacesExpandedAndroidCompat
    {
        private const string PackageId = "vanillaracesexpanded.android";

        // ── Reflected VRE types ─────────────────────────────────────────────────
        private static readonly Type _androidCreationUtilityType =
            AccessTools.TypeByName("VREAndroids.AndroidCreationUtility");
        private static readonly Type _workGiverCreateAndroidType =
            AccessTools.TypeByName("VREAndroids.WorkGiver_CreateAndroid");
        private static readonly Type _jobDriverCreateAndroidType =
            AccessTools.TypeByName("VREAndroids.JobDriver_CreateAndroid");
        private static readonly Type _buildingAndroidCreationStationType =
            AccessTools.TypeByName("VREAndroids.Building_AndroidCreationStation");

        // ── Reflected AndroidCreationUtility members ───────────────────────────
        private static readonly MethodInfo _tryFindBestIngredientsHelperMethod =
            AccessTools.Method(_androidCreationUtilityType, "TryFindBestIngredientsHelper");
        private static readonly FieldInfo _relevantThingsField =
            AccessTools.Field(_androidCreationUtilityType, "relevantThings");
        private static readonly FieldInfo _processedThingsField =
            AccessTools.Field(_androidCreationUtilityType, "processedThings");
        private static readonly FieldInfo _newRelevantThingsField =
            AccessTools.Field(_androidCreationUtilityType, "newRelevantThings");

        // ── Reflected Building_AndroidCreationStation members ──────────────────
        private static readonly FieldInfo _unfinishedAndroidField =
            AccessTools.Field(_buildingAndroidCreationStationType, "unfinishedAndroid");

        // ── Availability check (evaluated once at class load) ──────────────────
        private static readonly bool _isFullyAvailable = CheckAvailability();

        private static bool CheckAvailability()
        {
            if (!ModsConfig.IsActive(PackageId))
                return false;

            if (_androidCreationUtilityType == null ||
                _workGiverCreateAndroidType == null ||
                _jobDriverCreateAndroidType == null ||
                _tryFindBestIngredientsHelperMethod == null ||
                _relevantThingsField == null ||
                _processedThingsField == null ||
                _newRelevantThingsField == null ||
                _buildingAndroidCreationStationType == null ||
                _unfinishedAndroidField == null)
            {
                Log.Warning("[Matter Network] VRE Androids compatibility disabled because expected VREAndroids members were not found.");
                return false;
            }
            return true;
        }

        // ── PATCH 1: Inject network ingredients into VRE ingredient search ─────
        //
        // Transpiles AndroidCreationUtility.TryFindBestIngredientsHelper.
        // Inserts immediately after processedThings.AddRange(relevantThings) — the
        // point where VRE has cleared and re-initialised all three static lists but
        // not yet started region traversal.  If every required ingredient can be
        // satisfied by the Matter Network, the helper returns true early; otherwise
        // the network candidates stay in relevantThings so VRE's region traversal
        // can mix them with physical items.
        [HarmonyPatch]
        public static class Patch_TryFindBestIngredientsHelper
        {
            [HarmonyPrepare]
            public static bool Prepare() => _isFullyAvailable;

            public static MethodBase TargetMethod() => _tryFindBestIngredientsHelperMethod;

            public static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions,
                ILGenerator generator)
            {
                var codes = new List<CodeInstruction>(instructions);

                var tryAddMethod = AccessTools.Method(
                    typeof(VanillaRacesExpandedAndroidCompat),
                    nameof(TryAddAndChooseNetworkIngredients));

                if (tryAddMethod == null)
                {
                    Logger.Error("[Matter Network] VRE Android transpiler: could not resolve TryAddAndChooseNetworkIngredients.");
                    return codes;
                }

                // Match:  ldsfld processedThings
                //         ldsfld relevantThings
                //         call   GenCollection::AddRange<Thing>
                //
                // Use name-based comparison instead of FieldInfo/MethodInfo reference
                // equality: Mono can return different wrapper instances for the same
                // member, so == would fail even when the field is identical.
                string utilTypeName = _androidCreationUtilityType.FullName;
                int insertAfterIdx = -1;
                for (int i = 0; i + 2 < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Ldsfld &&
                        codes[i].operand is FieldInfo f0 &&
                        f0.Name == "processedThings" &&
                        f0.DeclaringType?.FullName == utilTypeName &&
                        codes[i + 1].opcode == OpCodes.Ldsfld &&
                        codes[i + 1].operand is FieldInfo f1 &&
                        f1.Name == "relevantThings" &&
                        f1.DeclaringType?.FullName == utilTypeName &&
                        (codes[i + 2].opcode == OpCodes.Call || codes[i + 2].opcode == OpCodes.Callvirt) &&
                        codes[i + 2].operand is MethodInfo mi &&
                        mi.Name == "AddRange")
                    {
                        insertAfterIdx = i + 2;
                        break;
                    }
                }

                if (insertAfterIdx == -1)
                {
                    Logger.Error("[Matter Network] VRE Android transpiler: could not find processedThings.AddRange(relevantThings) in TryFindBestIngredientsHelper.");
                    return codes;
                }

                if (insertAfterIdx + 1 >= codes.Count)
                {
                    Logger.Error("[Matter Network] VRE Android transpiler: no instructions follow the insertion point.");
                    return codes;
                }

                // Label attached to the first original instruction after AddRange.
                // Brfalse jumps here to continue VRE's normal region traversal.
                var skipLabel = generator.DefineLabel();
                codes[insertAfterIdx + 1].labels.Add(skipLabel);

                // Injected IL — pushed immediately after AddRange:
                //   TryAddAndChooseNetworkIngredients(pawn, billGiver, searchRadius,
                //       relevantThings, processedThings, newRelevantThings,
                //       thingValidator, foundAllIngredientsAndChoose)
                //   brfalse  skipLabel
                //   ldc.i4.1
                //   ret
                var toInsert = new List<CodeInstruction>
                {
                    // TryFindBestIngredientsHelper is static, so arg 0 = thingValidator.
                    new CodeInstruction(OpCodes.Ldarg_3),           // pawn          (arg 3)
                    new CodeInstruction(OpCodes.Ldarg_S, (byte)4),  // billGiver     (arg 4)
                    new CodeInstruction(OpCodes.Ldarg_S, (byte)6),  // searchRadius  (arg 6)
                    new CodeInstruction(OpCodes.Ldsfld, _relevantThingsField),
                    new CodeInstruction(OpCodes.Ldsfld, _processedThingsField),
                    new CodeInstruction(OpCodes.Ldsfld, _newRelevantThingsField),
                    new CodeInstruction(OpCodes.Ldarg_0),           // thingValidator              (arg 0)
                    new CodeInstruction(OpCodes.Ldarg_1),           // foundAllIngredientsAndChoose (arg 1)
                    new CodeInstruction(OpCodes.Call, tryAddMethod),
                    new CodeInstruction(OpCodes.Brfalse, skipLabel),
                    new CodeInstruction(OpCodes.Ldc_I4_1),
                    new CodeInstruction(OpCodes.Ret),
                };

                codes.InsertRange(insertAfterIdx + 1, toInsert);
                Logger.Message("[Matter Network] VRE Androids transpiler patched TryFindBestIngredientsHelper successfully.");
                return codes;
            }
        }

        // Called from the transpiler.  Adds extractable Matter Network items to
        // VRE's relevantThings/processedThings, then asks VRE's own selector
        // whether the collected set satisfies all ingredients.
        //
        // Returns true  → all ingredients found in network; caller clears the
        //                 three VRE static lists and returns true immediately.
        // Returns false → could not satisfy all from network alone; network
        //                 candidates stay in the lists so VRE's region traversal
        //                 can supplement them with physical items.
        private static bool TryAddAndChooseNetworkIngredients(
            Pawn pawn,
            Thing billGiver,
            float searchRadius,
            List<Thing> relevantThings,
            HashSet<Thing> processedThings,
            List<Thing> newRelevantThings,
            Predicate<Thing> thingValidator,
            Predicate<List<Thing>> foundAllIngredientsAndChoose)
        {
            int countBefore = relevantThings.Count;
            NetworkBillIngredientUtility.AddNetworkIngredientCandidates(
                pawn, billGiver, searchRadius, relevantThings, processedThings, thingValidator);

            if (relevantThings.Count == countBefore)
                return false;   // no extractable network candidates

            if (!foundAllIngredientsAndChoose(relevantThings))
                return false;   // network alone insufficient; keep for mixed resolution

            // Network-only success: mirror VRE's end-of-method cleanup before the early ret.
            relevantThings.Clear();
            newRelevantThings.Clear();
            processedThings.Clear();
            return true;
        }

        // ── PATCH 2: Reservation handling for VREA_CreateAndroid job ──────────
        //
        // VRE's original TryMakePreToilReservations calls ReserveAsManyAsPossible
        // with default maxPawns=1, which is too narrow for shared network stacks,
        // and then adds a physicalInteractionReservation for every ingredient
        // including network items.  This prefix takes over when the job contains
        // at least one network item, reserving physical items normally and network
        // items with the shared maxPawns cap and the chosen stack count.
        [HarmonyPatch]
        public static class Patch_TryMakePreToilReservations
        {
            [HarmonyPrepare]
            public static bool Prepare() => _isFullyAvailable;

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_jobDriverCreateAndroidType, nameof(JobDriver.TryMakePreToilReservations));

            public static bool Prefix(object __instance, bool errorOnFailed, ref bool __result)
            {
                JobDriver driver = __instance as JobDriver;
                if (driver == null) return true;

                Pawn pawn = driver.pawn;
                Job job = driver.job;
                List<LocalTargetInfo> targetQueueB = job.GetTargetQueue(TargetIndex.B);

                if (targetQueueB == null ||
                    !NetworkBillIngredientUtility.ContainsNetworkIngredient(pawn, targetQueueB))
                    return true;    // no network ingredients — let VRE handle

                // Reserve the android creation station.
                if (!pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed))
                {
                    __result = false;
                    return false;
                }

                // Reserve ingredient targets: physical items with maxPawns=1,
                // network items with SharedIngredientReservationMaxPawns and
                // the exact chosen count from job.countQueue.
                NetworkBillIngredientUtility.ReserveIngredientQueue(pawn, job, targetQueueB);

                // Physical interaction reservations for spawned (non-network) items only.
                NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
                foreach (LocalTargetInfo target in targetQueueB)
                {
                    if (!target.HasThing) continue;
                    if (mapComp.TryGetItemNetwork(target.Thing, out _)) continue;
                    pawn.Map.physicalInteractionReservationManager.Reserve(pawn, job, target.Thing);
                }

                // Reserve the unfinished android if one already exists at the station.
                Thing stationThing = job.GetTarget(TargetIndex.A).Thing;
                if (stationThing != null)
                {
                    Thing unfinishedAndroid = _unfinishedAndroidField.GetValue(stationThing) as Thing;
                    if (unfinishedAndroid != null &&
                        !pawn.Reserve(unfinishedAndroid, job, 1, -1, null, errorOnFailed))
                    {
                        __result = false;
                        return false;
                    }
                }

                __result = true;
                return false;
            }
        }
    }
}
