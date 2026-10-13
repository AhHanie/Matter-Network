using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    // A network interface presents its active controller's shared container from
    // GetDirectlyHeldThings() while online (see NetworkBuildingNetworkInterface.GetDirectlyHeldThings),
    // so vanilla's per-haul-source lister recalculation redundantly re-checks every stored stack once
    // per interface, on top of the controller's own pass over the same container. With many
    // interfaces on one network this turns an O(stacks) sweep into O(stacks x interfaces).
    //
    // These patches skip that duplicate enumeration for interfaces only, and only when the
    // controller is already registered as a haul source to cover the same scan itself. Disk drives,
    // chutes, offline/fallback interfaces, and topology transitions where the controller isn't
    // registered yet all fall through to vanilla behavior unchanged.
    //
    // For the two single-source entry points below, "the controller is registered" alone does not
    // guarantee the controller will actually be (re-)scanned as a result of this specific call - it
    // may have been checked before the state changed, or never land in the installed periodic
    // scheduler's visited range (RimWorld/ListerHaulables.HaulSourcesCheckTick uses additive index math
    // - group + i without multiplying by the group width - so sources past a certain index are never
    // visited on maps with enough registered haul sources; a pre-existing vanilla defect, not something
    // this mod attempts to patch around, since doing so via a per-tick dedup scan costs far more than
    // the rare gap it closes). Skipping on the registration assumption alone silently drops the update.
    // These two prefixes therefore only skip when we can also prove a compensating controller-level
    // scan is guaranteed as part of the same operation:
    // DataNetwork.Notify_SettingsChanged and DataNetwork.ReconcileStorageSettingsFromEndpoints both
    // set IsBroadcastingSettingsChange for the duration of their interface loop and explicitly notify
    // the controller directly afterward. Outside of that window (e.g. a brand new interface joining a
    // settled network, or any other caller), the real scan always runs.
    public static class Patch_ListerHaulables
    {
        [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.RecalculateAllInHaulSource))]
        public static class RecalculateAllInHaulSource
        {
            public static bool Prefix(IHaulSource source)
            {
                return !IsDuplicateOfCompensatedControllerPass(source);
            }
        }

        [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.Notify_HaulSourceChanged))]
        public static class Notify_HaulSourceChanged
        {
            public static bool Prefix(IHaulSource holder)
            {
                return !IsDuplicateOfCompensatedControllerPass(holder);
            }
        }

        // RecalculateAllInHaulSources enumerates each source's GetDirectlyHeldThings() directly in
        // its own body instead of delegating to RecalculateAllInHaulSource, so it can't be skipped
        // with the same kind of prefix as the two patches above. Instead, wrap that one inner call
        // with an infix.
        //
        // Decompiled shape (RimWorld 1.7, Assembly-CSharp.dll):
        //   foreach (IHaulSource source in sources)
        //   {
        //       foreach (Thing item in (IEnumerable<Thing>)source.GetDirectlyHeldThings())
        //           Check(item);
        //   }
        // For a source that is a duplicate interface pass, the infix prefix skips the real call and hands
        // the loop an empty owner, so that source's inner loop checks nothing instead of enumerating and
        // Check()-ing every item it would report.
        [HarmonyPatch(typeof(ListerHaulables), nameof(ListerHaulables.RecalculateAllInHaulSources))]
        public static class RecalculateAllInHaulSources
        {
            private static readonly ThingOwner<Thing> EmptyOwner = new ThingOwner<Thing>();

            [HarmonyPrefix]
            [HarmonyInfix(typeof(IThingHolder), nameof(IThingHolder.GetDirectlyHeldThings))]
            public static bool SkipDuplicateControllerPass(IThingHolder __instance, ref ThingOwner __result)
            {
                if (!(__instance is IHaulSource source) || !IsDuplicateOfControllerPass(source))
                {
                    return true;
                }

                __result = EmptyOwner;
                return false;
            }
        }

        // True only when: source is a network interface, it is currently presenting its active
        // controller's shared container (i.e. online and routed through that controller), the
        // controller is spawned on the same map, and the controller is itself registered as a haul
        // source - so the controller's own pass already covers every item this interface would
        // report.
        internal static bool IsDuplicateOfControllerPass(IHaulSource source)
        {
            if (!(source is NetworkBuildingNetworkInterface iface) || !iface.Spawned)
            {
                return false;
            }

            NetworkBuildingController controller = iface.ParentNetwork?.ActiveController;
            if (controller == null || !controller.Spawned || controller.Map != iface.Map)
            {
                return false;
            }

            if (!ReferenceEquals(iface.GetDirectlyHeldThings(), controller.innerContainer))
            {
                return false;
            }

            return iface.Map.haulDestinationManager.AllHaulSourcesListForReading.Contains(controller);
        }

        // Same structural check as IsDuplicateOfControllerPass, plus proof that a compensating
        // controller-level scan is guaranteed as part of the same operation: DataNetwork sets
        // IsBroadcastingSettingsChange only around the interface loops that explicitly notify the
        // controller afterward (see DataNetwork.Notify_SettingsChanged and
        // ReconcileStorageSettingsFromEndpoints), and nothing else can run "inside" that synchronous
        // window. Used only by the two single-source prefixes above; the batch transpiler below has
        // its own same-call guarantee (the controller is covered within the same batch) and keeps
        // using the bare structural check.
        private static bool IsDuplicateOfCompensatedControllerPass(IHaulSource source)
        {
            if (!IsDuplicateOfControllerPass(source))
            {
                return false;
            }

            NetworkBuildingNetworkInterface iface = (NetworkBuildingNetworkInterface)source;
            return iface.ParentNetwork.IsBroadcastingSettingsChange;
        }
    }
}
