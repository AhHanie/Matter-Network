using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobDriver_Equip
    {
        [HarmonyPatch(typeof(JobDriver_Equip), "MakeNewToils")]
        public static class MakeNewToils
        {
            public static IEnumerable<Toil> Postfix(IEnumerable<Toil> values, JobDriver_Equip __instance)
            {
                Toil pending = null;
                bool havePending = false;
                foreach (Toil toil in values)
                {
                    if (havePending)
                    {
                        yield return pending;
                    }

                    pending = toil;
                    havePending = true;
                }

                if (havePending)
                {
                    WrapFinalToilInitAction(pending, __instance);
                    yield return pending;
                }
            }
        }

        private static void WrapFinalToilInitAction(Toil toil, JobDriver_Equip driver)
        {
            Action originalInitAction = toil.initAction;
            toil.initAction = delegate
            {
                if (TryHandleNetworkEquip(driver))
                {
                    return;
                }

                originalInitAction?.Invoke();
            };
        }

        // Vanilla JobDriver_Equip only knows how to pull an unspawned weapon from a
        // Building_OutfitStand. A network-held weapon is also unspawned, so mirror that
        // handling here: detach it from the controller's ControllerItemOwner ourselves
        // (vanilla's own DeSpawn()-based detachment is a no-op for an already-unspawned
        // thing, which would otherwise leave the weapon stuck in two owners at once).
        private static bool TryHandleNetworkEquip(JobDriver_Equip driver)
        {
            Thing target = driver.job.GetTarget(TargetIndex.A).Thing;
            if (target == null || target.Spawned || target.ParentHolder is Building_OutfitStand)
            {
                return false;
            }

            if (!(target is ThingWithComps thingWithComps))
            {
                return false;
            }

            Map map = target.MapHeld;
            if (map == null)
            {
                return false;
            }

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (!mapComp.TryGetItemNetwork(thingWithComps, out DataNetwork network))
            {
                return false;
            }

            Pawn pawn = driver.pawn;
            if (!network.CanExtractItems
                || network.ActiveController?.innerContainer == null
                || !network.ActiveController.innerContainer.Contains(thingWithComps))
            {
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                return true;
            }

            ThingWithComps toEquip;
            if (thingWithComps.def.stackLimit > 1 && thingWithComps.stackCount > 1)
            {
                toEquip = (ThingWithComps)thingWithComps.SplitOff(1);
            }
            else
            {
                toEquip = thingWithComps;
                network.ActiveController.innerContainer.Remove(toEquip);
            }
            network.MarkBytesDirty();

            pawn.equipment.MakeRoomFor(toEquip);
            pawn.equipment.AddEquipment(toEquip);
            thingWithComps.def.soundInteract?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            return true;
        }
    }
}
