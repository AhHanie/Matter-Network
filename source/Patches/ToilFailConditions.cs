using HarmonyLib;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_ToilFailConditions
    {
        [HarmonyPatch(typeof(ToilFailConditions), "DespawnedOrNull")]
        public static class DespawnedOrNull
        {
            public static bool Prefix(LocalTargetInfo target, ref bool __result)
            {
                Thing thing = target.Thing;

                if (thing == null || thing.MapHeld == null)
                {
                    return true;
                }

                NetworksMapComponent mapComp = thing.MapHeld.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(thing, out DataNetwork network))
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }
    }
}