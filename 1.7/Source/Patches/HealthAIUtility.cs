using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_HealthAIUtility
    {
        public static bool UseHaulSourcesForMedicineSearch { get; private set; }

        [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.FindBestMedicine))]
        public static class FindBestMedicine
        {
            public static void Prefix()
            {
                UseHaulSourcesForMedicineSearch = true;
            }

            public static void Finalizer()
            {
                UseHaulSourcesForMedicineSearch = false;
            }
        }
    }
}
