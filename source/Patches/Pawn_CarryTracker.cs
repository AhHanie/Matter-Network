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
                if (__result <= 0)
                {
                    return;
                }

                if (item.MapHeld == null)
                {
                    return;
                }

                NetworksMapComponent mapComp = item.MapHeld.GetComponent<NetworksMapComponent>();

                if (!mapComp.TryGetItemNetwork(item, out DataNetwork network))
                {
                    return;
                }

                Log.Message($"Pawn {___pawn.LabelShort} picked up {__result} of {item.def.defName} from network {network.NetworkId}");

                network.RemoveItem(item, __result);
            }
        }

        [HarmonyPatch(typeof(Pawn_CarryTracker), "TryStartCarry", new System.Type[] { typeof(Thing) })]
        public static class TryStartCarry_Thing
        {
            public static void Postfix(Thing item, bool __result, Pawn ___pawn)
            {
                if (!__result)
                {
                    return;
                }

                if (item.MapHeld == null)
                {
                    return;
                }

                NetworksMapComponent mapComp = item.MapHeld.GetComponent<NetworksMapComponent>();

                if (!mapComp.TryGetItemNetwork(item, out DataNetwork network))
                {
                    return;
                }

                Thing carriedThing = ___pawn.carryTracker.CarriedThing;
                if (carriedThing != null && (carriedThing == item || carriedThing.def == item.def))
                {
                    int amountPickedUp = carriedThing.stackCount;
                    Log.Message($"Pawn {___pawn.LabelShort} picked up entire stack ({amountPickedUp}) of {item.def.defName} from network {network.NetworkId}");

                    network.RemoveItem(item, amountPickedUp);
                }
            }
        }
    }
}