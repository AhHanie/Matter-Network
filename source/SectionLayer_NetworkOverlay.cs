using RimWorld;
using Verse;
using System.Collections.Generic;

namespace SK_Matter_Network
{
    public class SectionLayer_NetworkOverlay : SectionLayer
    {
        private static readonly HashSet<ThingDef> networkBuildings = new HashSet<ThingDef>() { BuildingDefOf.MN_DiskDrive, BuildingDefOf.MN_NetworkInterface, BuildingDefOf.MN_NetworkController, BuildingDefOf.MN_NetworkCable };
        public SectionLayer_NetworkOverlay(Section section) : base(section)
        {
            relevantChangeTypes = MapMeshFlagDefOf.Buildings;
        }

        public override void DrawLayer()
        {
            Designator_Build designator_Build = Find.DesignatorManager.SelectedDesignator as Designator_Build;
            if (designator_Build != null)
            {
                ThingDef thingDef = designator_Build.PlacingDef as ThingDef;
                if (thingDef != null && networkBuildings.Contains(thingDef))
                {
                    base.DrawLayer();
                }
                return;
            }
        }

        public override void Regenerate()
        {
            ClearSubMeshes(MeshParts.All);

            NetworksMapComponent mapComponent = Map.GetComponent<NetworksMapComponent>();
            if (mapComponent != null)
            {
                List<Thing> thingsInSection = new List<Thing>();
                foreach (IntVec3 cell in section.CellRect)
                {
                    if (mapComponent.CellHasNetworkBuilding(cell))
                    {
                        List<Thing> thingsAtCell = Map.thingGrid.ThingsListAt(cell);
                        foreach (Thing thing in thingsAtCell)
                        {
                            if (thing.def == BuildingDefOf.MN_NetworkCable)
                            {
                                thingsInSection.Add(thing);
                            }
                        }
                    }
                }

                foreach (Thing thing in thingsInSection)
                {
                    Resources.LinkedOverlayGraphic.Print(this, thing, 0f);
                }
            }

            FinalizeMesh(MeshParts.All);
        }
    }
}