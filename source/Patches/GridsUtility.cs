using HarmonyLib;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_GridsUtility
    {
        [HarmonyPatch(typeof(GridsUtility), "Fogged", new System.Type[] { typeof(IntVec3), typeof(Map) })]
        public static class Fogged
        {
            public static bool Prefix (IntVec3 c, ref bool __result)
            {
                if (!Patch_Pawn_TradeTracker.PAWNTRADER_COLONYTHINGSBUY_FLAG)
                {
                    return true;
                }

                __result = Patch_Pawn_TradeTracker.PAWNTRADER_COLONYTHINGSBUY_MAP.fogGrid.IsFogged(c);
                return false;
            }
        }
    }
}
