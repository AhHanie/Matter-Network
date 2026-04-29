using UnityEngine;
using Verse;
using RimWorld;

namespace SK_Matter_Network
{
    public class ITab_DiskDriveSlots : ITab
    {
        private const float HeaderHeight = 68f;
        private const float SlotHeight = 82f;
        private const float SlotGap = 8f;
        private const float SlotIconSize = 44f;

        private readonly NetworkStorageChromeDrawer chromeDrawer = new NetworkStorageChromeDrawer();
        private Vector2 scrollPosition;

        private NetworkBuildingDiskDrive SelectedDrive => SelThing as NetworkBuildingDiskDrive;

        public ITab_DiskDriveSlots()
        {
            size = new Vector2(620f, 430f);
            labelKey = "MN_DiskDriveSlotsTab";
        }

        protected override void FillTab()
        {
            NetworkBuildingDiskDrive drive = SelectedDrive;
            Rect outerRect = new Rect(0f, 0f, size.x, size.y).ContractedBy(NetworkStorageUiConstants.OuterPadding);

            if (drive == null)
            {
                chromeDrawer.DrawCenteredMessage(outerRect, "MN_DiskDriveSlotsNoDrive".Translate());
                return;
            }

            DrawHeader(new Rect(outerRect.x, outerRect.y, outerRect.width, HeaderHeight), drive);

            Rect slotsRect = new Rect(
                outerRect.x,
                outerRect.y + HeaderHeight + NetworkStorageUiConstants.HeaderGap,
                outerRect.width,
                outerRect.height - HeaderHeight - NetworkStorageUiConstants.HeaderGap);

            DrawSlots(slotsRect, drive);
        }

        protected override void UpdateSize()
        {
            base.UpdateSize();
            size = new Vector2(620f, 430f);
        }

        private void DrawHeader(Rect rect, NetworkBuildingDiskDrive drive)
        {
            chromeDrawer.DrawPanel(rect, strong: true);

            Rect innerRect = rect.ContractedBy(10f);
            Rect titleRect = new Rect(innerRect.x, innerRect.y + 3f, innerRect.width * 0.58f, 30f);
            Rect subtitleRect = new Rect(innerRect.x, titleRect.yMax, innerRect.width * 0.70f, 20f);
            Rect statusRect = new Rect(innerRect.xMax - 150f, innerRect.y + ((innerRect.height - NetworkStorageUiConstants.StatusPillHeight) / 2f), 150f, NetworkStorageUiConstants.StatusPillHeight);
            Rect summaryRect = new Rect(statusRect.x - 180f, statusRect.y, 170f, statusRect.height);

            Text.Font = GameFont.Medium;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(titleRect, "MN_DiskDriveSlotsHeaderTitle".Translate());

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(subtitleRect, drive.LabelCap);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
            Widgets.Label(summaryRect, "MN_DiskDriveSlotsHeaderSummary".Translate(drive.HeldItems.Count, drive.MaximumItems));
            Text.Anchor = TextAnchor.UpperLeft;

            DrawStatusPill(statusRect, drive);
            chromeDrawer.DrawLine(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), NetworkStorageUiConstants.OutlineColor);
            GUI.color = Color.white;
        }

        private void DrawStatusPill(Rect rect, NetworkBuildingDiskDrive drive)
        {
            Color pillColor = drive.Locked ? NetworkStorageUiConstants.WarningColor : NetworkStorageUiConstants.OkColor;
            string label = drive.Locked ? "MN_DiskDriveSlotsLocked".Translate() : "MN_DiskDriveSlotsAccepting".Translate();

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

        private void DrawSlots(Rect rect, NetworkBuildingDiskDrive drive)
        {
            chromeDrawer.DrawPanel(rect, strong: false);

            Rect innerRect = rect.ContractedBy(NetworkStorageUiConstants.SectionPadding);
            int columns = 2;
            float viewWidth = innerRect.width - 16f;
            float slotWidth = (viewWidth - SlotGap) / columns;
            int rows = Mathf.CeilToInt((float)drive.MaximumItems / columns);
            float contentHeight = rows * SlotHeight + Mathf.Max(0, rows - 1) * SlotGap;
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(innerRect.height, contentHeight));

            Widgets.BeginScrollView(innerRect, ref scrollPosition, viewRect);
            for (int i = 0; i < drive.MaximumItems; i++)
            {
                int column = i % columns;
                int row = i / columns;
                Rect slotRect = new Rect(
                    column * (slotWidth + SlotGap),
                    row * (SlotHeight + SlotGap),
                    slotWidth,
                    SlotHeight);
                Thing disk = i < drive.HeldItems.Count ? drive.HeldItems[i] : null;
                DrawSlot(slotRect, i + 1, disk);
            }
            Widgets.EndScrollView();
        }

        private void DrawSlot(Rect rect, int slotNumber, Thing disk)
        {
            Widgets.DrawBoxSolid(rect, NetworkStorageUiConstants.StrongSectionFillColor);
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, NetworkStorageUiConstants.OutlineColor);
            Widgets.DrawHighlightIfMouseover(rect);

            Rect numberRect = new Rect(rect.x + 8f, rect.y + 6f, 70f, 18f);
            Rect statusLightRect = new Rect(rect.xMax - 17f, rect.y + 10f, 8f, 8f);
            Text.Font = GameFont.Tiny;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(numberRect, "MN_DiskDriveSlotsSlotNumber".Translate(slotNumber));

            Rect iconRect = new Rect(rect.x + 10f, rect.y + 28f, SlotIconSize, SlotIconSize);
            Rect labelRect = new Rect(iconRect.xMax + 10f, rect.y + 22f, rect.width - SlotIconSize - 30f, 26f);
            Rect detailsRect = new Rect(labelRect.x, labelRect.yMax, labelRect.width, 38f);

            if (disk == null)
            {
                DrawStatusLight(statusLightRect, NetworkStorageUiConstants.MutedTextColor);
                DrawEmptySlot(iconRect, labelRect);
                GUI.color = Color.white;
                return;
            }

            chromeDrawer.DrawThingIcon(iconRect, disk);
            CompDiskCapacity capacity = disk.TryGetComp<CompDiskCapacity>();
            DrawStatusLight(statusLightRect, capacity.HasArchivedItems ? NetworkStorageUiConstants.WarningColor : NetworkStorageUiConstants.OkColor);

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(labelRect, disk.LabelShortCap);

            Text.Font = GameFont.Tiny;
            GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
            Widgets.Label(detailsRect, FormatDiskDetails(capacity));
            GUI.color = Color.white;
        }

        private void DrawEmptySlot(Rect iconRect, Rect labelRect)
        {
            Widgets.DrawBoxSolid(iconRect, NetworkStorageUiConstants.SectionFillColor);
            Widgets.DrawBoxSolidWithOutline(iconRect, Color.clear, NetworkStorageUiConstants.OutlineColor);

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(labelRect, "MN_DiskDriveSlotsEmpty".Translate());
        }

        private void DrawStatusLight(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(rect, new Color(color.r, color.g, color.b, 0.85f));
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, color);
        }

        private string FormatDiskDetails(CompDiskCapacity capacity)
        {
            if (capacity.HasArchivedItems)
            {
                return "MN_DiskDriveSlotsDiskDetailsArchived".Translate(
                    capacity.MaxBytes,
                    capacity.ArchivedUsedBytes,
                    capacity.ArchivedStackCount);
            }

            return "MN_DiskDriveSlotsDiskDetailsActive".Translate(capacity.MaxBytes);
        }
    }
}
