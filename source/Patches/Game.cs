using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Game_Patch
    {
        [HarmonyPatch(typeof(Game), "FinalizeInit")]
        public static class FinalizeInit
        {
            public static void Postfix()
            {
                NetworksStaticCache.Reset();
            }
        }
    }
}
