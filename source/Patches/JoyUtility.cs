using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JoyUtility
    {
        [HarmonyPatch(typeof(JoyUtility), nameof(JoyUtility.JoyKindsOnMapTempList))]
        public static class JoyKindsOnMapTempList
        {
            public static void Postfix(Map map, ref List<JoyKindDef> __result)
            {
                AppendNetworkJoyKinds(map, __result);
            }
        }

        [HarmonyPatch(typeof(JoyUtility), nameof(JoyUtility.JoyKindsOnMapString))]
        public static class JoyKindsOnMapString
        {
            public static void Postfix(Map map, ref string __result)
            {
                List<JoyKindDef> networkKinds = new List<JoyKindDef>();
                AppendNetworkJoyKinds(map, networkKinds);

                foreach (JoyKindDef kind in networkKinds)
                {
                    string label = kind.LabelCap.ToString();
                    if (__result.Contains(label))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(__result))
                    {
                        __result += "\n";
                    }

                    __result += "  - " + label;
                }
            }
        }

        private static void AppendNetworkJoyKinds(Map map, List<JoyKindDef> kinds)
        {
            foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                if (item.def.IsIngestible && item.def.ingestible?.joyKind != null && !kinds.Contains(item.def.ingestible.joyKind))
                {
                    kinds.Add(item.def.ingestible.joyKind);
                }
                else if (item is Book && !kinds.Contains(JoyKindDefOf.Reading))
                {
                    kinds.Add(JoyKindDefOf.Reading);
                }
            }
        }
    }
}
