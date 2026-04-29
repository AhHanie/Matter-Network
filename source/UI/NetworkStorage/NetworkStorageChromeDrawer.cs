using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    internal sealed class NetworkStorageChromeDrawer
    {
        internal void DrawHeader(Rect rect, DataNetwork network, Func<int, string> formatItemCount)
        {
            DrawPanel(rect, strong: true);

            Rect innerRect = rect.ContractedBy(10f);
            Rect statusRect = new Rect(innerRect.xMax - 170f, innerRect.y + ((innerRect.height - NetworkStorageUiConstants.StatusPillHeight) / 2f), 170f, NetworkStorageUiConstants.StatusPillHeight);
            Rect titleRect = new Rect(innerRect.x, innerRect.y + 3f, innerRect.width * 0.52f, 32f);
            Rect subtitleRect = new Rect(innerRect.x, titleRect.yMax, innerRect.width * 0.64f, 20f);
            Rect summaryRect = new Rect(statusRect.x - 190f, statusRect.y, 180f, statusRect.height);

            Text.Font = GameFont.Medium;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(titleRect, "MN_NetworkStorageHeaderTitle".Translate());

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(subtitleRect, "MN_NetworkStorageHeaderNetworkId".Translate(network.NetworkId));

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
            Widgets.Label(summaryRect, "MN_NetworkStorageHeaderSummary".Translate(formatItemCount(network.UsedBytes), formatItemCount(network.TotalCapacityBytes)));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            DrawStatusPill(statusRect, network);
            DrawLine(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), NetworkStorageUiConstants.OutlineColor);
        }

        internal NetworkStorageSubTab DrawTabStrip(Rect tabContentRect, NetworkStorageSubTab selectedSubTab)
        {
            NetworkStorageSubTab currentTab = selectedSubTab;
            List<TabRecord> tabs = new List<TabRecord>
            {
                new TabRecord("MN_NetworkStorageSubTabOverview".Translate(), delegate { currentTab = NetworkStorageSubTab.Overview; }, selectedSubTab == NetworkStorageSubTab.Overview),
                new TabRecord("MN_NetworkStorageSubTabPower".Translate(), delegate { currentTab = NetworkStorageSubTab.Power; }, selectedSubTab == NetworkStorageSubTab.Power),
                new TabRecord("MN_NetworkStorageSubTabByDef".Translate(), delegate { currentTab = NetworkStorageSubTab.ByDef; }, selectedSubTab == NetworkStorageSubTab.ByDef),
                new TabRecord("MN_NetworkStorageSubTabByStack".Translate(), delegate { currentTab = NetworkStorageSubTab.ByStack; }, selectedSubTab == NetworkStorageSubTab.ByStack)
            };

            TabDrawer.DrawTabs(tabContentRect, tabs, 180f);
            return currentTab;
        }

        internal void DrawSummaryCard(Rect rect, string label, string value, Color accent, float? fillPercent)
        {
            DrawPanel(rect, strong: false);
            Rect innerRect = rect.ContractedBy(10f);

            Text.Font = GameFont.Tiny;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 18f), label);

            Text.Font = GameFont.Medium;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(new Rect(innerRect.x, innerRect.y + 18f, innerRect.width, 28f), value);

            if (fillPercent.HasValue)
            {
                Rect barRect = new Rect(innerRect.x, rect.yMax - 20f, innerRect.width, 12f);
                Color previousColor = GUI.color;
                GUI.color = accent;
                Widgets.FillableBar(barRect, Mathf.Clamp01(fillPercent.Value));
                GUI.color = previousColor;
            }

            GUI.color = Color.white;
        }

        internal void DrawLabeledSection(Rect rect, string label)
        {
            DrawPanel(rect, strong: false);

            Rect labelRect = new Rect(rect.x + NetworkStorageUiConstants.SectionPadding, rect.y + 5f, rect.width - (NetworkStorageUiConstants.SectionPadding * 2f), 22f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            DrawLine(
                new Rect(rect.x + NetworkStorageUiConstants.SectionPadding, labelRect.yMax + 2f, rect.width - (NetworkStorageUiConstants.SectionPadding * 2f), 1f),
                NetworkStorageUiConstants.OutlineColor);
        }

        internal void DrawKeyValueRow(Rect rect, string label, string value, Color? valueColor = null)
        {
            Rect labelRect = new Rect(rect.x, rect.y, rect.width * 0.62f, rect.height);
            Rect valueRect = new Rect(rect.x + rect.width * 0.62f, rect.y, rect.width * 0.38f, rect.height);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
            Widgets.Label(labelRect, label);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = valueColor ?? NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(valueRect, value);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        internal void DrawSearchToolbar(Rect rect, string searchLabel, string currentText, string summaryText, Action<string> setText, Action resetScroll)
        {
            Rect labelRect = new Rect(rect.x, rect.y + 5f, 56f, NetworkStorageUiConstants.SearchHeight);
            Rect summaryRect = new Rect(rect.xMax - 250f, rect.y + 5f, 250f, NetworkStorageUiConstants.SearchHeight);
            Rect searchRect = new Rect(labelRect.xMax + 6f, rect.y + 3f, rect.width - labelRect.width - summaryRect.width - 18f, NetworkStorageUiConstants.SearchHeight);

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, searchLabel);
            Text.Anchor = TextAnchor.UpperLeft;

            string updatedText = Widgets.TextField(searchRect, currentText);
            if (updatedText != currentText)
            {
                setText(updatedText);
                resetScroll();
            }

            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(summaryRect, summaryText);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        internal void DrawPanel(Rect rect, bool strong)
        {
            Widgets.DrawBoxSolid(rect, strong ? NetworkStorageUiConstants.StrongSectionFillColor : NetworkStorageUiConstants.SectionFillColor);
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, NetworkStorageUiConstants.OutlineColor);
        }

        internal void DrawThingDefIcon(Rect rect, ThingDef thingDef)
        {
            Widgets.DefIcon(rect, thingDef);
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, NetworkStorageUiConstants.OutlineColor);
        }

        internal void DrawThingIcon(Rect rect, Thing thing)
        {
            Widgets.ThingIcon(rect, thing);
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, NetworkStorageUiConstants.OutlineColor);
        }

        internal void DrawCenteredMessage(Rect rect, string message, GameFont font = GameFont.Medium, Color? color = null)
        {
            Text.Font = font;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = color ?? NetworkStorageUiConstants.SecondaryTextColor;
            Widgets.Label(rect, message);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        internal void DrawEmptySectionMessage(Rect rect, string message)
        {
            DrawCenteredMessage(new Rect(rect.x, rect.y + 18f, rect.width, rect.height - 18f), message, GameFont.Small, NetworkStorageUiConstants.SecondaryTextColor);
        }

        internal void DrawLine(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            Widgets.DrawBoxSolid(rect, color);
            GUI.color = previousColor;
        }

        private void DrawStatusPill(Rect rect, DataNetwork network)
        {
            Color pillColor;
            string label;

            if (!network.HasActiveController)
            {
                pillColor = NetworkStorageUiConstants.WarningColor;
                label = "MN_NetworkStorageNoController".Translate();
            }
            else if (network.OvercommittedBytes > 0)
            {
                pillColor = NetworkStorageUiConstants.ErrorColor;
                label = "MN_NetworkStorageStatusOvercommitted".Translate();
            }
            else if (network.PowerMode == NetworkPowerMode.Offline || network.PowerMode == NetworkPowerMode.ControllerDisabled)
            {
                pillColor = NetworkStorageUiConstants.ErrorColor;
                label = "MN_NetworkStorageStatusOffline".Translate();
            }
            else if (network.PowerMode == NetworkPowerMode.ReservePowered)
            {
                pillColor = NetworkStorageUiConstants.WarningColor;
                label = "MN_NetworkStorageStatusReserve".Translate();
            }
            else
            {
                pillColor = NetworkStorageUiConstants.OkColor;
                label = "MN_NetworkStorageStatusOnline".Translate();
            }

            Color previousColor = GUI.color;
            Widgets.DrawBoxSolid(rect, new Color(pillColor.r, pillColor.g, pillColor.b, 0.22f));
            GUI.color = pillColor;
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, pillColor);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previousColor;
        }
    }
}
