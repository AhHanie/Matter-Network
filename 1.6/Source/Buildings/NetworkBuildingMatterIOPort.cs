using System.Text;
using Verse;

namespace SK_Matter_Network
{
    public class NetworkBuildingMatterIOPort : NetworkBuilding
    {
        private static readonly StringBuilder sb = new StringBuilder();

        private CompMatterIOPort PortComp => GetComp<CompMatterIOPort>();

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
    }
}
