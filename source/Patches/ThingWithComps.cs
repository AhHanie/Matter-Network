using HarmonyLib;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_ThingWithComps
    {
        [HarmonyPatch(typeof(ThingWithComps), "DrawGUIOverlay")]
        public static class DrawGUIOverlay
        {
            public static bool Prefix(Thing __instance)
            {
                if (!__instance.def.EverStorable(false))
                {
                    return true;
                }
                if (__instance.MapHeld == null)
                {
                    return true;
                }
                return !__instance.MapHeld.GetComponent<NetworksMapComponent>().TryGetItemNetwork(__instance, out _);
            }
        }
    }
}
