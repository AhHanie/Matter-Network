using HarmonyLib;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Toils_Haul
    {
        [HarmonyPatch(typeof(Toils_Haul), "ErrorCheckForCarry")]
        public static class ErrorCheckForCarry
        {
            public static bool Prefix(Pawn pawn, Thing haulThing, bool canTakeFromInventory, ref bool __result)
            {
                NetworksMapComponent mapComp = haulThing.MapHeld.GetComponent<NetworksMapComponent>();
                if (!mapComp.TryGetItemNetwork(haulThing, out DataNetwork network))
                {
                    return true;
                }

                if (haulThing.stackCount == 0)
                {
                    Log.Message(pawn?.ToString() + " tried to start carry " + haulThing?.ToString() + " which had stackcount 0.");
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    __result = true;
                    return false;
                }

                if (pawn.jobs.curJob.count <= 0)
                {
                    Log.Error("Invalid count: " + pawn.jobs.curJob.count + ", setting to 1. Job was " + pawn.jobs.curJob);
                    pawn.jobs.curJob.count = 1;
                }

                __result = false;
                return false;
            }
        }
    }
}
