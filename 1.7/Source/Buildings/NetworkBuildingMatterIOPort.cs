using System.Text;
using Verse;

namespace SK_Matter_Network
{
    public class NetworkBuildingMatterIOPort : NetworkBuilding
    {
        private static readonly StringBuilder sb = new StringBuilder();

        private GraphicData importGraphicData;
        private GraphicData exportGraphicData;

        private CompMatterIOPort PortComp => GetComp<CompMatterIOPort>();
        private Graphic ImportGraphic => GetOrCreateVariantGraphic(ref importGraphicData, "_Import");
        private Graphic ExportGraphic => GetOrCreateVariantGraphic(ref exportGraphicData, "_Export");

        public override Graphic Graphic
        {
            get
            {
                DataNetwork network = ParentNetwork;
                if (!network.IsOperational)
                {
                    return base.Graphic;
                }

                CompMatterIOPort comp = PortComp;
                return comp.Mode == MatterIOPortMode.Input ? ImportGraphic : ExportGraphic;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            NotifyVisualStateChanged();
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            PortComp.DrawSelectionOverlay();
        }

        public override string GetInspectString()
        {
            sb.Clear();
            sb.Append(base.GetInspectString());

            CompMatterIOPort comp = PortComp;
            sb.AppendLineIfNotEmpty();
            sb.Append(comp.StatusInspectString());

            return sb.ToString();
        }

        public void NotifyVisualStateChanged()
        {
            if (Spawned && Map != null)
            {
                DirtyMapMesh(Map);
            }
        }

        private Graphic GetOrCreateVariantGraphic(ref GraphicData graphicData, string suffix)
        {
            if (graphicData == null)
            {
                graphicData = new GraphicData();
                graphicData.CopyFrom(def.graphicData);
                graphicData.texPath = def.graphicData.texPath + suffix;
            }

            return graphicData.GraphicColoredFor(this);
        }
    }
}
