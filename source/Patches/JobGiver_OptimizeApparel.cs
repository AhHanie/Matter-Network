using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobGiver_OptimizeApparel
    {
        [HarmonyPatch(typeof(JobGiver_OptimizeApparel), nameof(JobGiver_OptimizeApparel.TryCreateRecolorJob))]
        public static class TryCreateRecolorJob
        {
            public static void Postfix(Pawn pawn, bool dryRun, ref Job job, ref bool __result)
            {
                if (__result || !ModLister.CheckIdeology("Apparel recoloring") || !pawn.apparel.AnyApparelNeedsRecoloring)
                {
                    return;
                }

                Thing station = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(ThingDefOf.StylingStation), PathEndMode.Touch, TraverseParms.For(pawn), 9999f, t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && pawn.CanReserveSittableOrSpot(t.InteractionCell));
                if (station == null)
                {
                    return;
                }

                List<Apparel> apparelToRecolor = new List<Apparel>();
                foreach (Apparel item in pawn.apparel.WornApparel)
                {
                    if (item.DesiredColor.HasValue)
                    {
                        apparelToRecolor.Add(item);
                    }
                }

                if (apparelToRecolor.Count == 0)
                {
                    return;
                }

                List<Thing> networkDyes = new List<Thing>();
                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(pawn.Map))
                {
                    if (item.def == ThingDefOf.Dye && !item.IsForbidden(pawn) && pawn.CanReserve(item))
                    {
                        networkDyes.Add(item);
                    }
                }

                if (networkDyes.Count == 0)
                {
                    return;
                }

                networkDyes.SortBy(t => NetworkItemSearchUtility.GetThingDistanceSquared(pawn, t));
                List<Thing> queueDye = new List<Thing>();
                List<Apparel> queueApparel = new List<Apparel>();

                foreach (Thing dye in networkDyes)
                {
                    for (int i = 0; i < dye.stackCount && pawn.CanReserve(dye, 1, i + 1) && apparelToRecolor.Count > 0; i++)
                    {
                        queueApparel.Add(apparelToRecolor[apparelToRecolor.Count - 1]);
                        if (!queueDye.Contains(dye))
                        {
                            queueDye.Add(dye);
                        }

                        apparelToRecolor.RemoveAt(apparelToRecolor.Count - 1);
                    }

                    if (apparelToRecolor.Count == 0)
                    {
                        break;
                    }
                }

                if (queueApparel.Count == 0)
                {
                    return;
                }

                if (dryRun)
                {
                    __result = true;
                    return;
                }

                job = JobMaker.MakeJob(JobDefOf.RecolorApparel);
                List<LocalTargetInfo> dyeQueue = job.GetTargetQueue(TargetIndex.A);
                for (int i = 0; i < queueDye.Count; i++)
                {
                    dyeQueue.Add(queueDye[i]);
                }

                List<LocalTargetInfo> apparelQueue = job.GetTargetQueue(TargetIndex.B);
                for (int i = 0; i < queueApparel.Count; i++)
                {
                    apparelQueue.Add(queueApparel[i]);
                }

                job.SetTarget(TargetIndex.C, station);
                job.count = queueApparel.Count;
                __result = true;
            }
        }
    }
}
