using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public abstract class Dialog_NetworkItemExportFileList : Window
    {
        protected const float EntryHeight = 40f;
        protected const float FileNameLeftMargin = 8f;
        protected const float FileInfoWidth = 170f;
        protected const float InteractButWidth = 140f;
        protected const float DeleteButSize = 32f;
        protected const float NameTextFieldHeight = 35f;

        protected readonly List<NetworkItemExportFileInfo> files = new List<NetworkItemExportFileInfo>();
        protected string interactButLabel = "";
        protected string typingName = "";

        private Vector2 scrollPosition;
        private readonly QuickSearchWidget search = new QuickSearchWidget();

        public override Vector2 InitialSize => new Vector2(620f, 700f);

        protected virtual bool ShouldDoTypeInField => false;
        protected virtual bool FocusSearchField => false;

        protected Dialog_NetworkItemExportFileList()
        {
            doCloseButton = true;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            ReloadFiles();
        }

        public override void PostOpen()
        {
            base.PostOpen();
            if (FocusSearchField)
            {
                search.Focus();
            }
        }

        protected abstract void ReloadFiles();
        protected abstract void DoFileInteraction(NetworkItemExportFileInfo file);

        protected void SetFiles(IEnumerable<NetworkItemExportFileInfo> newFiles)
        {
            files.Clear();
            files.AddRange(newFiles);
        }

        // Gap above the vanilla Close button drawn separately by Window.WindowOnGUI; matches Dialog_FileList's own spacing.
        private const float CloseButtonGap = 18f;

        public override void DoWindowContents(Rect inRect)
        {
            const float searchHeight = 30f;
            const float gap = 8f;

            Rect searchRect = new Rect(0f, 0f, inRect.width, searchHeight);
            search.OnGUI(searchRect);

            float bottomReserve = CloseButSize.y + CloseButtonGap + (ShouldDoTypeInField ? (NameTextFieldHeight + gap) : 0f);
            Rect listRect = new Rect(0f, searchRect.yMax + gap, inRect.width, inRect.height - searchRect.height - gap - bottomReserve);

            DrawFileList(listRect);

            if (ShouldDoTypeInField)
            {
                DoTypeInField(new Rect(0f, listRect.yMax + gap, inRect.width, NameTextFieldHeight));
            }
        }

        private void DrawFileList(Rect rect)
        {
            List<NetworkItemExportFileInfo> visibleFiles = search.filter.Active
                ? files.Where(f => search.filter.Matches(f.FileName)).ToList()
                : files;

            Widgets.DrawMenuSection(rect);

            if (visibleFiles.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.grey;
                Widgets.Label(rect, "MN_DiskImport_NoFiles".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, visibleFiles.Count * EntryHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);

            float curY = 0f;
            foreach (NetworkItemExportFileInfo file in visibleFiles)
            {
                Rect entryRect = new Rect(0f, curY, viewRect.width, EntryHeight);
                DrawFileEntry(entryRect, file);
                curY += EntryHeight;
            }

            Widgets.EndScrollView();
        }

        private void DrawFileEntry(Rect rect, NetworkItemExportFileInfo file)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            Rect deleteRect = new Rect(rect.xMax - DeleteButSize - 4f, rect.y + ((rect.height - DeleteButSize) / 2f), DeleteButSize, DeleteButSize);
            Rect interactRect = new Rect(deleteRect.x - InteractButWidth - 8f, rect.y + 4f, InteractButWidth, rect.height - 8f);
            Rect infoRect = new Rect(interactRect.x - FileInfoWidth - 8f, rect.y, FileInfoWidth, rect.height);
            Rect nameRect = new Rect(FileNameLeftMargin, rect.y, infoRect.x - FileNameLeftMargin - 8f, rect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            Widgets.Label(nameRect, file.FileName.Truncate(nameRect.width));

            GUI.color = Color.grey;
            Text.Font = GameFont.Tiny;
            Widgets.Label(infoRect, $"{file.LastWriteTime:g}\n{file.StackCount} stacks | {file.UnitCount} units");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(interactRect, interactButLabel))
            {
                DoFileInteraction(file);
            }

            if (Widgets.ButtonImage(deleteRect, TexButton.Delete))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "MN_ItemExport_DeleteConfirm".Translate(file.FileName),
                    delegate
                    {
                        NetworkItemExportService.DeleteExport(file);
                        ReloadFiles();
                    },
                    destructive: true));
            }
        }

        protected virtual void DoTypeInField(Rect rect)
        {
            Rect fieldRect = new Rect(rect.x, rect.y, rect.width - InteractButWidth - 8f, rect.height);
            Rect saveRect = new Rect(fieldRect.xMax + 8f, rect.y, InteractButWidth, rect.height);

            typingName = Widgets.TextField(fieldRect, typingName);
            bool validName = typingName.Length > 0 && GenText.IsValidFilename(typingName);

            GUI.enabled = validName;
            bool pressedEnter = validName && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
            if (Widgets.ButtonText(saveRect, interactButLabel) || pressedEnter)
            {
                TrySaveTypedName();
            }
            GUI.enabled = true;
        }

        protected virtual void TrySaveTypedName()
        {
        }
    }
}
