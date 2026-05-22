using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_ThingDef
    {
        [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.ConnectToPower), MethodType.Getter)]
        public static class ConnectToPower
        {
            public static void Postfix(ThingDef __instance, ref bool __result)
            {
                if (__result || __instance.EverTransmitsPower)
                {
                    return;
                }

                for (int i = 0; i < __instance.comps.Count; i++)
                {
                    if (__instance.comps[i].compClass == typeof(CompNetworkPowerController))
                    {
                        __result = true;
                        return;
                    }
                }
            }
        }
    }
}
