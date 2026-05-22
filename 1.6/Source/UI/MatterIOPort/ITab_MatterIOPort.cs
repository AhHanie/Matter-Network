using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public class ITab_MatterIOPort : ITab
    {
        private const int TickStep = 60;

        private CompMatterIOPort SelectedComp => SelThing?.TryGetComp<CompMatterIOPort>();

        public ITab_MatterIOPort()
        {
            size = new Vector2(420f, 260f);
            labelKey = "MN_MatterIOPortTab";
        }

        protected override void FillTab()
        {
            CompMatterIOPort comp = SelectedComp;
            Rect rect = new Rect(10f, 10f, size.x - 20f, size.y - 20f);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);

            listing.Label("MN_MatterIOPortMode".Translate(comp.ModeLabel));
            DrawModeButtons(listing, comp);
            listing.Gap();

            listing.Label("MN_MatterIOPortDirection".Translate(comp.DirectionLabel));
            DrawDirectionButtons(listing, comp);
            listing.Gap();

            int seconds = comp.TickInterval / 60;
            listing.Label("MN_MatterIOPortInterval".Translate(comp.TickInterval, seconds));
            float sliderValue = listing.Slider(comp.TickInterval, comp.Props.minTickInterval, comp.Props.maxTickInterval);
            int roundedValue = Mathf.RoundToInt(sliderValue / TickStep) * TickStep;
            comp.SetTickInterval(roundedValue);
            listing.Gap();

            listing.Label("MN_MatterIOPortTargetCell".Translate(comp.TargetCell));
            if (comp.TryResolveAdapter(out IMatterInventoryAdapter adapter))
            {
                listing.Label("MN_MatterIOPortTarget".Translate(adapter.Label));
                listing.Label("MN_MatterIOPortAdapterStatus".Translate(adapter.GetStatus()));
            }
            else
            {
                listing.Label("MN_MatterIOPortNoCompatibleTarget".Translate());
            }

            listing.Label("MN_MatterIOPortLastStatus".Translate(comp.LastStatus));
            listing.End();
        }

        private void DrawModeButtons(Listing_Standard listing, CompMatterIOPort comp)
        {
            Rect row = listing.GetRect(32f);
            float width = (row.width - 8f) / 2f;
            Rect inputRect = new Rect(row.x, row.y, width, row.height);
            Rect outputRect = new Rect(inputRect.xMax + 8f, row.y, width, row.height);

            if (Widgets.ButtonText(inputRect, "MN_MatterIOPortModeInput".Translate()))
            {
                comp.SetMode(MatterIOPortMode.Input);
            }

            if (Widgets.ButtonText(outputRect, "MN_MatterIOPortModeOutput".Translate()))
            {
                comp.SetMode(MatterIOPortMode.Output);
            }

            if (comp.Mode == MatterIOPortMode.Input)
            {
                Widgets.DrawHighlightSelected(inputRect);
            }
            else
            {
                Widgets.DrawHighlightSelected(outputRect);
            }
        }

        private void DrawDirectionButtons(Listing_Standard listing, CompMatterIOPort comp)
        {
            Rect row = listing.GetRect(32f);
            float width = (row.width - 24f) / 4f;
            DrawDirectionButton(new Rect(row.x, row.y, width, row.height), comp, Rot4.North, "North".Translate());
            DrawDirectionButton(new Rect(row.x + (width + 8f), row.y, width, row.height), comp, Rot4.East, "East".Translate());
            DrawDirectionButton(new Rect(row.x + (width + 8f) * 2f, row.y, width, row.height), comp, Rot4.South, "South".Translate());
            DrawDirectionButton(new Rect(row.x + (width + 8f) * 3f, row.y, width, row.height), comp, Rot4.West, "West".Translate());
        }

        private void DrawDirectionButton(Rect rect, CompMatterIOPort comp, Rot4 direction, string label)
        {
            if (Widgets.ButtonText(rect, label))
            {
                comp.SetDirection(direction);
            }

            if (comp.Direction == direction)
            {
                Widgets.DrawHighlightSelected(rect);
            }
        }
    }
}
