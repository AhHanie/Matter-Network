using HarmonyLib;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_HaulAIUtility
    {
        [HarmonyPatch(typeof(HaulAIUtility), "IsInHaulableInventory")]
        public static class IsInHaulableInventory
        {
            public static bool Prefix(Thing thing, ref bool __result)
            {
                if (thing.MapHeld == null)
                {
                    return true;
                }
                NetworksMapComponent mapComp = thing.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(thing, out DataNetwork network))
                {
                    return true;
                }
                __result = true;
                return false;
            }
        }
    }
}
