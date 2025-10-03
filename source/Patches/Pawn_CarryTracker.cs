using HarmonyLib;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Pawn_CarryTracker
    {
        [HarmonyPatch(typeof(Pawn_CarryTracker), "TryStartCarry", new System.Type[] { typeof(Thing), typeof(int), typeof(bool) })]
        public static class TryStartCarry_Int
        {
            public static void Postfix(Thing item, int __result)
            {
                if (__result <= 0) return;
                HandlePostCarry(item);
            }
        }

        [HarmonyPatch(typeof(Pawn_CarryTracker), "TryStartCarry", new System.Type[] { typeof(Thing) })]
        public static class TryStartCarry_Thing
        {
            public static void Postfix(Thing item, bool __result)
            {
                if (!__result) return;
                HandlePostCarry(item);
            }
        }

        private static void HandlePostCarry(Thing item)
        {
            Map map = item.MapHeld;
            if (map == null) return;

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp.TryGetItemNetwork(item, out DataNetwork network))
            {
                network.MarkBytesDirty();
            }
        }
    }
}
