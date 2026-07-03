using System.Linq;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class Dialog_NetworkItemExportFileList_Load : Dialog_NetworkItemExportFileList
    {
        private readonly NetworkBuildingDiskDrive drive;
        private readonly Thing preselectedDisk;

        protected override bool FocusSearchField => true;

        public Dialog_NetworkItemExportFileList_Load(NetworkBuildingDiskDrive drive, Thing preselectedDisk = null)
        {
            this.drive = drive;
            this.preselectedDisk = preselectedDisk;
            interactButLabel = "MN_DiskImport_Label".Translate();
        }

        protected override void ReloadFiles()
        {
            SetFiles(NetworkItemExportService.AllExportFiles().Select(NetworkItemExportFileInfo.FromFile));
        }

        protected override void DoFileInteraction(NetworkItemExportFileInfo file)
        {
            Close();

            if (preselectedDisk != null)
            {
                ImportInto(preselectedDisk, file);
                return;
            }

            var disks = drive.HeldItems.ToList();
            if (disks.Count == 0)
            {
                Messages.Message("MN_DiskImport_NoCapacityComp".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (disks.Count == 1)
            {
                ImportInto(disks[0], file);
                return;
            }

            Find.WindowStack.Add(new Dialog_DiskImportTargetDisk(disks, disk => ImportInto(disk, file)));
        }

        private void ImportInto(Thing disk, NetworkItemExportFileInfo file)
        {
            DiskImportReport report = NetworkItemExportService.ImportIntoDisk(disk, file.FileInfo.FullName, drive);
            Messages.Message(report.ToMessage(), disk, MessageTypeDefOf.TaskCompletion, historical: false);
        }
    }
}
