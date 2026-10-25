using HarmonyLib;
using RimWorld;

namespace SK_Matter_Network.Patches
{
    // Network settings (and any StorageGroup linked to a terminal) are edited in place. Vanilla's own
    // callbacks do not reach the network for these owners: the network owner is neither a haul
    // destination nor a slot group, and StorageGroup only notifies slot-group members. These patches
    // route those edits into NetworkSettingsSync.
    public static class Patch_StorageSettings
    {
        [HarmonyPatch(typeof(StorageSettings), "Priority", MethodType.Setter)]
        public static class Priority
        {
            public static void Postfix(StorageSettings __instance)
            {
                if (NetworkSettingsSync.IsNetworkOrGroupSettings(__instance))
                {
                    NetworkSettingsSync.NotifyEdited(__instance);
                }
            }
        }

        // CopyFrom sets the priority, copies the filter and notifies again; treat it as one edit.
        [HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.CopyFrom))]
        public static class CopyFrom
        {
            public static void Prefix(StorageSettings __instance, out bool __state)
            {
                __state = !NetworkSettingsSync.IsSuppressed && NetworkSettingsSync.IsNetworkOrGroupSettings(__instance);
                if (__state)
                {
                    NetworkSettingsSync.BeginCopy();
                }
            }

            public static void Finalizer(bool __state)
            {
                if (__state)
                {
                    NetworkSettingsSync.EndCopy();
                }
            }
        }

        [HarmonyPatch(typeof(StorageGroup), nameof(StorageGroup.Notify_SettingsChanged))]
        public static class StorageGroupSettingsChanged
        {
            public static void Postfix(StorageGroup __instance)
            {
                NetworkSettingsSync.NotifyEdited(__instance.GetStoreSettings());
            }
        }
    }
}
