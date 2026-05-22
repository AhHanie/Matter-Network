using HarmonyLib;
using System.Collections.Generic;
using Verse.AI;
using Verse;
using System;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Reachability
    {
        [HarmonyPatch(typeof(Reachability), "CanReach", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
        public static class CanReach
        {
            public static bool Prefix(IntVec3 start, LocalTargetInfo dest, PathEndMode peMode, TraverseParms traverseParams, ref bool __result, Map ___map, Reachability __instance)
            {
                if (!(dest.Thing?.def.EverStorable(false) ?? false))
                    return true;

                NetworksMapComponent mapComp = ___map.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(dest.Thing, out DataNetwork network))
                {
                    return true;
                }

                if (!network.IsOperational)
                {
                    __result = false;
                    return false;
                }

                List<NetworkBuildingNetworkInterface> interfaces = network.NetworkInterfaces;
                PathEndMode interfacePeMode = GetInterfacePathEndMode(peMode);
                foreach (NetworkBuildingNetworkInterface interf in interfaces)
                {
                    if (__instance.CanReach(start, interf.InteractionCell, interfacePeMode, traverseParams))
                    {
                        __result = true;
                        return false;
                    }
                }

                __result = false;
                return false;
            }
        }

        private static PathEndMode GetInterfacePathEndMode(PathEndMode peMode)
        {
            return peMode == PathEndMode.InteractionCell ? PathEndMode.OnCell : peMode;
        }
    }
}
