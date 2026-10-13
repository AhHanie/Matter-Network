using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace SK_Matter_Network.Patches
{
    // Soft compat: lets Simple Sidearms' automatic re-equip job giver discover a
    // remembered weapon that's stored (unspawned) in a Matter Network instead of
    // sitting on the map. No compile-time reference to SimpleSidearms.dll; everything
    // below is resolved via reflection and skipped entirely if the mod isn't loaded.
    //
    // SimpleSidearms.rimworld.JobGiver_RetrieveWeapon.TryGiveJobStatic builds its
    // candidate list with pawn.Map.listerThings.ThingsOfDef(weaponMemory.thing), which
    // only ever contains spawned things. Network-stored weapons never reach that list,
    // so they're invisible to everything downstream - the stuff/bladelink/biocode LINQ
    // filters and GenClosest.ClosestThing_Global_Reachable, whose Matter Network postfix
    // (Patch_GenClosest.ClosestThing_Global_Reachable) already ranks any network item
    // that's present in the *searchSet it's handed* against reachable interfaces. A
    // transpiler splices a network-augmented list in at that one call site so everything
    // downstream - filtering, ranking, and the eventual pickup via JobDriver_EquipSidearm
    // removing the item from its holdingOwner - runs unmodified.
    public static class SimpleSidearmsCompat
    {
        private const string PackageId = "PeteTimesSix.SimpleSidearms";

        private static readonly System.Type JobGiverRetrieveWeaponType =
            AccessTools.TypeByName("SimpleSidearms.rimworld.JobGiver_RetrieveWeapon");

        private static readonly MethodInfo TryGiveJobStaticMethod =
            JobGiverRetrieveWeaponType != null
                ? AccessTools.Method(JobGiverRetrieveWeaponType, "TryGiveJobStatic", new[] { typeof(Pawn), typeof(bool) })
                : null;

        private static readonly MethodInfo ThingsOfDefMethod =
            AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsOfDef), new[] { typeof(ThingDef) });

        private static readonly MethodInfo NetworkAugmentedThingsOfDefMethod =
            AccessTools.Method(typeof(SimpleSidearmsCompat), nameof(NetworkAugmentedThingsOfDef));

        private static bool IsAvailable() =>
            ModsConfig.IsActive(PackageId)
            && JobGiverRetrieveWeaponType != null
            && TryGiveJobStaticMethod != null
            && ThingsOfDefMethod != null
            && NetworkAugmentedThingsOfDefMethod != null;

        // Replaces pawn.Map.listerThings.ThingsOfDef(weaponMemory.thing). Extends the
        // vanilla result with matching, extraction-eligible network-stored weapons so
        // Simple Sidearms' own stuff/bladelink/biocode filters and its GenClosest call
        // (which Matter Network's own postfix already ranks network items within) can
        // consider them. Never touches map.listerThings itself.
        private static List<Thing> NetworkAugmentedThingsOfDef(ListerThings lister, ThingDef def, Pawn pawn)
        {
            List<Thing> baseList = lister.ThingsOfDef(def);
            if (pawn?.Map == null)
            {
                return baseList;
            }

            List<Thing> combined = null;
            foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(pawn.Map))
            {
                if (item == null || item.Destroyed || item.def != def || !(item is ThingWithComps))
                {
                    continue;
                }

                if (combined == null)
                {
                    combined = new List<Thing>(baseList);
                }

                if (!combined.Contains(item))
                {
                    combined.Add(item);
                }
            }

            return combined ?? baseList;
        }

        [HarmonyPatch]
        public static class Patch_TryGiveJobStatic
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() => TryGiveJobStaticMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> list = new List<CodeInstruction>(instructions);
                bool patched = false;

                for (int i = 0; i < list.Count; i++)
                {
                    if (!list[i].Calls(ThingsOfDefMethod))
                    {
                        continue;
                    }

                    // TryGiveJobStatic(Pawn pawn, bool inCombat) - arg 0 is the pawn.
                    // Mutate the existing instruction in place (rather than replacing it
                    // outright) so any labels/exception blocks attached to it survive;
                    // the inserted ldarg.0 is new and carries none of its own.
                    list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    CodeInstruction call = list[i + 1];
                    call.opcode = OpCodes.Call;
                    call.operand = NetworkAugmentedThingsOfDefMethod;

                    patched = true;
                    break;
                }

                if (!patched)
                {
                    Logger.Warning("[Matter Network] Simple Sidearms transpiler: ListerThings.ThingsOfDef call not found in TryGiveJobStatic; network-stored sidearms will not be found for automatic re-equip.");
                }

                return list;
            }
        }
    }
}
