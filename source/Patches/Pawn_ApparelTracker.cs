using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Pawn_ApparelTracker
    {
        [HarmonyPatch(typeof(Pawn_ApparelTracker), "Wear")]
        public static class Wear
        {
            private static DataNetwork currentNetwork;
            public static void Prefix(Apparel newApparel, Pawn_ApparelTracker __instance)
            {
                // Function is used during game initilization where map is null
                if (__instance.pawn.Map == null)
                {
                    return;
                }
                NetworksMapComponent mapComp = __instance.pawn.Map.GetComponent<NetworksMapComponent>();

                if (!mapComp.TryGetItemNetwork(newApparel, out DataNetwork network))
                {
                    return;
                }

                currentNetwork = network;
                Log.Message($"Pawn {__instance.pawn.LabelShort} is going to wear a {newApparel.def.defName} from network {network.NetworkId}");
                network.RemoveItem(newApparel, 1, true);
            }

            public static void Postfix()
            {
                currentNetwork?.ValidateNetwork();
            }
        }
    }
}
