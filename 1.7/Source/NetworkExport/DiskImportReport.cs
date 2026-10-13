using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace SK_Matter_Network
{
    public class DiskImportReport
    {
        public int LoadedStacks;
        public int ImportedStacks;
        public int ImportedUnits;
        public int SkippedStacks;
        public int SkippedUnits;
        public bool CapacityLimited;
        public bool FileNotFound;
        public bool LoadFailed;
        public List<string> MissingDefs = new List<string>();
        public List<string> MissingClasses = new List<string>();

        public string ToMessage()
        {
            if (FileNotFound)
            {
                return "MN_DiskImport_NoFiles".Translate();
            }

            if (LoadFailed)
            {
                return "MN_DiskImport_LoadFailed".Translate();
            }

            if (ImportedStacks == 0 && LoadedStacks == 0)
            {
                return "MN_DiskImport_NoImportableItems".Translate();
            }

            var sb = new StringBuilder();
            sb.Append("MN_DiskImport_Loaded".Translate(ImportedStacks, ImportedUnits));

            if (CapacityLimited)
            {
                sb.Append(" ").Append("MN_DiskImport_CapacityLimited".Translate(SkippedStacks, SkippedUnits));
            }

            if (MissingDefs.Count > 0)
            {
                sb.Append(" ").Append("MN_DiskImport_MissingDefs".Translate(MissingDefs.Distinct().Count()));
            }

            return sb.ToString();
        }
    }
}
