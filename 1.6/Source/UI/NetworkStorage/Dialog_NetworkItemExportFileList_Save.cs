using System.Linq;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class Dialog_NetworkItemExportFileList_Save : Dialog_NetworkItemExportFileList
    {
        private readonly CompDiskCapacity capacity;

        protected override bool ShouldDoTypeInField => true;

        public Dialog_NetworkItemExportFileList_Save(CompDiskCapacity capacity)
        {
            this.capacity = capacity;
            interactButLabel = "MN_ItemExport_ButtonLabel".Translate();
            typingName = NetworkItemExportService.DefaultExportNameForDisk(capacity);
        }

        protected override void ReloadFiles()
        {
            SetFiles(NetworkItemExportService.AllExportFiles().Select(NetworkItemExportFileInfo.FromFile));
        }

        protected override void DoFileInteraction(NetworkItemExportFileInfo file)
        {
            ExportAs(file.FileName);
        }

        protected override void TrySaveTypedName()
        {
            ExportAs(typingName);
        }

        private void ExportAs(string exportName)
        {
            if (!NetworkItemExportService.CanExportDisk(capacity, out string rejectReasonKey))
            {
                Messages.Message(rejectReasonKey.Translate(), MessageTypeDefOf.RejectInput, historical: false);
                Close();
                return;
            }

            if (System.IO.File.Exists(NetworkExportPaths.FilePathFor(exportName)))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "MN_ItemExport_OverwriteConfirm".Translate(exportName),
                    delegate { DoExport(exportName); },
                    destructive: true));
                return;
            }

            DoExport(exportName);
        }

        private void DoExport(string exportName)
        {
            NetworkItemExportService.ExportDisk(capacity, exportName);
            Messages.Message("MN_ItemExport_Saved".Translate(exportName), MessageTypeDefOf.TaskCompletion, historical: false);
            Close();
        }
    }
}
