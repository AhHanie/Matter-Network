using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    internal sealed class NetworkStorageOverviewDrawer
    {
        private readonly NetworkStorageChromeDrawer chromeDrawer;
        private readonly NetworkStorageTabDataSource dataSource;

        internal NetworkStorageOverviewDrawer(NetworkStorageChromeDrawer chromeDrawer, NetworkStorageTabDataSource dataSource)
        {
            this.chromeDrawer = chromeDrawer;
            this.dataSource = dataSource;
        }

        internal void Draw(Rect rect, DataNetwork network, NetworkStorageTabDataSnapshot snapshot, NetworkStorageTabState state)
        {
            const float infoPanelHeight = 134f;
            const float topDefsHeaderOffset = 28f;
            const float topDefsRowsOffset = 48f;

            int controllerCount = network.Buildings.OfType<NetworkBuildingController>().Count();
            int interfaceCount = network.Buildings.OfType<NetworkBuildingNetworkInterface>().Count();
            int diskDriveCount = network.Buildings.OfType<NetworkBuildingDiskDrive>().Count();
            float usagePercent = network.TotalCapacityBytes > 0 ? (float)network.UsedBytes / network.TotalCapacityBytes : 0f;

            float contentHeight = 300f + Mathf.Max(0, Mathf.Min(6, snapshot.GroupedEntries.Count) * 30f);
            Rect outRect = rect;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, contentHeight));
            Widgets.BeginScrollView(outRect, ref state.OverviewScrollPosition, viewRect);

            float cardWidth = (viewRect.width - (NetworkStorageUiConstants.SummaryGap * 4f)) / 5f;
            chromeDrawer.DrawSummaryCard(new Rect(0f, 0f, cardWidth, NetworkStorageUiConstants.SummaryCardHeight), "MN_NetworkStorageOverviewUsedBytes".Translate(), dataSource.FormatItemCount(network.UsedBytes), NetworkStorageUiConstants.SecondaryTextColor, null);
            chromeDrawer.DrawSummaryCard(new Rect(cardWidth + NetworkStorageUiConstants.SummaryGap, 0f, cardWidth, NetworkStorageUiConstants.SummaryCardHeight), "MN_NetworkStorageOverviewCapacity".Translate(), dataSource.FormatItemCount(network.TotalCapacityBytes), NetworkStorageUiConstants.SecondaryTextColor, null);
            chromeDrawer.DrawSummaryCard(new Rect((cardWidth + NetworkStorageUiConstants.SummaryGap) * 2f, 0f, cardWidth, NetworkStorageUiConstants.SummaryCardHeight), "MN_NetworkStorageOverviewUtilization".Translate(), network.TotalCapacityBytes > 0 ? usagePercent.ToStringPercent("0") : "0%", NetworkStorageUiConstants.AccentColor, usagePercent);
            chromeDrawer.DrawSummaryCard(new Rect((cardWidth + NetworkStorageUiConstants.SummaryGap) * 3f, 0f, cardWidth, NetworkStorageUiConstants.SummaryCardHeight), "MN_NetworkStorageOverviewStoredStacks".Translate(), snapshot.StoredStackCount.ToString(), NetworkStorageUiConstants.SecondaryTextColor, null);
            chromeDrawer.DrawSummaryCard(new Rect((cardWidth + NetworkStorageUiConstants.SummaryGap) * 4f, 0f, cardWidth, NetworkStorageUiConstants.SummaryCardHeight), "MN_NetworkStorageOverviewUniqueDefs".Translate(), snapshot.UniqueDefCount.ToString(), NetworkStorageUiConstants.SecondaryTextColor, null);

            float sectionY = NetworkStorageUiConstants.SummaryCardHeight + 14f;
            float leftWidth = (viewRect.width - NetworkStorageUiConstants.PanelGap) * 0.52f;
            float rightWidth = viewRect.width - leftWidth - NetworkStorageUiConstants.PanelGap;

            Rect compositionRect = new Rect(0f, sectionY, leftWidth, infoPanelHeight);
            Rect statusRect = new Rect(compositionRect.xMax + NetworkStorageUiConstants.PanelGap, sectionY, rightWidth, infoPanelHeight);
            Rect highlightsRect = new Rect(0f, compositionRect.yMax + NetworkStorageUiConstants.PanelGap, viewRect.width, viewRect.height - compositionRect.yMax - NetworkStorageUiConstants.PanelGap);

            chromeDrawer.DrawLabeledSection(compositionRect, "MN_NetworkStorageOverviewNetworkComposition".Translate());
            Rect compositionInner = compositionRect.ContractedBy(NetworkStorageUiConstants.SectionPadding);
            chromeDrawer.DrawKeyValueRow(new Rect(compositionInner.x, compositionInner.y + 26f, compositionInner.width, 22f), "MN_NetworkStorageOverviewControllers".Translate(), controllerCount.ToString());
            chromeDrawer.DrawKeyValueRow(new Rect(compositionInner.x, compositionInner.y + 48f, compositionInner.width, 22f), "MN_NetworkStorageOverviewInterfaces".Translate(), interfaceCount.ToString());
            chromeDrawer.DrawKeyValueRow(new Rect(compositionInner.x, compositionInner.y + 70f, compositionInner.width, 22f), "MN_NetworkStorageOverviewDiskDrives".Translate(), diskDriveCount.ToString());
            chromeDrawer.DrawKeyValueRow(new Rect(compositionInner.x, compositionInner.y + 92f, compositionInner.width, 22f), "MN_NetworkStorageOverviewBuildings".Translate(), network.BuildingCount.ToString());

            chromeDrawer.DrawLabeledSection(statusRect, "MN_NetworkStorageOverviewStatus".Translate());
            Rect statusInner = statusRect.ContractedBy(NetworkStorageUiConstants.SectionPadding);
            string controllerStatus = network.HasActiveController ? "MN_NetworkStorageStatusOnline".Translate().ToString() : "MN_NetworkStorageNoController".Translate().ToString();
            string extractionStatus = network.CanExtractItems ? "MN_NetworkStorageOverviewExtractionAvailable".Translate().ToString() : "MN_NetworkStorageOverviewExtractionUnavailable".Translate().ToString();
            chromeDrawer.DrawKeyValueRow(new Rect(statusInner.x, statusInner.y + 26f, statusInner.width, 22f), "MN_NetworkStorageOverviewControllerState".Translate(), controllerStatus, network.HasActiveController ? NetworkStorageUiConstants.OkColor : NetworkStorageUiConstants.WarningColor);
            chromeDrawer.DrawKeyValueRow(new Rect(statusInner.x, statusInner.y + 48f, statusInner.width, 22f), "MN_NetworkStorageOverviewExtraction".Translate(), extractionStatus, network.CanExtractItems ? NetworkStorageUiConstants.OkColor : NetworkStorageUiConstants.MutedTextColor);
            chromeDrawer.DrawKeyValueRow(new Rect(statusInner.x, statusInner.y + 70f, statusInner.width, 22f), "MN_NetworkStorageOverviewStoredUnits".Translate(), dataSource.FormatItemCount(snapshot.TotalUnits));
            chromeDrawer.DrawKeyValueRow(new Rect(statusInner.x, statusInner.y + 92f, statusInner.width, 22f), "MN_NetworkStorageOverviewOvercommitted".Translate(), network.OvercommittedBytes > 0 ? dataSource.FormatItemCount(network.OvercommittedBytes) : "0", network.OvercommittedBytes > 0 ? NetworkStorageUiConstants.ErrorColor : NetworkStorageUiConstants.SecondaryTextColor);

            chromeDrawer.DrawLabeledSection(highlightsRect, "MN_NetworkStorageOverviewTopDefs".Translate());
            Rect highlightsInner = highlightsRect.ContractedBy(NetworkStorageUiConstants.SectionPadding);
            if (!network.HasActiveController)
            {
                chromeDrawer.DrawEmptySectionMessage(highlightsInner, "MN_NetworkStorageNoController".Translate());
            }
            else if (snapshot.GroupedEntries.Count == 0)
            {
                chromeDrawer.DrawEmptySectionMessage(highlightsInner, "MN_NetworkStorageNoStoredItems".Translate());
            }
            else
            {
                DrawTopDefHeaders(new Rect(highlightsInner.x, highlightsInner.y + topDefsHeaderOffset, highlightsInner.width, 18f));
                int rows = Mathf.Min(6, snapshot.GroupedEntries.Count);
                for (int i = 0; i < rows; i++)
                {
                    DrawTopDefRow(new Rect(highlightsInner.x, highlightsInner.y + topDefsRowsOffset + (i * 30f), highlightsInner.width, 24f), snapshot.GroupedEntries[i]);
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawTopDefHeaders(Rect rect)
        {
            Rect stackHeaderRect = new Rect(rect.xMax - 122f, rect.y, 54f, rect.height);
            Rect totalHeaderRect = new Rect(rect.xMax - 64f, rect.y, 64f, rect.height);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(stackHeaderRect, "MN_NetworkStorageOverviewTopDefsHeaderStacks".Translate());
            Widgets.Label(totalHeaderRect, "MN_NetworkStorageOverviewTopDefsHeaderTotal".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawTopDefRow(Rect rect, GroupedItemEntry entry)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            Rect iconRect = new Rect(rect.x, rect.y, 20f, 20f);
            Rect labelRect = new Rect(iconRect.xMax + 6f, rect.y, rect.width - 126f, rect.height);
            Rect stackRect = new Rect(rect.xMax - 122f, rect.y, 54f, rect.height);
            Rect countRect = new Rect(rect.xMax - 64f, rect.y, 64f, rect.height);

            chromeDrawer.DrawThingDefIcon(iconRect, entry.Def);

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(labelRect, entry.Def.LabelCap.Truncate(labelRect.width));

            Text.Anchor = TextAnchor.MiddleRight;
            Text.Font = GameFont.Tiny;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(stackRect, entry.StackEntries.ToString());

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.AccentColor;
            Widgets.Label(countRect, dataSource.FormatItemCount(entry.TotalCount));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }
}
