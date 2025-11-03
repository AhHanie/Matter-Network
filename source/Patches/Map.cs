using HarmonyLib;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Map
    {
        public static bool MAP_EXPOSEDATA_FLAG = false;
        public static Map CURRENT_MAP = null;

        [HarmonyPatch(typeof(Map), "ExposeData")]
        public static class ExposeData
        {
            public static void Prefix(Map __instance)
            {
                MAP_EXPOSEDATA_FLAG = true;
                CURRENT_MAP = __instance;
            }

            public static void Postfix()
            {
                MAP_EXPOSEDATA_FLAG = false;
                CURRENT_MAP = null;
            }
        }
    }
}
