using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public static class ModSettingsWindow
    {
        private const float RowHeight = 36f;
        private const float HeaderHeight = 34f;
        private const float SectionTitleHeight = 40f;
        private const float SectionDescriptionHeight = 48f;
        private const float ButtonWidth = 86f;
        private const float ValueWidth = 76f;
        private const float DefaultWidth = 88f;

        private static Vector2 powerUsageScrollPosition;

        public static void Draw(Rect parent)
        {
            Rect topRect = new Rect(parent.x, parent.y, parent.width, 82f);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(topRect);
            listing.CheckboxLabeled(
                "MN_SettingsEnableLoggingLabel".Translate(),
                ref ModSettings.EnableLogging,
                "MN_SettingsEnableLoggingDescription".Translate());
            listing.GapLine();
            listing.CheckboxLabeled(
                "MN_SettingsDisableNetworkItemsForWealthLabel".Translate(),
                ref ModSettings.DisableNetworkItemsForWealth,
                "MN_SettingsDisableNetworkItemsForWealthDescription".Translate());
            float listedHeight = listing.CurHeight;
            listing.End();

            float topAreaHeight = DrawStoredItemPowerSetting(parent, parent.y + listedHeight + 4f);

            Rect powerRect = new Rect(parent.x, parent.y + topAreaHeight + 12f, parent.width, parent.height - topAreaHeight - 12f);
            DrawNetworkBuildingPowerUsageOverrides(powerRect);
        }

        private static float DrawStoredItemPowerSetting(Rect parent, float y)
        {
            bool enabled = ModSettings.EnableStoredItemPowerDraw;
            Rect checkboxRect = new Rect(parent.x, y, parent.width, 24f);
            Widgets.CheckboxLabeled(
                checkboxRect,
                "MN_SettingsEnableStoredItemPowerDrawLabel".Translate(),
                ref ModSettings.EnableStoredItemPowerDraw);
            TooltipHandler.TipRegion(checkboxRect, "MN_SettingsEnableStoredItemPowerDrawDescription".Translate());

            if (enabled != ModSettings.EnableStoredItemPowerDraw)
            {
                ModSettings.NotifyStoredItemPowerDrawSettingsChanged();
            }

            if (!ModSettings.EnableStoredItemPowerDraw)
            {
                return checkboxRect.yMax - parent.y;
            }

            Rect rowRect = new Rect(parent.x, checkboxRect.yMax + 8f, parent.width, 28f);
            Rect labelRect = new Rect(rowRect.x + 24f, rowRect.y + 4f, 260f, 24f);
            Rect valueRect = new Rect(rowRect.xMax - 80f, rowRect.y + 4f, 80f, 24f);
            Rect sliderRect = new Rect(labelRect.xMax + 10f, rowRect.y + 6f, Mathf.Max(120f, valueRect.x - labelRect.xMax - 20f), 20f);

            Text.Font = GameFont.Small;
            Widgets.Label(labelRect, "MN_SettingsStoredItemPowerDrawPer100Bytes".Translate());

            float sliderValue = Widgets.HorizontalSlider(
                sliderRect,
                Mathf.Clamp(ModSettings.StoredItemPowerDrawPer100Bytes, ModSettings.MinStoredItemPowerDrawPer100Bytes, ModSettings.MaxStoredItemPowerDrawPer100Bytes),
                ModSettings.MinStoredItemPowerDrawPer100Bytes,
                ModSettings.MaxStoredItemPowerDrawPer100Bytes,
                true);
            int roundedValue = ModSettings.ClampAndRoundStoredItemPowerDraw(Mathf.RoundToInt(sliderValue));
            if (roundedValue != ModSettings.StoredItemPowerDrawPer100Bytes)
            {
                ModSettings.SetStoredItemPowerDrawPer100Bytes(roundedValue);
            }

            Widgets.Label(valueRect, ModSettings.StoredItemPowerDrawPer100Bytes + " W");
            return rowRect.yMax - parent.y;
        }

        private static void DrawNetworkBuildingPowerUsageOverrides(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y + 4f, rect.width, SectionTitleHeight - 4f), "MN_SettingsNetworkPowerUsageTitle".Translate());

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, rect.y + SectionTitleHeight, rect.width, SectionDescriptionHeight), "MN_SettingsNetworkPowerUsageDescription".Translate());

            float listY = rect.y + SectionTitleHeight + SectionDescriptionHeight + 8f;
            Rect listOuterRect = new Rect(rect.x, listY, rect.width, rect.yMax - listY);
            Widgets.DrawBoxSolidWithOutline(listOuterRect, Color.clear, new Color(0.30f, 0.35f, 0.44f));

            List<ThingDef> defs = ModSettings.NetworkBuildingDefs.ToList();

            float contentHeight = HeaderHeight + (defs.Count * RowHeight) + 8f;
            Rect viewRect = new Rect(0f, 0f, listOuterRect.width - 16f, Mathf.Max(listOuterRect.height, contentHeight));
            Widgets.BeginScrollView(listOuterRect.ContractedBy(4f), ref powerUsageScrollPosition, viewRect);

            DrawPowerUsageHeader(new Rect(0f, 0f, viewRect.width, HeaderHeight));

            float curY = HeaderHeight;
            for (int i = 0; i < defs.Count; i++)
            {
                DrawPowerUsageRow(new Rect(0f, curY, viewRect.width, RowHeight), defs[i]);
                curY += RowHeight;
            }

            Widgets.EndScrollView();
        }

        private static void DrawPowerUsageHeader(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 10f, rect.width - 260f, 22f), "MN_SettingsNetworkPowerUsageBuilding".Translate());
            Widgets.Label(new Rect(rect.xMax - ValueWidth - ButtonWidth - DefaultWidth - 36f, rect.y + 10f, DefaultWidth, 22f), "MN_SettingsNetworkPowerUsageDefault".Translate());
            Widgets.Label(new Rect(rect.xMax - ValueWidth - ButtonWidth - 24f, rect.y + 10f, ValueWidth, 22f), "MN_SettingsNetworkPowerUsageCurrent".Translate());
            GUI.color = Color.white;
        }

        private static void DrawPowerUsageRow(Rect rect, ThingDef def)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            int defaultPowerUsage = ModSettings.GetDefaultNetworkBuildingPowerUsage(def);
            int currentPowerUsage = ModSettings.GetEffectiveNetworkBuildingPowerUsage(def);
            bool overridden = ModSettings.IsNetworkBuildingPowerUsageOverridden(def);

            Rect buttonRect = new Rect(rect.xMax - ButtonWidth - 6f, rect.y + 6f, ButtonWidth, 26f);
            Rect valueRect = new Rect(buttonRect.x - ValueWidth - 8f, rect.y + 8f, ValueWidth, 26f);
            Rect defaultRect = new Rect(valueRect.x - DefaultWidth - 8f, rect.y + 8f, DefaultWidth, 26f);
            Rect labelRect = new Rect(rect.x + 6f, rect.y + 8f, Mathf.Max(180f, rect.width - 450f), 26f);
            Rect sliderRect = new Rect(labelRect.xMax + 8f, rect.y + 11f, Mathf.Max(80f, defaultRect.x - labelRect.xMax - 16f), 18f);

            Text.Font = GameFont.Small;
            GUI.color = overridden ? Color.white : new Color(0.78f, 0.78f, 0.78f);
            Widgets.Label(labelRect, def.LabelCap + " (" + def.defName + ")");

            GUI.color = Color.gray;
            Widgets.Label(defaultRect, defaultPowerUsage + " W");

            float sliderValue = Widgets.HorizontalSlider(
                sliderRect,
                Mathf.Clamp(currentPowerUsage, ModSettings.MinNetworkBuildingPowerUsage, ModSettings.MaxNetworkBuildingPowerUsage),
                ModSettings.MinNetworkBuildingPowerUsage,
                ModSettings.MaxNetworkBuildingPowerUsage,
                true);
            int roundedValue = ModSettings.ClampAndRoundPowerUsage(Mathf.RoundToInt(sliderValue));
            if (roundedValue != currentPowerUsage)
            {
                ModSettings.SetNetworkBuildingPowerUsageOverride(def, roundedValue);
            }

            GUI.color = overridden ? Color.white : Color.gray;
            Widgets.Label(valueRect, currentPowerUsage + " W");

            GUI.color = Color.white;
            if (Widgets.ButtonText(buttonRect, overridden ? "MN_SettingsNetworkPowerUsageReset".Translate() : "MN_SettingsNetworkPowerUsageSet".Translate()))
            {
                if (overridden)
                {
                    ModSettings.ClearNetworkBuildingPowerUsageOverride(def);
                }
                else
                {
                    ModSettings.SetNetworkBuildingPowerUsageOverride(def, currentPowerUsage);
                }
            }
        }

    }
}
