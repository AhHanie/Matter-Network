using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class BetterArchitectCompat
    {
        private const string PackageId = "ferny.BetterArchitect";

        private static readonly Type architectCategoryTabPatchType =
            AccessTools.TypeByName("BetterArchitect.ArchitectCategoryTab_DesignationTabOnGUI_Patch");

        private static readonly Type transpilerType =
            AccessTools.TypeByName("BetterArchitect.Designator_Build_ProcessInput_Transpiler");

        private static readonly Type materialInfoType =
            AccessTools.TypeByName("BetterArchitect.MaterialInfo");

        private static readonly MethodInfo populateMaterialsMethod =
            architectCategoryTabPatchType != null
                ? AccessTools.Method(architectCategoryTabPatchType, "PopulateMaterials")
                : null;

        private static readonly FieldInfo selectedMaterialField =
            architectCategoryTabPatchType != null
                ? AccessTools.Field(architectCategoryTabPatchType, "selectedMaterial")
                : null;

        private static readonly FieldInfo shouldSkipFloatMenuField =
            transpilerType != null
                ? AccessTools.Field(transpilerType, "shouldSkipFloatMenu")
                : null;

        private static readonly FieldInfo materialInfoDefField =
            materialInfoType != null
                ? AccessTools.Field(materialInfoType, "def")
                : null;

        private static readonly ConstructorInfo materialInfoConstructor =
            materialInfoType != null
                ? AccessTools.Constructor(materialInfoType, new[] { typeof(string), typeof(Texture2D), typeof(Color), typeof(ThingDef) })
                : null;

        private static bool warnedMissingApi;

        private static bool IsApiAvailable()
        {
            return architectCategoryTabPatchType != null
                && transpilerType != null
                && materialInfoType != null
                && populateMaterialsMethod != null
                && selectedMaterialField != null
                && shouldSkipFloatMenuField != null
                && materialInfoDefField != null
                && materialInfoConstructor != null;
        }

        private static bool IsAvailable()
        {
            bool available = IsApiAvailable();
            if (ModsConfig.IsActive(PackageId) && !available && !warnedMissingApi)
            {
                warnedMissingApi = true;
                Logger.Warning("[Better Architect Menu] Compatibility disabled because a required BetterArchitect API member was not found. Normal Matter Network construction still works.");
            }

            return available;
        }

        public static bool TryGetSelectedFloorStuff(Designator_Build designator, ThingDef buildingDef, Map map, out ThingDef stuffDef)
        {
            stuffDef = null;
            if (!IsAvailable() || map == null)
            {
                return false;
            }

            if (!(bool)shouldSkipFloatMenuField.GetValue(null))
            {
                return false;
            }

            object selectedMaterial = selectedMaterialField.GetValue(null);
            if (selectedMaterial == null)
            {
                return false;
            }

            ThingDef candidate = materialInfoDefField.GetValue(selectedMaterial) as ThingDef;
            if (candidate == null || !candidate.IsStuff || candidate.stuffProps == null || !candidate.stuffProps.CanMake(buildingDef))
            {
                return false;
            }

            if (!DebugSettings.godMode && !Patch_Designator_Build.HasUsableStuff(map, candidate))
            {
                return false;
            }

            stuffDef = candidate;
            return true;
        }

        [HarmonyPatch]
        public static class Patch_PopulateMaterials
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() => populateMaterialsMethod;

            public static void Postfix(object[] __args)
            {
                if (__args == null || __args.Length < 3)
                {
                    return;
                }

                IDictionary floorsByMaterial = __args[0] as IDictionary;
                Designator_Build designatorBuild = __args[2] as Designator_Build;
                if (floorsByMaterial == null || designatorBuild == null)
                {
                    return;
                }

                if (!(designatorBuild.PlacingDef is ThingDef buildingDef) || !buildingDef.MadeFromStuff)
                {
                    return;
                }

                Map map = Find.CurrentMap;
                if (map == null)
                {
                    return;
                }

                HashSet<ThingDef> seenStuffDefs = new HashSet<ThingDef>();
                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    ThingDef stuffDef = item.def;
                    if (!seenStuffDefs.Add(stuffDef))
                    {
                        continue;
                    }

                    if (!stuffDef.IsStuff || stuffDef.stuffProps == null || !stuffDef.stuffProps.CanMake(buildingDef))
                    {
                        continue;
                    }

                    object materialInfo = materialInfoConstructor.Invoke(new object[]
                    {
                        (string)stuffDef.LabelCap, stuffDef.uiIcon, stuffDef.uiIconColor, stuffDef
                    });

                    AddDesignatorToMaterial(floorsByMaterial, materialInfo, designatorBuild);
                }
            }

            private static void AddDesignatorToMaterial(IDictionary floorsByMaterial, object materialKey, Designator designator)
            {
                if (!floorsByMaterial.Contains(materialKey))
                {
                    floorsByMaterial[materialKey] = new List<Designator>();
                }

                List<Designator> designators = (List<Designator>)floorsByMaterial[materialKey];
                if (!designators.Contains(designator))
                {
                    designators.Add(designator);
                }
            }
        }
    }
}
