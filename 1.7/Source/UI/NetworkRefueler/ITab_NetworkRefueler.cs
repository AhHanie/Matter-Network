using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public class ITab_NetworkRefueler : ITab
    {
        private const int TickStep = 60;

        private CompNetworkRefueler SelectedComp => SelThing?.TryGetComp<CompNetworkRefueler>();

        public ITab_NetworkRefueler()
        {
            size = new Vector2(360f, 120f);
            labelKey = "MN_NetworkRefuelerTab";
        }

        protected override void FillTab()
        {
            CompNetworkRefueler comp = SelectedComp;

            Rect rect = new Rect(10f, 10f, size.x - 20f, size.y - 20f);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            int seconds = comp.TickInterval / 60;
            listing.Label("MN_NetworkRefuelerInterval".Translate(comp.TickInterval, seconds));

            float sliderValue = listing.Slider(comp.TickInterval, comp.Props.minTickInterval, comp.Props.maxTickInterval);
            int roundedValue = Mathf.RoundToInt(sliderValue / TickStep) * TickStep;
            comp.TickInterval = roundedValue;

            listing.End();
        }
    }
}
