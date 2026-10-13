using Verse;

namespace SK_Matter_Network
{
    public class Graphic_LinkedNetworkOverlay : Graphic_Linked
    {
        public Graphic_LinkedNetworkOverlay(Graphic subGraphic) : base(subGraphic) { }

        public override bool ShouldLinkWith(IntVec3 c, Thing parent)
        {
            if (!parent.Spawned)
                return false;
            if (!c.InBounds(parent.Map))
                return (parent.def.graphicData.linkFlags & LinkFlags.MapEdge) != 0;
            return (parent.Map.linkGrid.LinkFlagsAt(c) & parent.def.graphicData.linkFlags) != 0;
        }
    }
}
