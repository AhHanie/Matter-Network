using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    internal sealed class NetworkStorageItemsDrawer
    {
        private readonly NetworkStorageChromeDrawer chromeDrawer;
        private readonly NetworkStorageTabDataSource dataSource;
        private readonly NetworkStorageTabActions actions;

        internal NetworkStorageItemsDrawer(NetworkStorageChromeDrawer chromeDrawer, NetworkStorageTabDataSource dataSource, NetworkStorageTabActions actions)
        {
            this.chromeDrawer = chromeDrawer;
            this.dataSource = dataSource;
            this.actions = actions;
        }

        internal void DrawByDefTab(Rect rect, DataNetwork network, NetworkStorageTabDataSnapshot snapshot, NetworkStorageTabState state, NetworkBuildingNetworkInterface selectedInterface)
        {
            List<GroupedItemEntry> filteredItems = dataSource.FilterGroupedEntries(snapshot, state.ByDefSearchText);
            int filteredTotal = filteredItems.Sum(entry => entry.TotalCount);

            Rect toolbarRect = new Rect(rect.x, rect.y, rect.width, NetworkStorageUiConstants.ToolbarHeight);
            Rect contentRect = new Rect(rect.x, toolbarRect.yMax + 8f, rect.width, rect.height - NetworkStorageUiConstants.ToolbarHeight - 8f);

            chromeDrawer.DrawSearchToolbar(
                toolbarRect,
                "MN_NetworkStorageSearchLabel".Translate(),
                state.ByDefSearchText,
                "MN_NetworkStorageByDefSummary".Translate(filteredItems.Count, dataSource.FormatItemCount(filteredTotal)).ToString(),
                value => state.ByDefSearchText = value,
                () => state.ByDefScrollPosition = Vector2.zero);

            chromeDrawer.DrawPanel(contentRect, strong: false);
            Rect innerRect = contentRect.ContractedBy(NetworkStorageUiConstants.SectionPadding);

            if (!network.HasActiveController)
            {
                chromeDrawer.DrawCenteredMessage(innerRect, "MN_NetworkStorageNoController".Translate(), GameFont.Small, NetworkStorageUiConstants.SecondaryTextColor);
                return;
            }

            if (!network.IsOperational)
            {
                chromeDrawer.DrawCenteredMessage(innerRect, "MN_NetworkStorageOffline".Translate(network.PowerModeLabel), GameFont.Small, NetworkStorageUiConstants.WarningColor);
                return;
            }

            if (filteredItems.Count == 0)
            {
                chromeDrawer.DrawCenteredMessage(innerRect, string.IsNullOrWhiteSpace(state.ByDefSearchText) ? "MN_NetworkStorageNoStoredItems".Translate() : "MN_NetworkStorageNoItemsFiltered".Translate(), GameFont.Small, NetworkStorageUiConstants.SecondaryTextColor);
                return;
            }

            int columns = Mathf.Max(1, Mathf.FloorToInt((innerRect.width + NetworkStorageUiConstants.CardGap) / (NetworkStorageUiConstants.CardWidth + NetworkStorageUiConstants.CardGap)));
            int rows = Mathf.CeilToInt(filteredItems.Count / (float)columns);
            float contentHeight = Mathf.Max(innerRect.height - 4f, rows * (NetworkStorageUiConstants.CardHeight + NetworkStorageUiConstants.CardGap) - NetworkStorageUiConstants.CardGap);
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(innerRect, ref state.ByDefScrollPosition, viewRect);
            for (int i = 0; i < filteredItems.Count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                Rect cardRect = new Rect(column * (NetworkStorageUiConstants.CardWidth + NetworkStorageUiConstants.CardGap), row * (NetworkStorageUiConstants.CardHeight + NetworkStorageUiConstants.CardGap), NetworkStorageUiConstants.CardWidth, NetworkStorageUiConstants.CardHeight);
                DrawGroupedItemCard(cardRect, filteredItems[i], selectedInterface);
            }
            Widgets.EndScrollView();
        }

        internal void DrawByStackTab(Rect rect, DataNetwork network, NetworkStorageTabDataSnapshot snapshot, NetworkStorageTabState state, NetworkBuildingNetworkInterface selectedInterface)
        {
            List<StoredThingEntry> filteredItems = dataSource.FilterStoredEntries(snapshot, state.ByStackSearchText);

            Rect toolbarRect = new Rect(rect.x, rect.y, rect.width, NetworkStorageUiConstants.ToolbarHeight);
            Rect contentRect = new Rect(rect.x, toolbarRect.yMax + 8f, rect.width, rect.height - NetworkStorageUiConstants.ToolbarHeight - 8f);

            chromeDrawer.DrawSearchToolbar(
                toolbarRect,
                "MN_NetworkStorageSearchLabel".Translate(),
                state.ByStackSearchText,
                "MN_NetworkStorageByStackSummary".Translate(filteredItems.Count).ToString(),
                value => state.ByStackSearchText = value,
                () => state.ByStackScrollPosition = Vector2.zero);

            chromeDrawer.DrawPanel(contentRect, strong: false);
            Rect innerRect = contentRect.ContractedBy(NetworkStorageUiConstants.SectionPadding);

            if (!network.HasActiveController)
            {
                chromeDrawer.DrawCenteredMessage(innerRect, "MN_NetworkStorageNoController".Translate(), GameFont.Small, NetworkStorageUiConstants.SecondaryTextColor);
                return;
            }

            if (!network.IsOperational)
            {
                chromeDrawer.DrawCenteredMessage(innerRect, "MN_NetworkStorageOffline".Translate(network.PowerModeLabel), GameFont.Small, NetworkStorageUiConstants.WarningColor);
                return;
            }

            if (filteredItems.Count == 0)
            {
                chromeDrawer.DrawCenteredMessage(innerRect, string.IsNullOrWhiteSpace(state.ByStackSearchText) ? "MN_NetworkStorageNoStoredStacks".Translate() : "MN_NetworkStorageNoItemsFiltered".Translate(), GameFont.Small, NetworkStorageUiConstants.SecondaryTextColor);
                return;
            }

            float contentHeight = Mathf.Max(innerRect.height - 4f, filteredItems.Count * (NetworkStorageUiConstants.StackRowHeight + NetworkStorageUiConstants.RowGap) - NetworkStorageUiConstants.RowGap);
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(innerRect, ref state.ByStackScrollPosition, viewRect);
            for (int i = 0; i < filteredItems.Count; i++)
            {
                Rect rowRect = new Rect(0f, i * (NetworkStorageUiConstants.StackRowHeight + NetworkStorageUiConstants.RowGap), viewRect.width, NetworkStorageUiConstants.StackRowHeight);
                DrawStoredThingRow(rowRect, filteredItems[i], selectedInterface);
            }
            Widgets.EndScrollView();
        }

        private void DrawGroupedItemCard(Rect rect, GroupedItemEntry entry, NetworkBuildingNetworkInterface selectedInterface)
        {
            chromeDrawer.DrawPanel(rect, strong: false);
            Widgets.DrawHighlightIfMouseover(rect);

            Rect innerRect = rect.ContractedBy(8f);
            Rect iconRect = new Rect(innerRect.x, innerRect.y + 2f, NetworkStorageUiConstants.DefIconSize, NetworkStorageUiConstants.DefIconSize);
            Rect infoRect = new Rect(iconRect.xMax + 8f, innerRect.y + 1f, innerRect.width - NetworkStorageUiConstants.DefIconSize - 8f, 42f);
            Rect countRect = new Rect(innerRect.x, iconRect.yMax + 8f, innerRect.width, 22f);
            Rect stackRect = new Rect(innerRect.x, countRect.yMax, innerRect.width, 16f);
            Rect buttonRect = new Rect(innerRect.x, rect.yMax - NetworkStorageUiConstants.DropButtonHeight - 8f, innerRect.width, NetworkStorageUiConstants.DropButtonHeight);

            chromeDrawer.DrawThingDefIcon(iconRect, entry.Def);

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(infoRect, entry.Def.LabelCap.Truncate(infoRect.width));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = NetworkStorageUiConstants.AccentColor;
            Widgets.Label(countRect, "MN_NetworkStorageTotalCount".Translate(dataSource.FormatItemCount(entry.TotalCount)));
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Tiny;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(stackRect, "MN_NetworkStorageStackEntries".Translate(entry.StackEntries));
            GUI.color = Color.white;

            if (Widgets.ButtonText(buttonRect, "MN_NetworkStorageDropLabel".Translate()))
            {
                actions.DropItemByDef(selectedInterface, entry.Def);
            }

            TooltipHandler.TipRegion(rect, "MN_NetworkStorageDropByDefTooltip".Translate(entry.Def.LabelCap, entry.StackEntries, dataSource.FormatItemCount(entry.TotalCount)));
        }

        private void DrawStoredThingRow(Rect rect, StoredThingEntry entry, NetworkBuildingNetworkInterface selectedInterface)
        {
            chromeDrawer.DrawPanel(rect, strong: false);
            Widgets.DrawHighlightIfMouseover(rect);

            Rect innerRect = rect.ContractedBy(8f);
            Rect iconRect = new Rect(innerRect.x, innerRect.y + ((innerRect.height - NetworkStorageUiConstants.ThingIconSize) / 2f), NetworkStorageUiConstants.ThingIconSize, NetworkStorageUiConstants.ThingIconSize);
            Rect buttonRect = new Rect(innerRect.xMax - 92f, innerRect.y + ((innerRect.height - NetworkStorageUiConstants.DropButtonHeight) / 2f), 92f, NetworkStorageUiConstants.DropButtonHeight);
            Rect countRect = new Rect(buttonRect.x - 94f, innerRect.y + 2f, 84f, innerRect.height - 4f);
            Rect textRect = new Rect(iconRect.xMax + 10f, innerRect.y + 2f, countRect.x - iconRect.xMax - 18f, innerRect.height - 4f);
            Rect nameRect = new Rect(textRect.x, textRect.y, textRect.width, 19f);
            Rect detailsRect = new Rect(textRect.x, nameRect.yMax + 1f, textRect.width, 18f);

            chromeDrawer.DrawThingIcon(iconRect, entry.Thing);

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(nameRect, entry.Label.Truncate(nameRect.width));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(detailsRect, dataSource.BuildThingMetadata(entry.Thing).Truncate(detailsRect.width));
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = NetworkStorageUiConstants.AccentColor;
            Widgets.Label(countRect, dataSource.FormatItemCount(entry.StackCount));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            if (Widgets.ButtonText(buttonRect, "MN_NetworkStorageDropLabel".Translate()))
            {
                actions.DropStoredThing(selectedInterface, entry.Thing);
            }

            TooltipHandler.TipRegion(rect, "MN_NetworkStorageDropStackTooltip".Translate(entry.Label, dataSource.FormatItemCount(entry.StackCount)));
        }
    }
}
