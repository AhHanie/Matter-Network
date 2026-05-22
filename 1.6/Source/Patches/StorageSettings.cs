using HarmonyLib;
using RimWorld;

namespace SK_Matter_Network.Patches
{
    public static class Patch_StorageSettings
    {
        [HarmonyPatch(typeof(StorageSettings), "Priority", MethodType.Setter)]
        public static class Priority
        {
            public static void Postfix(StorageSettings __instance)
            {
                if (__instance.owner is NetworkBuildingNetworkInterface)
                {
                    __instance.owner.Notify_SettingsChanged();
                }
            }
        }
    }
}
