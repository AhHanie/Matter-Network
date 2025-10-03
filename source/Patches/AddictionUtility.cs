using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_AddictionUtility
    {
        [HarmonyPatch(typeof(AddictionUtility), nameof(AddictionUtility.CanBingeOnNow))]
        public static class CanBingeOnNow_Patch
        {
            public static void Postfix(Pawn pawn, ChemicalDef chemical, DrugCategory drugCategory, ref bool __result)
            {
                if (__result || !chemical.canBinge || !pawn.Spawned)
                {
                    return;
                }

                NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();

                foreach (DataNetwork network in mapComp.ExtractionEnabledNetworks)
                {
                    foreach (NetworkBuildingNetworkInterface networkInterface in network.NetworkInterfaces)
                    {
                        if (networkInterface.InteractionCell.Fogged(pawn.Map))
                        {
                            continue;
                        }

                        if (!pawn.CanReach(networkInterface.InteractionCell, PathEndMode.OnCell, Danger.Deadly))
                        {
                            continue;
                        }

                        if (!networkInterface.InteractionCell.Roofed(pawn.Map) && !networkInterface.InteractionCell.InHorDistOf(pawn.Position, 45f))
                        {
                            continue;
                        }

                        foreach (Thing item in network.StoredItems)
                        {
                            if (IsMatchingDrug(item, chemical, drugCategory))
                            {
                                __result = true;
                                return;
                            }
                        }
                    }
                }
            }
        }

        public static bool IsMatchingDrug(Thing thing, ChemicalDef chemical, DrugCategory drugCategory)
        {
            if (!thing.def.IsDrug || thing.def.ingestible == null)
            {
                return false;
            }

            if (drugCategory != DrugCategory.Any && !thing.def.ingestible.drugCategory.IncludedIn(drugCategory))
            {
                return false;
            }

            CompDrug compDrug = thing.TryGetComp<CompDrug>();
            return compDrug != null && compDrug.Props.chemical == chemical;
        }
    }
}
