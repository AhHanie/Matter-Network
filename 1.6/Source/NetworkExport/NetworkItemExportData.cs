using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    public class NetworkItemExportData : IExposable
    {
        public int formatVersion = 1;
        public string exportedAtUtc;
        public string sourceMapName;
        public int exportedStackCount;
        public int exportedUnitCount;
        public List<string> exportedThingDefNames = new List<string>();
        public List<string> exportedThingLabels = new List<string>();
        public List<string> exportedModIds = new List<string>();
        public List<string> exportedModNames = new List<string>();
        public List<Thing> items = new List<Thing>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref formatVersion, "formatVersion", 1);
            Scribe_Values.Look(ref exportedAtUtc, "exportedAtUtc");
            Scribe_Values.Look(ref sourceMapName, "sourceMapName");
            Scribe_Values.Look(ref exportedStackCount, "exportedStackCount", 0);
            Scribe_Values.Look(ref exportedUnitCount, "exportedUnitCount", 0);
            Scribe_Collections.Look(ref exportedThingDefNames, "exportedThingDefNames", LookMode.Value);
            Scribe_Collections.Look(ref exportedThingLabels, "exportedThingLabels", LookMode.Value);
            Scribe_Collections.Look(ref exportedModIds, "exportedModIds", LookMode.Value);
            Scribe_Collections.Look(ref exportedModNames, "exportedModNames", LookMode.Value);
            Scribe_Collections.Look(ref items, "items", LookMode.Deep);
        }
    }
}
