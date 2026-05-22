using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    internal sealed class NetworkStoragePowerDrawer
    {
        private readonly NetworkStorageChromeDrawer chromeDrawer;

        internal NetworkStoragePowerDrawer(NetworkStorageChromeDrawer chromeDrawer, NetworkStorageTabDataSource dataSource)
        {
            this.chromeDrawer = chromeDrawer;
        }

        internal void Draw(Rect rect, DataNetwork network)
        {
            float cardWidth = (rect.width - (NetworkStorageUiConstants.SummaryGap * 3f)) / 4f;
            chromeDrawer.DrawSummaryCard(new Rect(rect.x, rect.y, cardWidth, NetworkStorageUiConstants.SummaryCardHeight), "MN_NetworkStoragePowerStatus".Translate(), network.PowerModeLabel, GetModeColor(network), null);
            chromeDrawer.DrawSummaryCard(new Rect(rect.x + (cardWidth + NetworkStorageUiConstants.SummaryGap), rect.y, cardWidth, NetworkStorageUiConstants.SummaryCardHeight), "MN_NetworkStoragePowerRequiredDraw".Translate(), network.RequiredPowerDrawWatts + " W", NetworkStorageUiConstants.SecondaryTextColor, null);
            chromeDrawer.DrawSummaryCard(new Rect(rect.x + ((cardWidth + NetworkStorageUiConstants.SummaryGap) * 2f), rect.y, cardWidth, NetworkStorageUiConstants.SummaryCardHeight), "MN_NetworkStoragePowerChargingDraw".Translate(), network.CurrentRequestedChargingDrawWatts + " W", NetworkStorageUiConstants.SecondaryTextColor, null);
            chromeDrawer.DrawSummaryCard(new Rect(rect.x + ((cardWidth + NetworkStorageUiConstants.SummaryGap) * 3f), rect.y, cardWidth, NetworkStorageUiConstants.SummaryCardHeight), "MN_NetworkStoragePowerReserve".Translate(), network.StoredReserveEnergyWd.ToString("F0") + " / " + network.MaxReserveEnergyWd.ToString("F0") + " Wd", NetworkStorageUiConstants.AccentColor, network.ReserveFillPercent);

            Rect settingsRect = new Rect(rect.x, rect.y + NetworkStorageUiConstants.SummaryCardHeight + 14f, rect.width, 240f);
            chromeDrawer.DrawLabeledSection(settingsRect, "MN_NetworkStoragePowerDetails".Translate());
            Rect innerRect = settingsRect.ContractedBy(NetworkStorageUiConstants.SectionPadding);

            chromeDrawer.DrawKeyValueRow(new Rect(innerRect.x, innerRect.y + 28f, innerRect.width, 24f), "MN_NetworkStoragePowerRequiredDraw".Translate(), network.RequiredPowerDrawWatts + " W");
            chromeDrawer.DrawKeyValueRow(new Rect(innerRect.x, innerRect.y + 52f, innerRect.width, 24f), "MN_NetworkStoragePowerBuildingDraw".Translate(), network.BuildingPowerDrawWatts + " W");
            chromeDrawer.DrawKeyValueRow(new Rect(innerRect.x, innerRect.y + 76f, innerRect.width, 24f), "MN_NetworkStoragePowerStoredItemDraw".Translate(), FormatStoredItemDraw(network));
            chromeDrawer.DrawKeyValueRow(new Rect(innerRect.x, innerRect.y + 100f, innerRect.width, 24f), "MN_NetworkStoragePowerConfiguredCharging".Translate(), network.ReserveChargingDrawWatts + " W");
            chromeDrawer.DrawKeyValueRow(new Rect(innerRect.x, innerRect.y + 124f, innerRect.width, 24f), "MN_NetworkStoragePowerRuntime".Translate(), FormatRuntime(network));

            Rect sliderRect = new Rect(innerRect.x, innerRect.y + 158f, innerRect.width - 96f, 24f);
            Rect minusRect = new Rect(sliderRect.xMax + 8f, sliderRect.y, 40f, 24f);
            Rect plusRect = new Rect(minusRect.xMax + 8f, sliderRect.y, 40f, 24f);

            float sliderValue = Widgets.HorizontalSlider(sliderRect, network.ReserveChargingDrawWatts, 0, network.MaxReserveChargingDraw, true);
            int roundedSliderValue = Mathf.RoundToInt(sliderValue / network.ReserveChargeStep) * network.ReserveChargeStep;
            if (roundedSliderValue != network.ReserveChargingDrawWatts)
            {
                network.SetReserveChargingDrawWatts(roundedSliderValue);
            }

            if (Widgets.ButtonText(minusRect, "MN_NetworkStoragePowerChargeMinus".Translate()))
            {
                network.SetReserveChargingDrawWatts(network.ReserveChargingDrawWatts - network.ReserveChargeStep);
            }

            if (Widgets.ButtonText(plusRect, "MN_NetworkStoragePowerChargePlus".Translate()))
            {
                network.SetReserveChargingDrawWatts(network.ReserveChargingDrawWatts + network.ReserveChargeStep);
            }

            if (DebugSettings.ShowDevGizmos)
            {
                Rect fillRect = new Rect(innerRect.x, innerRect.y + 188f, 140f, 24f);
                Rect emptyRect = new Rect(fillRect.xMax + 8f, fillRect.y, 140f, 24f);
                if (Widgets.ButtonText(fillRect, "MN_NetworkStoragePowerFillReserve".Translate()))
                {
                    network.SetReserveEnergy(network.MaxReserveEnergyWd);
                }

                if (Widgets.ButtonText(emptyRect, "MN_NetworkStoragePowerEmptyReserve".Translate()))
                {
                    network.SetReserveEnergy(0f);
                }
            }
        }

        private static Color GetModeColor(DataNetwork network)
        {
            if (network.PowerMode == NetworkPowerMode.GridPowered)
            {
                return NetworkStorageUiConstants.OkColor;
            }

            if (network.PowerMode == NetworkPowerMode.ReservePowered)
            {
                return NetworkStorageUiConstants.WarningColor;
            }

            return NetworkStorageUiConstants.ErrorColor;
        }

        private string FormatRuntime(DataNetwork network)
        {
            int ticks = network.EstimatedReserveRuntimeTicks();
            if (ticks == int.MaxValue)
            {
                return "MN_NetworkStoragePowerRuntimeIndefinite".Translate();
            }

            if (ticks <= 0)
            {
                return "MN_NetworkStoragePowerRuntimeEmpty".Translate();
            }

            return ticks.ToStringTicksToPeriod();
        }

        private string FormatStoredItemDraw(DataNetwork network)
        {
            if (!ModSettings.EnableStoredItemPowerDraw)
            {
                return "MN_NetworkStoragePowerStoredItemDrawDisabled".Translate();
            }

            return "MN_NetworkStoragePowerStoredItemDrawValue".Translate(
                network.StoredItemPowerDrawWatts,
                ModSettings.StoredItemPowerDrawPer100Bytes);
        }
    }
}
