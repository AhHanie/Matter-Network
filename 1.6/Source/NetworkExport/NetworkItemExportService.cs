using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public static class NetworkItemExportService
    {
        public static string DefaultExportNameForDisk(CompDiskCapacity capacity)
        {
            return $"{GenFile.SanitizedFileName(capacity.parent.LabelShort)}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        }

        public static IEnumerable<FileInfo> AllExportFiles()
        {
            return NetworkExportPaths.AllExportFiles();
        }

        public static bool CanExportDisk(CompDiskCapacity capacity, out string rejectReasonKey)
        {
            if (capacity == null || !capacity.HasArchivedItems)
            {
                rejectReasonKey = "MN_DiskExport_NoItems";
                return false;
            }

            rejectReasonKey = null;
            return true;
        }

        public static void ExportDisk(CompDiskCapacity capacity, string exportName)
        {
            List<Thing> sourceItems = capacity.ArchivedContainer.InnerListForReading.ToList();
            string sourceMapName = capacity.parent.Map?.Parent?.Label;
            string path = NetworkExportPaths.FilePathFor(exportName);

            var data = new NetworkItemExportData
            {
                exportedAtUtc = DateTime.UtcNow.ToString("o"),
                sourceMapName = sourceMapName,
                exportedStackCount = sourceItems.Count,
                exportedUnitCount = sourceItems.Sum(t => t.stackCount),
                exportedThingDefNames = sourceItems.Select(t => t.def.defName).Distinct().ToList(),
                exportedThingLabels = sourceItems.Select(t => t.LabelCap.ToString()).Distinct().ToList(),
                exportedModIds = LoadedModManager.RunningMods.Select(m => m.PackageId).ToList(),
                exportedModNames = LoadedModManager.RunningMods.Select(m => m.Name).ToList(),
                items = sourceItems
            };

            SafeSaver.Save(path, "matterNetworkItemExport", delegate
            {
                ScribeMetaHeaderUtility.WriteMetaHeader();
                Scribe_Deep.Look(ref data, "itemExport");
            });
        }

        public static void DeleteExport(NetworkItemExportFileInfo file)
        {
            file.FileInfo.Delete();
        }

        public static DiskImportReport ImportIntoDisk(Thing diskThing, string filePath, NetworkBuildingDiskDrive drive)
        {
            var report = new DiskImportReport();

            if (!File.Exists(filePath))
            {
                report.FileNotFound = true;
                return report;
            }

            CompDiskCapacity capacity = diskThing.TryGetComp<CompDiskCapacity>();
            string sanitizedTempPath = BuildSanitizedTempFile(filePath, report);

            NetworkItemExportData data = null;
            try
            {
                Scribe.loader.InitLoading(sanitizedTempPath);
                ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeMetaHeaderUtility.ScribeHeaderMode.None, logVersionConflictWarning: true);
                Scribe_Deep.Look(ref data, "itemExport");
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception e)
            {
                Scribe.ForceStop();
                Logger.Exception(e, $"Failed to load network item export '{filePath}'");
                report.LoadFailed = true;
                return report;
            }
            finally
            {
                File.Delete(sanitizedTempPath);
            }

            List<Thing> loaded = SanitizeLoadedItems(data?.items);
            report.LoadedStacks = loaded.Count;

            foreach (Thing thing in loaded)
            {
                int free = capacity.ArchivedFreeBytes;
                if (free <= 0)
                {
                    report.SkippedStacks++;
                    report.SkippedUnits += thing.stackCount;
                    report.CapacityLimited = true;
                    thing.Destroy(DestroyMode.Vanish);
                    continue;
                }

                Thing toAdd = thing;
                if (thing.stackCount > free)
                {
                    int leftover = thing.stackCount - free;
                    toAdd = thing.SplitOff(free);
                    report.SkippedStacks++;
                    report.SkippedUnits += leftover;
                    report.CapacityLimited = true;
                    thing.Destroy(DestroyMode.Vanish);
                }

                if (capacity.ArchivedContainer.TryAdd(toAdd, canMergeWithExistingStacks: true))
                {
                    report.ImportedStacks++;
                    report.ImportedUnits += toAdd.stackCount;
                }
                else
                {
                    report.SkippedStacks++;
                    report.SkippedUnits += toAdd.stackCount;
                    toAdd.Destroy(DestroyMode.Vanish);
                }
            }

            drive?.NotifyDiskContentsImported(diskThing);
            return report;
        }

        private static string BuildSanitizedTempFile(string sourcePath, DiskImportReport report)
        {
            var doc = new XmlDocument();
            doc.Load(sourcePath);

            XmlNode itemsNode = doc.SelectSingleNode("matterNetworkItemExport/itemExport/items");
            if (itemsNode != null)
            {
                var toRemove = new List<XmlNode>();
                foreach (XmlNode li in itemsNode.SelectNodes("li"))
                {
                    string defName = li.SelectSingleNode("def")?.InnerText;
                    if (string.IsNullOrEmpty(defName) || DefDatabase<ThingDef>.GetNamedSilentFail(defName) == null)
                    {
                        toRemove.Add(li);
                        report.MissingDefs.Add(defName ?? "<unknown>");
                        continue;
                    }

                    string classAttr = li.Attributes?["Class"]?.Value;
                    if (!string.IsNullOrEmpty(classAttr) && GenTypes.GetTypeInAnyAssembly(classAttr) == null)
                    {
                        report.MissingClasses.Add(classAttr);
                    }
                }

                foreach (XmlNode li in toRemove)
                {
                    itemsNode.RemoveChild(li);
                }
            }

            string tempPath = Path.Combine(NetworkExportPaths.TempPath, Path.GetRandomFileName() + NetworkExportPaths.FileExtension);
            doc.Save(tempPath);
            return tempPath;
        }

        private static List<Thing> SanitizeLoadedItems(List<Thing> items)
        {
            var result = new List<Thing>();
            if (items == null)
            {
                return result;
            }

            foreach (Thing thing in items)
            {
                if (thing == null || thing.Destroyed || thing.def == null || thing.stackCount <= 0)
                {
                    continue;
                }

                result.Add(thing);
            }

            return result;
        }
    }
}
