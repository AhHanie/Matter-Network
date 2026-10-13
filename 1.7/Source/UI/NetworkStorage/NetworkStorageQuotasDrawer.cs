using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    internal sealed class NetworkStorageQuotasDrawer
    {
        private const float RowHeight = 42f;
        private const float HeaderHeight = 24f;
        private const float ButtonWidth = 42f;
        private const float SmallButtonWidth = 34f;

        private readonly NetworkStorageChromeDrawer chromeDrawer;
        private readonly NetworkStorageTabDataSource dataSource;

        internal NetworkStorageQuotasDrawer(NetworkStorageChromeDrawer chromeDrawer, NetworkStorageTabDataSource dataSource)
        {
            this.chromeDrawer = chromeDrawer;
            this.dataSource = dataSource;
        }

        internal void Draw(Rect rect, DataNetwork network, NetworkStorageTabState state)
        {
            Rect toolbarRect = new Rect(rect.x, rect.y, rect.width, NetworkStorageUiConstants.ToolbarHeight);
            Rect modesRect = new Rect(rect.x, toolbarRect.yMax + 4f, rect.width, 28f);
            Rect contentRect = new Rect(rect.x, modesRect.yMax + 8f, rect.width, rect.height - NetworkStorageUiConstants.ToolbarHeight - 40f);

            DrawToolbar(toolbarRect, state);
            DrawModeButtons(modesRect, state);

            List<QuotaItemEntry> entries = state.GetQuotaEntries(dataSource, network);
            DrawSummary(toolbarRect, entries.Count);

            chromeDrawer.DrawPanel(contentRect, strong: false);
            Rect innerRect = contentRect.ContractedBy(NetworkStorageUiConstants.SectionPadding);

            if (entries.Count == 0)
            {
                chromeDrawer.DrawCenteredMessage(innerRect, "MN_NetworkStorageNoQuotaDefs".Translate(), GameFont.Small, NetworkStorageUiConstants.SecondaryTextColor);
                return;
            }

            Rect headerRect = new Rect(innerRect.x, innerRect.y, innerRect.width, HeaderHeight);
            Rect scrollRect = new Rect(innerRect.x, headerRect.yMax + 4f, innerRect.width, innerRect.height - HeaderHeight - 4f);
            DrawHeader(headerRect);

            float rowStep = RowHeight + NetworkStorageUiConstants.RowGap;
            float contentHeight = Mathf.Max(scrollRect.height - 4f, entries.Count * rowStep - NetworkStorageUiConstants.RowGap);
            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 16f, contentHeight);

            state.QuotaScrollPosition.x = 0f;
            state.QuotaScrollPosition.y = Mathf.Clamp(state.QuotaScrollPosition.y, 0f, Mathf.Max(0f, contentHeight - scrollRect.height));
            Widgets.BeginScrollView(scrollRect, ref state.QuotaScrollPosition, viewRect);
            DrawVisibleQuotaRows(scrollRect, viewRect, rowStep, entries, network, state);
            Widgets.EndScrollView();
        }

        private void DrawVisibleQuotaRows(Rect scrollRect, Rect viewRect, float rowStep, List<QuotaItemEntry> entries, DataNetwork network, NetworkStorageTabState state)
        {
            int firstIndex = Mathf.Max(0, Mathf.FloorToInt(state.QuotaScrollPosition.y / rowStep));
            int lastIndex = Mathf.Min(entries.Count, Mathf.CeilToInt((state.QuotaScrollPosition.y + scrollRect.height) / rowStep) + 1);

            for (int i = firstIndex; i < lastIndex; i++)
            {
                Rect rowRect = new Rect(0f, i * rowStep, viewRect.width, RowHeight);
                DrawQuotaRow(rowRect, entries[i], network, state);
            }
        }

        private void DrawToolbar(Rect rect, NetworkStorageTabState state)
        {
            Rect labelRect = new Rect(rect.x, rect.y + 5f, 56f, NetworkStorageUiConstants.SearchHeight);
            Rect summaryRect = GetSummaryRect(rect);
            Rect searchRect = new Rect(labelRect.xMax + 6f, rect.y + 3f, rect.width - labelRect.width - summaryRect.width - 18f, NetworkStorageUiConstants.SearchHeight);

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, "MN_NetworkStorageSearchLabel".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            string updatedText = Widgets.TextField(searchRect, state.QuotaSearchText);
            if (updatedText != state.QuotaSearchText)
            {
                state.QuotaSearchText = updatedText;
                state.QuotaScrollPosition = Vector2.zero;
            }

            GUI.color = Color.white;
        }

        private void DrawSummary(Rect rect, int entryCount)
        {
            Rect summaryRect = GetSummaryRect(rect);

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(summaryRect, "MN_NetworkStorageQuotaSummary".Translate(entryCount));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static Rect GetSummaryRect(Rect rect)
        {
            return new Rect(rect.xMax - 140f, rect.y + 5f, 140f, NetworkStorageUiConstants.SearchHeight);
        }

        private void DrawModeButtons(Rect rect, NetworkStorageTabState state)
        {
            float x = rect.x;
            DrawModeButton(new Rect(x, rect.y, 92f, rect.height), "MN_NetworkStorageQuotaModeAll".Translate(), NetworkStorageQuotaMode.All, state);
            x += 98f;
            DrawModeButton(new Rect(x, rect.y, 112f, rect.height), "MN_NetworkStorageQuotaModeConfigured".Translate(), NetworkStorageQuotaMode.Configured, state);
            x += 118f;
            DrawModeButton(new Rect(x, rect.y, 92f, rect.height), "MN_NetworkStorageQuotaModeStored".Translate(), NetworkStorageQuotaMode.Stored, state);
            x += 98f;
            DrawModeButton(new Rect(x, rect.y, 112f, rect.height), "MN_NetworkStorageQuotaModeDisallowed".Translate(), NetworkStorageQuotaMode.Disallowed, state);
        }

        private void DrawModeButton(Rect rect, string label, NetworkStorageQuotaMode mode, NetworkStorageTabState state)
        {
            bool selected = state.QuotaMode == mode;
            Color previous = GUI.color;
            GUI.color = selected ? NetworkStorageUiConstants.AccentColor : Color.white;
            if (Widgets.ButtonText(rect, label))
            {
                state.QuotaMode = mode;
                state.QuotaScrollPosition = Vector2.zero;
            }
            GUI.color = previous;
        }

        private void DrawHeader(Rect rect)
        {
            Rect labelRect;
            Rect storedRect;
            Rect maxRect;
            Rect remainingRect;
            Rect statusRect;
            Rect columnsRect = new Rect(rect.x + 6f, rect.y, rect.width - 28f, rect.height);
            GetColumns(columnsRect, out Rect _, out labelRect, out storedRect, out maxRect, out remainingRect, out statusRect);

            Text.Font = GameFont.Tiny;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, "MN_NetworkStorageQuotaHeaderDef".Translate());
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(storedRect, "MN_NetworkStorageQuotaHeaderStored".Translate());
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(maxRect, "MN_NetworkStorageQuotaHeaderMax".Translate());
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(remainingRect, "MN_NetworkStorageQuotaHeaderRemaining".Translate());
            Widgets.Label(statusRect, "MN_NetworkStorageQuotaHeaderStatus".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawQuotaRow(Rect rect, QuotaItemEntry entry, DataNetwork network, NetworkStorageTabState state)
        {
            chromeDrawer.DrawPanel(rect, strong: false);
            Widgets.DrawHighlightIfMouseover(rect);

            Rect innerRect = rect.ContractedBy(6f);
            GetColumns(innerRect, out Rect iconRect, out Rect labelRect, out Rect storedRect, out Rect maxRect, out Rect remainingRect, out Rect statusRect);

            chromeDrawer.DrawThingDefIcon(iconRect, entry.Def);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(labelRect, entry.Def.LabelCap.Truncate(labelRect.width));

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = entry.HasConfiguredMax && entry.StoredCount > entry.ConfiguredMax
                ? NetworkStorageUiConstants.WarningColor
                : NetworkStorageUiConstants.SecondaryTextColor;
            Widgets.Label(storedRect, dataSource.FormatItemCount(entry.StoredCount));

            DrawMaxControls(maxRect, entry, network, state);

            GUI.color = entry.HasConfiguredMax ? NetworkStorageUiConstants.SecondaryTextColor : NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(remainingRect, entry.HasConfiguredMax ? dataSource.FormatItemCount(entry.Remaining) : "MN_NetworkStorageQuotaUnlimited".Translate().ToString());

            GUI.color = entry.CurrentlyAllowed ? NetworkStorageUiConstants.OkColor : NetworkStorageUiConstants.WarningColor;
            Widgets.Label(statusRect, entry.CurrentlyAllowed ? "MN_NetworkStorageQuotaAllowed".Translate() : "MN_NetworkStorageQuotaDisallowed".Translate());

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawMaxControls(Rect rect, QuotaItemEntry entry, DataNetwork network, NetworkStorageTabState state)
        {
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            Rect clearRect = new Rect(rect.xMax - ButtonWidth, rect.y + 4f, ButtonWidth, 24f);
            Rect plus100Rect = new Rect(clearRect.x - SmallButtonWidth - 4f, rect.y + 4f, SmallButtonWidth, 24f);
            Rect plus10Rect = new Rect(plus100Rect.x - SmallButtonWidth - 4f, rect.y + 4f, SmallButtonWidth, 24f);
            Rect minus10Rect = new Rect(plus10Rect.x - SmallButtonWidth - 4f, rect.y + 4f, SmallButtonWidth, 24f);
            Rect minus100Rect = new Rect(minus10Rect.x - SmallButtonWidth - 4f, rect.y + 4f, SmallButtonWidth, 24f);
            Rect fieldRect = new Rect(rect.x, rect.y + 4f, minus100Rect.x - rect.x - 6f, 24f);

            string buffer = GetQuotaBuffer(entry, state);
            string updated = Widgets.TextField(fieldRect, buffer);
            if (updated != buffer)
            {
                state.QuotaInputBuffers[entry.Def] = updated;
                if (int.TryParse(updated, out int parsed))
                {
                    SetQuota(network, state, entry.Def, parsed);
                }
            }

            DrawStepButton(minus100Rect, "-100", -100, entry, network, state);
            DrawStepButton(minus10Rect, "-10", -10, entry, network, state);
            DrawStepButton(plus10Rect, "+10", 10, entry, network, state);
            DrawStepButton(plus100Rect, "+100", 100, entry, network, state);

            if (Widgets.ButtonText(clearRect, "MN_NetworkStorageQuotaClear".Translate()))
            {
                network.ClearItemQuota(entry.Def);
                state.QuotaInputBuffers[entry.Def] = string.Empty;
                state.InvalidateQuotaEntries();
            }
        }

        private void DrawStepButton(Rect rect, string label, int delta, QuotaItemEntry entry, DataNetwork network, NetworkStorageTabState state)
        {
            if (Widgets.ButtonText(rect, label))
            {
                int current = entry.HasConfiguredMax ? entry.ConfiguredMax : 0;
                SetQuota(network, state, entry.Def, current + delta);
            }
        }

        private void SetQuota(DataNetwork network, NetworkStorageTabState state, ThingDef def, int value)
        {
            int clamped = Mathf.Clamp(value, 0, DataNetwork.MaxItemQuota);
            network.SetItemQuota(def, clamped);
            state.QuotaInputBuffers[def] = clamped.ToString();
            state.InvalidateQuotaEntries();
        }

        private string GetQuotaBuffer(QuotaItemEntry entry, NetworkStorageTabState state)
        {
            if (!state.QuotaInputBuffers.TryGetValue(entry.Def, out string buffer))
            {
                buffer = entry.HasConfiguredMax ? entry.ConfiguredMax.ToString() : string.Empty;
                state.QuotaInputBuffers[entry.Def] = buffer;
            }

            return buffer;
        }

        private static void GetColumns(Rect rect, out Rect iconRect, out Rect labelRect, out Rect storedRect, out Rect maxRect, out Rect remainingRect, out Rect statusRect)
        {
            iconRect = new Rect(rect.x, rect.y + 1f, 30f, 30f);
            statusRect = new Rect(rect.xMax - 82f, rect.y, 82f, rect.height);
            remainingRect = new Rect(statusRect.x - 96f, rect.y, 88f, rect.height);
            maxRect = new Rect(remainingRect.x - 280f, rect.y, 272f, rect.height);
            storedRect = new Rect(maxRect.x - 74f, rect.y, 66f, rect.height);
            labelRect = new Rect(iconRect.xMax + 8f, rect.y, storedRect.x - iconRect.xMax - 16f, rect.height);
        }
    }
}
