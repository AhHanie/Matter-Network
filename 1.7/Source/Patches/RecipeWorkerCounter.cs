using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_RecipeWorkerCounter
    {
        [HarmonyPatch(typeof(RecipeWorkerCounter), nameof(RecipeWorkerCounter.CountProducts), typeof(Bill_Production))]
        public static class CountProducts
        {
            [HarmonyPostfix]
            [HarmonyInfix(typeof(HaulDestinationManager), nameof(HaulDestinationManager.AllHaulSourcesListForReading), InnerTargetKind.Getter)]
            public static List<IHaulSource> ProjectNetworkSources(List<IHaulSource> __result, [HarmonyOuter] Bill_Production bill)
            {
                List<IHaulSource> sources = null;
                for (int i = 0; i < __result.Count; i++)
                {
                    IHaulSource source = __result[i];
                    if (source is NetworkBuildingController ||
                        (source is NetworkBuildingNetworkInterface iface && iface.GetDirectlyHeldThings() is ControllerItemOwner))
                    {
                        sources ??= __result.GetRange(0, i);
                        continue;
                    }

                    sources?.Add(source);
                }

                // A live controller can be absent from the registry after a topology change.
                Map map = bill.Map;
                HashSet<ControllerItemOwner> seenOwners = null;
                foreach (DataNetwork network in NetworkItemSearchUtility.Networks(map))
                {
                    NetworkBuildingController controller = network.ActiveController;
                    ControllerItemOwner owner = controller?.innerContainer;
                    if (owner == null || !controller.Spawned || controller.Map != map)
                        continue;

                    foreach (NetworkBuildingNetworkInterface iface in network.NetworkInterfaces)
                    {
                        if (!iface.Spawned || iface.Map != map || iface.GetDirectlyHeldThings() != owner)
                            continue;

                        seenOwners ??= new HashSet<ControllerItemOwner>();
                        if (seenOwners.Add(owner))
                        {
                            sources ??= new List<IHaulSource>(__result);
                            sources.Add(controller);
                        }

                        break;
                    }
                }

                return sources ?? __result;
            }
        }
    }
}
