using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace SK_Matter_Network.Patches
{
    public static class Patch_ForbidUtility
    {
        [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new[] { typeof(Thing), typeof(Pawn) })]
        public static class IsForbidden
        {
            public static void Postfix(Thing t, Pawn pawn, ref bool __result)
            {
                if (!__result)
                {
                    return;
                }

                NetworksMapComponent mapComp = t.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(t, out DataNetwork network) || !network.IsOperational)
                {
                    return;
                }

                if (!IsForbiddenByHeldPositionOnly(t, pawn))
                {
                    return;
                }

                if (NetworkHasAllowedAccessPoint(network, pawn))
                {
                    __result = false;
                }
            }

            private static bool IsForbiddenByHeldPositionOnly(Thing item, Pawn pawn)
            {
                if ((item.Spawned || item.SpawnedParentOrMe != pawn) && item.PositionHeld.InAllowedArea(pawn))
                {
                    return false;
                }

                if (item.IsForbidden(pawn.Faction) || item.IsForbidden(pawn.HostFaction))
                {
                    return false;
                }

                Lord pawnLord = pawn.GetLord();
                LordManager lordManager = pawn.MapHeld?.lordManager;
                if (lordManager == null)
                {
                    return true;
                }

                if (pawnLord != null && pawnLord.extraForbiddenThings.Contains(item))
                {
                    return false;
                }

                foreach (Lord lord in lordManager.lords)
                {
                    if (lord == pawnLord)
                    {
                        continue;
                    }

                    if (lord.CurLordToil is LordToil_Ritual ritual && ritual.ReservedThings.Contains(item))
                    {
                        return false;
                    }

                    if (lord.CurLordToil is LordToil_PsychicRitual psychicRitual)
                    {
                        PsychicRitualDef_InvocationCircle invocationCircle = psychicRitual.RitualData.psychicRitual.def as PsychicRitualDef_InvocationCircle;
                        if (invocationCircle != null &&
                            invocationCircle.TargetRole != null &&
                            psychicRitual.RitualData.psychicRitual.assignments.FirstAssignedPawn(invocationCircle.TargetRole) == item &&
                            !(psychicRitual.RitualData.CurPsychicRitualToil is PsychicRitualToil_TargetCleanup))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            private static bool NetworkHasAllowedAccessPoint(DataNetwork network, Pawn pawn)
            {
                NetworkBuildingController controller = network.ActiveController;
                if (controller.Position.InAllowedArea(pawn))
                {
                    return true;
                }

                foreach (NetworkBuildingNetworkInterface networkInterface in network.NetworkInterfaces)
                {
                    if (networkInterface.InteractionCell.InAllowedArea(pawn))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
