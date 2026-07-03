using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public class Dialog_DiskImportTargetDisk : Window
    {
        private const float RowHeight = 40f;
        // Gap above the vanilla Close button drawn separately by Window.WindowOnGUI.
        private const float CloseButtonGap = 18f;

        private readonly List<Thing> disks;
        private readonly Action<Thing> onDiskChosen;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(420f, 480f);

        public Dialog_DiskImportTargetDisk(List<Thing> disks, Action<Thing> onDiskChosen)
        {
            this.disks = disks;
            this.onDiskChosen = onDiskChosen;
            doCloseButton = true;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(0f, 0f, inRect.width, 30f);
            Widgets.Label(titleRect, "MN_DiskImport_Label".Translate());
            Text.Font = GameFont.Small;

            Rect listRect = new Rect(0f, titleRect.yMax + 8f, inRect.width, inRect.height - titleRect.height - 8f - CloseButSize.y - CloseButtonGap);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, disks.Count * RowHeight);

            Widgets.BeginScrollView(listRect, ref scrollPosition, viewRect);
            for (int i = 0; i < disks.Count; i++)
            {
                Thing disk = disks[i];
                Rect rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                Widgets.DrawHighlightIfMouseover(rowRect);

                CompDiskCapacity capacity = disk.TryGetComp<CompDiskCapacity>();
                string label = $"{disk.LabelShortCap} ({capacity.ArchivedFreeBytes} bytes free)";

                if (Widgets.ButtonInvisible(rowRect))
                {
                    onDiskChosen(disk);
                    Close();
                }

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(rowRect, label);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            Widgets.EndScrollView();
        }
    }
}
