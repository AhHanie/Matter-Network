using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SK_Matter_Network.Patches
{
    // Soft compat: lets RimDark 40k - Mankind's Finest's gene manipulation table
    // (BEWH_GeneManipulationTable) draw its primarch gene-seed vial and human embryo
    // from a Matter Network when crafting a Primarch embryo through the table's
    // custom "Craft Primarch Embryo" dialog.
    //
    // Scope note: the table's ORDINARY bills (genetic matrices, materials, etc.)
    // already work through Matter Network's generic WorkGiver_DoBill /
    // JobDriver_DoBill compatibility - see Patch_WorkGiver_DoBill and
    // Patch_JobDriver_DoBill. Do NOT add a second general bill-ingredient hook here
    // just because this file also touches the gene table; this file exists solely
    // to widen the two hand-rolled ListerThings.ThingsOfDef pickers inside
    // Dialog_CraftPrimarchEmbryo, which bypass the normal bill-ingredient search
    // entirely and only ever look at spawned map things.
    //
    // Deliberately prefix/postfix only, no transpiler: rather than assuming anything
    // about DoWindowContents' internal IL shape, we bracket the call with a
    // Prefix/Postfix pair that records "we're currently inside this dialog, for this
    // map" and then postfix vanilla ListerThings.ThingsOfDef to widen its result only
    // while that flag is set and only for the two defs this dialog picks from. This
    // survives RimDark UI rewrites (new Rects, reordered controls, added buttons)
    // that would silently break an IL-offset-based transpiler match.
    //
    // No compile-time reference to Genes40k.dll/Core40k.dll: everything RimDark-side
    // is resolved via reflection and skipped entirely if the mod isn't loaded.
    public static class RimDarkMankindsFinestCompat
    {
        private const string PackageId = "Phonicmas.RimDark.MankindsFinest";
        private const string PrimarchVialDefName = "BEWH_GeneseedVialPrimarch";

        private static readonly Type dialogType =
            AccessTools.TypeByName("Genes40k.Dialog_CraftPrimarchEmbryo");
        private static readonly MethodInfo doWindowContentsMethod =
            AccessTools.Method(dialogType, "DoWindowContents", new[] { typeof(Rect) });
        private static readonly FieldInfo dialogMapField =
            AccessTools.Field(dialogType, "map");

        // Resolved lazily (first touched from Prepare(), i.e. during Harmony's
        // PatchAll after defs are loaded) - not a reflection lookup, just a normal
        // def-database query, same as other compat files in this project.
        private static readonly ThingDef primarchVialDef =
            DefDatabase<ThingDef>.GetNamedSilentFail(PrimarchVialDefName);

        private static bool warnedOnce;

        // Set/cleared around the dialog's DoWindowContents call; read by the
        // ThingsOfDef postfix below to scope its effect to just this dialog.
        private static Map activeDialogMap;
        private static int activeDialogDepth;

        private static bool IsAvailable()
        {
            if (!ModsConfig.IsActive(PackageId)) return false;

            if (dialogType != null && doWindowContentsMethod != null && dialogMapField != null
                && primarchVialDef != null && ThingDefOf.HumanEmbryo != null)
            {
                return true;
            }

            if (!warnedOnce)
            {
                warnedOnce = true;
                Logger.Warning(
                    "RimDark Mankind's Finest is active but Dialog_CraftPrimarchEmbryo's expected members " +
                    "(DoWindowContents(Rect), 'map' field, or the " + PrimarchVialDefName + "/HumanEmbryo defs) " +
                    "could not be resolved (API likely changed). The gene manipulation table's custom " +
                    "primarch-embryo picker will not see Matter Network items; its ordinary bills are unaffected.");
            }

            return false;
        }

        // ── Patch 1: mark when we're inside the dialog's own render call ────────
        [HarmonyPatch]
        public static class Patch_DoWindowContents
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() => doWindowContentsMethod;

            public static void Prefix(object __instance)
            {
                activeDialogMap = dialogMapField.GetValue(__instance) as Map;
                activeDialogDepth++;
            }

            public static void Postfix()
            {
                activeDialogDepth = Math.Max(0, activeDialogDepth - 1);
                if (activeDialogDepth == 0) activeDialogMap = null;
            }
        }

        // ── Patch 2: widen the two ThingsOfDef picker calls while flagged ────────
        //
        // ThingsOfDef is a hot vanilla method called constantly by unrelated game
        // systems; the depth check below is an O(1) short-circuit so this patch is a
        // no-op overhead-wise for every call that isn't one of the dialog's own two
        // picker lookups.
        [HarmonyPatch(typeof(ListerThings), nameof(ListerThings.ThingsOfDef))]
        public static class Patch_ThingsOfDef
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static void Postfix(ListerThings __instance, ThingDef def, ref List<Thing> __result)
            {
                if (activeDialogDepth == 0 || activeDialogMap == null) return;
                if (def != primarchVialDef && def != ThingDefOf.HumanEmbryo) return;
                if (__instance != activeDialogMap.listerThings) return;

                __result = GetSelectableThings(activeDialogMap, def, __result);
            }
        }

        // Builds a fresh combined list so we never mutate ListerThings' own live,
        // cached list (the one __result points to coming in). Starts from RimDark's
        // own spawned-things result (unchanged order/behavior), then appends matching
        // unspawned Matter Network items whose network can actually deliver them.
        private static List<Thing> GetSelectableThings(Map map, ThingDef def, List<Thing> spawnedThings)
        {
            List<Thing> combined = new List<Thing>(spawnedThings ?? (IEnumerable<Thing>)Array.Empty<Thing>());
            HashSet<Thing> seen = new HashSet<Thing>(combined);

            // NetworkItemSearchUtility.Networks already restricts to extraction-enabled
            // (operational, interface-having) networks; we only need to additionally
            // confirm one of those interfaces is actually spawned on this map.
            foreach (DataNetwork network in NetworkItemSearchUtility.Networks(map))
            {
                if (!NetworkHasSpawnedInterfaceOnMap(network, map)) continue;

                foreach (Thing item in network.StoredItems)
                {
                    if (item == null || item.def != def || item.Destroyed || item.stackCount <= 0) continue;
                    if (item.Spawned) continue; // already present in the vanilla list above
                    if (!seen.Add(item)) continue;

                    combined.Add(item);
                }
            }

            return combined;
        }

        private static bool NetworkHasSpawnedInterfaceOnMap(DataNetwork network, Map map)
        {
            foreach (NetworkBuildingNetworkInterface iface in network.NetworkInterfaces)
            {
                if (iface != null && iface.Spawned && iface.Map == map)
                    return true;
            }

            return false;
        }
    }
}
