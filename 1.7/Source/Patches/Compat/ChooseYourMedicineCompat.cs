using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class ChooseYourMedicineCompat
    {
        private const string PackageId = "Kopp.ChooseYourMedicine";
        private const string FindBestMedicinePrefixTypeName = "ChooseYourMedicine.FindBestMedicine_Prefix";

        private static readonly System.Type findBestMedicinePrefixType = AccessTools.TypeByName(FindBestMedicinePrefixTypeName);

        private static bool IsAvailable()
        {
            return ModsConfig.IsActive(PackageId) && findBestMedicinePrefixType != null;
        }

        [HarmonyPatch]
        public static class GetMapsMedicine
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return IsAvailable();
            }

            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(findBestMedicinePrefixType, "GetMapsMedicine");
            }

            public static void Postfix(Pawn patient, ref IEnumerable<Thing> __result)
            {
                if (__result == null)
                {
                    return;
                }

                __result = EnumerateMapAndNetworkMedicine(patient.MapHeld, __result);
            }
        }

        private static IEnumerable<Thing> EnumerateMapAndNetworkMedicine(Map map, IEnumerable<Thing> mapMedicine)
        {
            HashSet<Thing> yieldedThings = new HashSet<Thing>();

            foreach (Thing thing in mapMedicine)
            {
                if (thing != null && yieldedThings.Add(thing))
                {
                    yield return thing;
                }
            }

            foreach (Thing thing in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                if (thing != null && thing.def.IsMedicine && yieldedThings.Add(thing))
                {
                    yield return thing;
                }
            }
        }
    }
}
