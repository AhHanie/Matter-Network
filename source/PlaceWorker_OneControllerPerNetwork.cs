using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class PlaceWorker_OneControllerPerNetwork : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            bool newBuildingIsController = checkingDef == BuildingDefOf.MN_NetworkController;

            HashSet<NetworkBuilding> visited = new HashSet<NetworkBuilding>();
            Queue<NetworkBuilding> queue = new Queue<NetworkBuilding>();
            int controllerCount = newBuildingIsController ? 1 : 0;

            foreach (IntVec3 adj in GenAdj.CellsAdjacentCardinal(loc, rot, checkingDef.Size))
            {
                if (!adj.InBounds(map)) continue;
                foreach (Thing t in map.thingGrid.ThingsListAt(adj))
                {
                    if (t is NetworkBuilding nb && t != thingToIgnore && !visited.Contains(nb))
                    {
                        visited.Add(nb);
                        queue.Enqueue(nb);
                    }
                }
            }

            while (queue.Count > 0)
            {
                NetworkBuilding current = queue.Dequeue();

                if (current is NetworkBuildingController ctrl && !ctrl.ControllerConflictDisabled)
                {
                    controllerCount++;
                    if (controllerCount > 1)
                    {
                        return new AcceptanceReport("MN_PlaceWorker_TooManyControllers".Translate());
                    }
                }

                foreach (IntVec3 adj in GenAdj.CellsAdjacentCardinal(current))
                {
                    if (!adj.InBounds(map)) continue;
                    foreach (Thing t in map.thingGrid.ThingsListAt(adj))
                    {
                        if (t is NetworkBuilding nb && t != thingToIgnore && !visited.Contains(nb))
                        {
                            visited.Add(nb);
                            queue.Enqueue(nb);
                        }
                    }
                }
            }

            return AcceptanceReport.WasAccepted;
        }
    }
}
