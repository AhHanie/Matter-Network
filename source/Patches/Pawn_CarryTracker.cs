using HarmonyLib;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Pawn_CarryTracker
    {
        [HarmonyPatch(typeof(Pawn_CarryTracker), "TryStartCarry", new System.Type[] { typeof(Thing), typeof(int), typeof(bool) })]
        public static class TryStartCarry_Int
        {
            public static void Postfix(Thing item, int count, int __result, Pawn ___pawn)
            {
                // __result is the number of items actually picked up
                if (__result <= 0)
                {
                    return; // Nothing was picked up
                }

                NetworksMapComponent mapComp = item.MapHeld.GetComponent<NetworksMapComponent>();

                // Check if the item is in a network
                if (!mapComp.TryGetItemNetwork(item, out DataNetwork network))
                {
                    return; // Item not in network
                }

                Log.Message($"Pawn {___pawn.LabelShort} picked up {__result} of {item.def.defName} from network {network.NetworkId}");

                // Remove or update the item in the network
                network.RemoveItem(item, __result);
            }
        }

        [HarmonyPatch(typeof(Pawn_CarryTracker), "TryStartCarry", new System.Type[] { typeof(Thing) })]
        public static class TryStartCarry_Thing
        {
            public static void Postfix(Thing item, bool __result, Pawn ___pawn)
            {
                // __result indicates if the carry was successful
                if (!__result)
                {
                    return; // Nothing was picked up
                }

                NetworksMapComponent mapComp = item.MapHeld.GetComponent<NetworksMapComponent>();

                // Check if the item is in a network
                if (!mapComp.TryGetItemNetwork(item, out DataNetwork network))
                {
                    return; // Item not in network
                }

                // For this overload, the entire thing is picked up
                Thing carriedThing = ___pawn.carryTracker.CarriedThing;
                if (carriedThing != null && (carriedThing == item || carriedThing.def == item.def))
                {
                    int amountPickedUp = carriedThing.stackCount;
                    Log.Message($"Pawn {___pawn.LabelShort} picked up entire stack ({amountPickedUp}) of {item.def.defName} from network {network.NetworkId}");

                    // Remove the entire item from the network
                    network.RemoveItem(item, amountPickedUp);
                }
            }
        }
    }
}