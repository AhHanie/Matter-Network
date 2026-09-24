using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    // Soft compat: lets Equipment Manager's automatic loadout assignment (melee/ranged/tool
    // rules) and its "currently available" rule dialogs see weapons and tools stored
    // (unspawned) in an operational Matter Network. No compile-time reference to
    // LordKuper.EquipmentManager.dll; everything internal to that mod below is resolved via
    // reflection, and every patch in this file is skipped entirely unless the mod is active.
    //
    // MeleeWeaponRule/RangedWeaponRule/ToolRule.GetCurrentlyAvailableItems each build their
    // candidate list from a single ListerThings.ThingsInGroup(ThingRequestGroup.Weapon) call
    // (reached through a null-conditional chain off a possibly-null map). Network-stored items
    // never reach that map lister, so a transpiler splices a network-augmented call in at that
    // exact call site in each of the three rule methods; everything downstream (each rule's own
    // IsAvailable filter, whitelist/blacklist, scoring, sorted UI, dialogs) runs unmodified
    // since it only ever sees an ordinary List<Thing>.
    //
    // A network item that isn't actually retrievable right now (network unpowered/offline, no
    // reachable interface for this pawn, forbidden, or already reserved) shouldn't be able to
    // win an assignment over a usable alternative. Every assignment method in
    // EquipmentManagerMapComponent already funnels its candidates through vanilla
    // RimWorld.EquipmentUtility.CanEquip(Thing, Pawn) before scoring, so this patches that
    // method directly instead of scoping a flag around Equipment Manager's own update pass
    // (which would miss its rule-dialog previews). Nothing in CanEquip's vanilla logic checks
    // location or reachability today, so the postfix only ever turns a true into false, and
    // only for a Thing that resolves to a Matter Network item; every other caller keeps its
    // existing behavior unchanged.
    //
    // Combat Extended ammunition pickup (UpdateAmmo, private on EquipmentManagerMapComponent)
    // gathers candidate stacks via ListerThings.ThingsOfDef(ThingDef) inside a LINQ selector, so
    // the three rule transpilers above don't reach it, and UpdateAmmo is itself a no-op unless
    // CE's ammo system is enabled. Rather than transpiling UpdateAmmo directly (its ThingsOfDef
    // call lives inside a compiler-generated closure whose generated name isn't a stable patch
    // target), a Prefix/Finalizer pair around UpdateAmmo scopes thread-static state (map, pawn,
    // an active flag) for its entire synchronous call graph, and a guarded postfix on
    // ListerThings.ThingsOfDef appends matching, pawn-reachable network stacks only while that
    // flag is set - inert whenever UpdateAmmo's own early-out skips the call entirely.
    public static class EquipmentManagerCompat
    {
        private const string PackageId = "LordKuper.EquipmentManager";

        private static readonly Type MeleeWeaponRuleType = AccessTools.TypeByName("LordKuper.EquipmentManager.MeleeWeaponRule");
        private static readonly Type RangedWeaponRuleType = AccessTools.TypeByName("LordKuper.EquipmentManager.RangedWeaponRule");
        private static readonly Type ToolRuleType = AccessTools.TypeByName("LordKuper.EquipmentManager.ToolRule");
        private static readonly Type EquipmentManagerMapComponentType = AccessTools.TypeByName("LordKuper.EquipmentManager.EquipmentManagerMapComponent");
        private static readonly Type PawnCacheType = AccessTools.TypeByName("LordKuper.EquipmentManager.PawnCache");

        // Equipment Manager hard-depends on a separate "LordKuper.Common" mod for types like
        // RimWorldTime and StatLimit that show up in its own public method/field signatures
        // (ItemRule.StatLimits, GetCurrentlyAvailableItems(Map, RimWorldTime), etc.). Looking a
        // type up *by name* (AccessTools.TypeByName, used above) never forces it to fully
        // resolve, but reflecting into one of its *members* (AccessTools.Method/Property below)
        // does - and if LordKuper.Common isn't installed/loaded, that throws a TypeLoadException
        // for those member types. Since that would happen inside this class's own static field
        // initializers, it would permanently poison EquipmentManagerCompat for the rest of the
        // session (TypeInitializationException, cached by the CLR) and make every patch in this
        // file fail loudly through Harmony instead of just declining to activate. Probe a
        // LordKuper.Common type by name first, as a canary, before resolving anything that
        // touches Equipment Manager's own member signatures.
        private static readonly bool CommonDependencyResolvable =
            AccessTools.TypeByName("LordKuper.Common.Cache.TimedCache") != null;

        private static readonly MethodInfo MeleeGetCurrentlyAvailableItemsMethod =
            CommonDependencyResolvable && MeleeWeaponRuleType != null
                ? AccessTools.Method(MeleeWeaponRuleType, "GetCurrentlyAvailableItems") : null;
        private static readonly MethodInfo RangedGetCurrentlyAvailableItemsMethod =
            CommonDependencyResolvable && RangedWeaponRuleType != null
                ? AccessTools.Method(RangedWeaponRuleType, "GetCurrentlyAvailableItems") : null;
        private static readonly MethodInfo ToolGetCurrentlyAvailableItemsMethod =
            CommonDependencyResolvable && ToolRuleType != null
                ? AccessTools.Method(ToolRuleType, "GetCurrentlyAvailableItems") : null;

        private static readonly MethodInfo UpdateAmmoMethod =
            CommonDependencyResolvable && EquipmentManagerMapComponentType != null &&
                RangedWeaponRuleType != null && PawnCacheType != null
                ? AccessTools.Method(EquipmentManagerMapComponentType, "UpdateAmmo", new[] { PawnCacheType, typeof(Thing), RangedWeaponRuleType })
                : null;
        private static readonly PropertyInfo PawnCachePawnProperty =
            CommonDependencyResolvable && PawnCacheType != null
                ? AccessTools.Property(PawnCacheType, "Pawn") : null;

        private static readonly MethodInfo ThingsInGroupMethod =
            AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsInGroup));
        private static readonly MethodInfo NetworkAugmentedThingsInGroupMethod =
            AccessTools.Method(typeof(EquipmentManagerCompat), nameof(NetworkAugmentedThingsInGroup));

        private static readonly MethodInfo CanEquipMethod = AccessTools.Method(
            typeof(EquipmentUtility), nameof(EquipmentUtility.CanEquip),
            new[] { typeof(Thing), typeof(Pawn), typeof(string).MakeByRefType(), typeof(bool) });

        [ThreadStatic] private static bool _ammoUpdateActive;
        [ThreadStatic] private static Map _ammoUpdateMap;
        [ThreadStatic] private static Pawn _ammoUpdatePawn;

        private static bool _warnedMissingRuleApi;
        private static bool _warnedMissingAmmoApi;

        private static bool IsAvailable()
        {
            return ModsConfig.IsActive(PackageId)
                && MeleeWeaponRuleType != null && RangedWeaponRuleType != null && ToolRuleType != null
                && MeleeGetCurrentlyAvailableItemsMethod != null && RangedGetCurrentlyAvailableItemsMethod != null
                && ToolGetCurrentlyAvailableItemsMethod != null
                && ThingsInGroupMethod != null && NetworkAugmentedThingsInGroupMethod != null
                && CanEquipMethod != null;
        }

        private static bool WarnOnceIfMissingRuleApi()
        {
            bool available = IsAvailable();
            if (ModsConfig.IsActive(PackageId) && !available && !_warnedMissingRuleApi)
            {
                _warnedMissingRuleApi = true;
                Logger.Warning(CommonDependencyResolvable
                    ? "Equipment Manager compat: expected rule/CanEquip API not found. Network-stored weapons and tools will not be offered to Equipment Manager's loadout assignment."
                    : "Equipment Manager compat: Equipment Manager is active but its required 'LordKuper.Common' mod does not appear to be installed/loaded. Network-stored weapons and tools will not be offered to Equipment Manager's loadout assignment until that dependency is fixed.");
            }
            return available;
        }

        private static bool AmmoAugmentAvailable()
        {
            bool available = ModsConfig.IsActive(PackageId) && UpdateAmmoMethod != null;
            if (ModsConfig.IsActive(PackageId) && !available && !_warnedMissingAmmoApi)
            {
                _warnedMissingAmmoApi = true;
                Logger.Warning(CommonDependencyResolvable
                    ? "Equipment Manager compat: expected UpdateAmmo API not found. Combat Extended ammo pickup will not see network-stored ammo."
                    : "Equipment Manager compat: Equipment Manager is active but its required 'LordKuper.Common' mod does not appear to be installed/loaded. Combat Extended ammo pickup will not see network-stored ammo until that dependency is fixed.");
            }
            return available;
        }

        // Replaces map?.listerThings?.ThingsInGroup(ThingRequestGroup.Weapon) inside each rule's
        // GetCurrentlyAvailableItems. Extends the vanilla result with matching, extraction-eligible
        // network-stored items so each rule's own IsAvailable/whitelist/blacklist/limit filtering,
        // scoring, and sorted UI can consider them. Never touches map.listerThings itself.
        private static List<Thing> NetworkAugmentedThingsInGroup(ListerThings lister, ThingRequestGroup group, Map map)
        {
            List<Thing> baseList = lister.ThingsInGroup(group);
            if (map == null)
            {
                return baseList;
            }

            List<Thing> combined = null;
            foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                if (item == null || item.Destroyed || !(item is ThingWithComps) || !group.Includes(item.def))
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

        private static IEnumerable<CodeInstruction> SpliceThingsInGroupCall(IEnumerable<CodeInstruction> instructions, string methodLabel)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            bool patched = false;

            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].Calls(ThingsInGroupMethod))
                {
                    continue;
                }

                // GetCurrentlyAvailableItems(Map map, ...) - map is always the first declared
                // parameter (arg 1) on all three rule types. Mutate the existing call instruction
                // in place (rather than replacing it outright) so any labels/exception blocks
                // attached to it survive; the inserted ldarg.1 is new and carries none of its own.
                list.Insert(i, new CodeInstruction(OpCodes.Ldarg_1));
                CodeInstruction call = list[i + 1];
                call.opcode = OpCodes.Call;
                call.operand = NetworkAugmentedThingsInGroupMethod;

                patched = true;
                break;
            }

            if (!patched)
            {
                Logger.Warning($"[Equipment Manager Compat] transpiler: ListerThings.ThingsInGroup call not found in {methodLabel}; network-stored items will not be offered for that rule type.");
            }

            return list;
        }

        [HarmonyPatch]
        public static class Patch_MeleeWeaponRule_GetCurrentlyAvailableItems
        {
            [HarmonyPrepare]
            public static bool Prepare() => WarnOnceIfMissingRuleApi();

            public static MethodBase TargetMethod() => MeleeGetCurrentlyAvailableItemsMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
                SpliceThingsInGroupCall(instructions, "MeleeWeaponRule.GetCurrentlyAvailableItems");
        }

        [HarmonyPatch]
        public static class Patch_RangedWeaponRule_GetCurrentlyAvailableItems
        {
            [HarmonyPrepare]
            public static bool Prepare() => WarnOnceIfMissingRuleApi();

            public static MethodBase TargetMethod() => RangedGetCurrentlyAvailableItemsMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
                SpliceThingsInGroupCall(instructions, "RangedWeaponRule.GetCurrentlyAvailableItems");
        }

        [HarmonyPatch]
        public static class Patch_ToolRule_GetCurrentlyAvailableItems
        {
            [HarmonyPrepare]
            public static bool Prepare() => WarnOnceIfMissingRuleApi();

            public static MethodBase TargetMethod() => ToolGetCurrentlyAvailableItemsMethod;

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
                SpliceThingsInGroupCall(instructions, "ToolRule.GetCurrentlyAvailableItems");
        }

        // A Matter Network item is only "equippable" if it's actually retrievable. Every
        // Equipment Manager assignment method (primary/sidearm/tool, all four rule types)
        // already filters its candidates through this exact vanilla call before scoring, so
        // rejecting an inaccessible network item here - rather than only at the final
        // TryTakeOrderedJob - keeps a reachable, lower-scoring alternative from being displaced
        // by one the pawn can't actually reach.
        [HarmonyPatch]
        public static class Patch_CanEquip
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() => CanEquipMethod;

            public static void Postfix(Thing thing, Pawn pawn, ref string cantReason, ref bool __result)
            {
                if (!__result || thing == null || pawn?.Map == null)
                {
                    return;
                }

                if (!NetworkItemSearchUtility.TryGetPawnMapNetwork(pawn, thing, out DataNetwork network))
                {
                    return;
                }

                if (thing.IsForbidden(pawn))
                {
                    __result = false;
                    cantReason = "MN_CantEquipForbiddenNetworkItem".Translate();
                    return;
                }

                if (!network.CanExtractItems ||
                    NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn, thing) == float.MaxValue)
                {
                    __result = false;
                    cantReason = "MN_CantEquipUnreachableNetworkItem".Translate();
                    return;
                }

                if (!pawn.CanReserve(thing))
                {
                    __result = false;
                    cantReason = "MN_CantEquipReservedNetworkItem".Translate();
                }
            }
        }

        [HarmonyPatch]
        public static class Patch_UpdateAmmo
        {
            [HarmonyPrepare]
            public static bool Prepare() => AmmoAugmentAvailable();

            public static MethodBase TargetMethod() => UpdateAmmoMethod;

            public static void Prefix(object __instance, object pawn)
            {
                Map map = (__instance as MapComponent)?.map;
                _ammoUpdateMap = map;
                _ammoUpdateActive = map != null;
                _ammoUpdatePawn = pawn != null && PawnCachePawnProperty != null
                    ? PawnCachePawnProperty.GetValue(pawn) as Pawn
                    : null;
            }

            // Cleanup only - passes the original exception (if any) through unchanged rather
            // than swallowing it.
            public static Exception Finalizer(Exception __exception)
            {
                _ammoUpdateActive = false;
                _ammoUpdateMap = null;
                _ammoUpdatePawn = null;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(ListerThings), nameof(ListerThings.ThingsOfDef), new[] { typeof(ThingDef) })]
        public static class Patch_ThingsOfDef_AmmoAugment
        {
            [HarmonyPrepare]
            public static bool Prepare() => AmmoAugmentAvailable();

            public static void Postfix(ThingDef def, ref List<Thing> __result)
            {
                if (!_ammoUpdateActive)
                {
                    return;
                }

                Map map = _ammoUpdateMap;
                if (map == null)
                {
                    return;
                }

                Pawn pawn = _ammoUpdatePawn;
                List<Thing> combined = null;
                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (item == null || item.Destroyed || item.def != def || !(item is ThingWithComps))
                    {
                        continue;
                    }

                    if (pawn != null &&
                        NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn, item) == float.MaxValue)
                    {
                        continue;
                    }

                    if (combined == null)
                    {
                        combined = new List<Thing>(__result);
                    }

                    if (!combined.Contains(item))
                    {
                        combined.Add(item);
                    }
                }

                if (combined != null)
                {
                    __result = combined;
                }
            }
        }
    }
}
