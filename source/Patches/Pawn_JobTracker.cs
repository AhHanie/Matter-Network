using HarmonyLib;
using Verse;
using Verse.AI;
using System.Collections.Generic;
using System.Linq;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Pawn_JobTracker
    {
        [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
        public static class StartJob
        {
            public static void Prefix(Job newJob, Pawn ___pawn)
            {
                if (IsItemInNetwork(newJob.targetA))
                {
                    Log.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {newJob.targetA.Thing.def.defName} in network");
                }

                if (IsItemInNetwork(newJob.targetB))
                {
                    Log.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {newJob.targetB.Thing.def.defName} in network");
                }

                if (IsItemInNetwork(newJob.targetC))
                {
                    Log.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {newJob.targetC.Thing.def.defName} in network");
                }

                if (IsItemInNetwork(newJob.targetQueueA))
                {
                    foreach (LocalTargetInfo trgt in newJob.targetQueueA.Where(item => IsItemInNetwork(item))) 
                    {
                        Log.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {trgt.Thing.def.defName} in network");
                    }
                }

                if (IsItemInNetwork(newJob.targetQueueB))
                {
                    foreach (LocalTargetInfo trgt in newJob.targetQueueB.Where(item => IsItemInNetwork(item)))
                    {
                        Log.Message($"Pawn {___pawn.LabelCap} started a job {newJob.def.defName} that interacts with item {trgt.Thing.def.defName} in network");
                    }
                }
            }
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

            if (target.Thing.MapHeld == null)
            {
                return false;
            }

            NetworksMapComponent mapComp = target.Thing.MapHeld.GetComponent<NetworksMapComponent>();
            if (mapComp.TryGetItemNetwork(target.Thing, out _))
            {
                return true;
            }

            return false;
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
    }
}
