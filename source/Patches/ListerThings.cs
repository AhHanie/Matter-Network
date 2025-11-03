using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_ListerThings
    {
        [HarmonyPatch(typeof(ListerThings), "AllThings", MethodType.Getter)]
        public static class AllThings
        {
            public static void Postfix(ref List<Thing> __result)
            {
                if (!Patch_Map.MAP_EXPOSEDATA_FLAG || Scribe.mode != LoadSaveMode.Saving)
                {
                    return;
                }

                NetworksMapComponent mapComp = Patch_Map.CURRENT_MAP.GetComponent<NetworksMapComponent>();

                __result = __result.Where(item => !mapComp.TryGetItemNetwork(item, out _)).ToList();
            }
        }
    }
}
