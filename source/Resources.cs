using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    [StaticConstructorOnStartup]
    public static class Resources
    {
        public static readonly Graphic LinkedOverlayGraphic;

        static Resources()
        {
            Graphic atlasGraphic = GraphicDatabase.Get<Graphic_Single>(
                "Things/Special/DataNetwork/TransmitterAtlas",
                ShaderDatabase.MetaOverlay,
                Vector2.one,
                new Color(0f, 1f, 0f, 0.4f)
            );

            LinkedOverlayGraphic = GraphicUtility.WrapLinked(
                atlasGraphic,
                LinkDrawerType.Basic
            );

            atlasGraphic.MatSingle.renderQueue = 3600;
        }
    }
}