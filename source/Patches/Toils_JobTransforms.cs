using HarmonyLib;
using System;
using Verse.AI;
using Verse;
using System.Collections.Generic;
using RimWorld;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Toils_JobTransforms
    {
        [HarmonyPatch(typeof(Toils_JobTransforms), "ClearDespawnedNullOrForbiddenQueuedTargets")]
        public static class ClearDespawnedNullOrForbiddenQueuedTargets
        {
            public static void Postfix(TargetIndex ind, ref Toil __result, Func<Thing, bool> validator = null)
            {
                Toil originalToil = __result;
                Action originalInitAction = originalToil.initAction;

                __result.initAction = delegate
                {
                    Pawn actor = originalToil.actor;
                    List<LocalTargetInfo> copy = new List<LocalTargetInfo>(actor.jobs.curJob.GetTargetQueue(ind));
                    originalInitAction();
                    List<LocalTargetInfo> result = actor.jobs.curJob.GetTargetQueue(ind);
                    NetworksMapComponent mapComp = actor.Map.GetComponent<NetworksMapComponent>();
                    foreach (LocalTargetInfo item in copy)
                    {
                        if (!result.Contains(item) && item.HasThing && !item.Thing.IsForbidden(actor))
                        {
                            if (validator != null && !validator(item.Thing))
                            {
                                continue;
                            }
                            if (mapComp.TryGetItemNetwork(item.Thing, out _))
                            {
                                result.Add(item);
                            }
                        }
                    }
                };
            }
        }
    }
}
