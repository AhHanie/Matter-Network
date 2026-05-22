using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Pawn_JobTracker
    {
        private const int MaxJobHistory = 20;
        private static readonly List<JobHistoryEntry> lastStartedJobs = new List<JobHistoryEntry>(MaxJobHistory);

        [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
        public static class StartJob
        {
            public static void Prefix(Job newJob, Pawn ___pawn)
            {
                RecordStartedJob(newJob, ___pawn);
                if (newJob == null)
                {
                    return;
                }

                if (IsItemInNetwork(newJob.targetA))
                {
                    Logger.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {newJob.targetA.Thing.def.defName}{newJob.targetA.Thing.thingIDNumber} stackCount: {newJob.targetA.Thing.stackCount} in network");
                }

                if (IsItemInNetwork(newJob.targetB))
                {
                    Logger.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {newJob.targetB.Thing.def.defName}{newJob.targetB.Thing.thingIDNumber} stackCount: {newJob.targetB.Thing.stackCount} in network");
                }

                if (IsItemInNetwork(newJob.targetC))
                {
                    Logger.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {newJob.targetC.Thing.def.defName}{newJob.targetC.Thing.thingIDNumber} stackCount: {newJob.targetC.Thing.stackCount} in network");
                }

                if (IsItemInNetwork(newJob.targetQueueA))
                {
                    foreach (LocalTargetInfo trgt in newJob.targetQueueA.Where(item => IsItemInNetwork(item)))
                    {
                        Logger.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {trgt.Thing.def.defName}{trgt.Thing.thingIDNumber} stackCount: {trgt.Thing.stackCount} in network");
                    }
                }

                if (IsItemInNetwork(newJob.targetQueueB))
                {
                    foreach (LocalTargetInfo trgt in newJob.targetQueueB.Where(item => IsItemInNetwork(item)))
                    {
                        Logger.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {trgt.Thing.def.defName}{trgt.Thing.thingIDNumber} stackCount: {trgt.Thing.stackCount} in network");
                    }
                }
            }
        }

        public static string GetLastStartedJobsReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Last started jobs:");

            if (lastStartedJobs.Count == 0)
            {
                sb.AppendLine("  <none>");
                return sb.ToString();
            }

            for (int i = 0; i < lastStartedJobs.Count; i++)
            {
                JobHistoryEntry entry = lastStartedJobs[i];
                sb.AppendLine($"  [{i + 1}] tick={entry.Tick} pawn={entry.PawnLabel} job={entry.JobDefName} jobId={entry.JobId} interactsWithNetworkItem={entry.InteractsWithNetworkItem} job={entry.JobText}");
                foreach (string targetInfo in entry.TargetInfos)
                {
                    sb.AppendLine($"      {targetInfo}");
                }
            }

            return sb.ToString();
        }

        public static string DescribeThingForDebug(Thing thing, Pawn pawnContext = null)
        {
            if (thing == null)
            {
                return "<null thing>";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append($"{thing.def?.defName ?? "nullDef"}{thing.thingIDNumber}");
            sb.Append($" label={thing.LabelShort ?? "null"}");
            sb.Append($" stackCount={thing.stackCount}");
            sb.Append($" destroyed={thing.Destroyed}");
            sb.Append($" spawned={thing.Spawned}");
            sb.Append($" position={SafeToString(() => thing.Position)}");
            sb.Append($" positionHeld={SafeToString(() => thing.PositionHeld)}");
            sb.Append($" map={DescribeMap(thing.Map)}");
            sb.Append($" mapHeld={DescribeMap(thing.MapHeld)}");
            sb.Append($" parentHolder={DescribeHolder(thing.ParentHolder)}");
            sb.Append($" holdingOwner={DescribeHolder(thing.holdingOwner?.Owner)}");
            sb.Append($" networkItem={IsItemInNetwork(thing)}");
            sb.Append($" locationContainsNetworkBuilding={LocationContainsNetworkBuilding(thing, pawnContext)}");
            return sb.ToString();
        }

        private static void RecordStartedJob(Job job, Pawn pawn)
        {
            if (job == null)
            {
                return;
            }

            JobHistoryEntry entry = new JobHistoryEntry
            {
                Tick = Find.TickManager?.TicksGame ?? -1,
                PawnLabel = pawn?.LabelCap?.ToString() ?? "null",
                JobDefName = job.def?.defName ?? "null",
                JobId = job.loadID,
                JobText = job.ToString(),
                InteractsWithNetworkItem = JobInteractsWithNetworkItem(job),
                TargetInfos = CollectTargetInfos(job, pawn)
            };

            lastStartedJobs.Add(entry);
            if (lastStartedJobs.Count > MaxJobHistory)
            {
                lastStartedJobs.RemoveAt(0);
            }
        }

        private static List<string> CollectTargetInfos(Job job, Pawn pawn)
        {
            List<string> infos = new List<string>();
            AddTargetInfo(infos, "targetA", job.targetA, pawn);
            AddTargetInfo(infos, "targetB", job.targetB, pawn);
            AddTargetInfo(infos, "targetC", job.targetC, pawn);
            AddTargetQueueInfos(infos, "targetQueueA", job.targetQueueA, pawn);
            AddTargetQueueInfos(infos, "targetQueueB", job.targetQueueB, pawn);
            return infos;
        }

        private static bool JobInteractsWithNetworkItem(Job job)
        {
            if (job == null)
            {
                return false;
            }

            return IsItemInNetwork(job.targetA) ||
                IsItemInNetwork(job.targetB) ||
                IsItemInNetwork(job.targetC) ||
                IsItemInNetwork(job.targetQueueA) ||
                IsItemInNetwork(job.targetQueueB);
        }

        private static void AddTargetQueueInfos(List<string> infos, string label, List<LocalTargetInfo> targets, Pawn pawn)
        {
            if (targets == null)
            {
                infos.Add($"{label}=<null>");
                return;
            }

            if (targets.Count == 0)
            {
                infos.Add($"{label}=<empty>");
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                AddTargetInfo(infos, $"{label}[{i}]", targets[i], pawn);
            }
        }

        private static void AddTargetInfo(List<string> infos, string label, LocalTargetInfo target, Pawn pawn)
        {
            if (!target.IsValid)
            {
                infos.Add($"{label}=<invalid>");
                return;
            }

            if (target.HasThing)
            {
                infos.Add($"{label}=thing {DescribeThingForDebug(target.Thing, pawn)}");
                return;
            }

            infos.Add($"{label}=cell {target.Cell} locationContainsNetworkBuilding={CellContainsNetworkBuilding(target.Cell, pawn?.MapHeld)}");
        }

        private static bool IsItemInNetwork(LocalTargetInfo target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.Thing == null)
            {
                return false;
            }

            return IsItemInNetwork(target.Thing);
        }

        private static bool IsItemInNetwork(Thing thing)
        {
            if (thing == null || thing.MapHeld == null)
            {
                return false;
            }

            NetworksMapComponent mapComp = thing.MapHeld.GetComponent<NetworksMapComponent>();
            return mapComp.TryGetItemNetwork(thing, out _);
        }

        private static bool IsItemInNetwork(List<LocalTargetInfo> targets)
        {
            if (targets == null)
            {
                return false;
            }

            foreach (LocalTargetInfo trgt in targets)
            {
                if (IsItemInNetwork(trgt))
                {
                    return true;
                }
            }

            return false;
        }

        private static string DescribeMap(Map map)
        {
            if (map == null)
            {
                return "null";
            }

            return $"index={map.Index} uniqueID={map.uniqueID}";
        }

        private static string DescribeHolder(IThingHolder holder)
        {
            if (holder == null)
            {
                return "null";
            }

            Thing holderThing = holder as Thing;
            if (holderThing != null)
            {
                return $"{holder.GetType().Name}({holderThing.def?.defName ?? "nullDef"}{holderThing.thingIDNumber})";
            }

            return holder.GetType().FullName;
        }

        private static bool LocationContainsNetworkBuilding(Thing thing, Pawn pawnContext)
        {
            if (thing == null)
            {
                return false;
            }

            Map map = thing.MapHeld ?? thing.Map ?? pawnContext?.MapHeld ?? pawnContext?.Map;
            return CellContainsNetworkBuilding(thing.PositionHeld, map);
        }

        private static bool CellContainsNetworkBuilding(IntVec3 cell, Map map)
        {
            if (map == null || !cell.IsValid)
            {
                return false;
            }

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            return mapComp != null && mapComp.CellHasNetworkBuilding(cell);
        }

        private static string SafeToString<T>(Func<T> valueGetter)
        {
            try
            {
                T value = valueGetter();
                return value?.ToString() ?? "null";
            }
            catch (Exception ex)
            {
                return $"<error:{ex.GetType().Name}>";
            }
        }

        private class JobHistoryEntry
        {
            public int Tick;
            public string PawnLabel;
            public string JobDefName;
            public int JobId;
            public string JobText;
            public bool InteractsWithNetworkItem;
            public List<string> TargetInfos;
        }
    }
}
