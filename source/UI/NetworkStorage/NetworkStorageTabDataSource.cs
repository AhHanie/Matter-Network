using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    internal sealed class NetworkStorageTabDataSource
    {
        internal NetworkStorageTabDataSnapshot BuildSnapshot(DataNetwork network)
        {
            List<GroupedItemEntry> groupedEntries = new List<GroupedItemEntry>();
            List<StoredThingEntry> storedEntries = new List<StoredThingEntry>();
            int totalUnits = 0;

            if (network.ActiveController?.innerContainer == null)
            {
                return NetworkStorageTabDataSnapshot.Empty;
            }

            Dictionary<ThingDef, GroupedItemEntry> groupedMap = new Dictionary<ThingDef, GroupedItemEntry>();
            foreach (Thing thing in network.ActiveController.innerContainer.InnerListForReading)
            {
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                storedEntries.Add(new StoredThingEntry(thing));
                totalUnits += thing.stackCount;

                if (groupedMap.TryGetValue(thing.def, out GroupedItemEntry existing))
                {
                    existing.TotalCount += thing.stackCount;
                    existing.StackEntries++;
                    groupedMap[thing.def] = existing;
                }
                else
                {
                    groupedMap.Add(thing.def, new GroupedItemEntry(thing.def, thing.stackCount, 1));
                }
            }

            groupedEntries.AddRange(groupedMap.Values
                .OrderByDescending(entry => entry.TotalCount)
                .ThenBy(entry => entry.Def.label));
            storedEntries.Sort(CompareStoredEntries);

            return new NetworkStorageTabDataSnapshot(
                groupedEntries,
                storedEntries,
                totalUnits,
                groupedEntries.Count,
                storedEntries.Count);
        }

        internal List<GroupedItemEntry> FilterGroupedEntries(NetworkStorageTabDataSnapshot snapshot, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return snapshot.GroupedEntries;
            }

            string term = searchText.Trim();
            return snapshot.GroupedEntries
                .Where(entry => ContainsIgnoreCase(entry.Def.LabelCap, term)
                    || ContainsIgnoreCase(entry.Def.label, term)
                    || ContainsIgnoreCase(entry.Def.defName, term))
                .ToList();
        }

        internal List<StoredThingEntry> FilterStoredEntries(NetworkStorageTabDataSnapshot snapshot, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return snapshot.StoredEntries;
            }

            string term = searchText.Trim();
            return snapshot.StoredEntries
                .Where(entry => ContainsIgnoreCase(entry.Label, term)
                    || ContainsIgnoreCase(entry.Thing.def.label, term)
                    || ContainsIgnoreCase(entry.Thing.def.defName, term))
                .ToList();
        }

        internal string BuildThingMetadata(Thing thing)
        {
            List<string> parts = new List<string>();

            if (thing.Stuff != null)
            {
                parts.Add("MN_NetworkStorageMetadataStuff".Translate(thing.Stuff.LabelCap));
            }

            CompQuality compQuality = thing.TryGetComp<CompQuality>();
            if (compQuality != null)
            {
                parts.Add("MN_NetworkStorageMetadataQuality".Translate(compQuality.Quality.GetLabel()));
            }

            if (thing.def.useHitPoints && thing.HitPoints >= 0 && thing.MaxHitPoints > 0)
            {
                parts.Add("MN_NetworkStorageMetadataHitPoints".Translate(thing.HitPoints, thing.MaxHitPoints));
            }

            if (parts.Count == 0)
            {
                return "MN_NetworkStorageMetadataGeneric".Translate();
            }

            return string.Join(" | ", parts);
        }

        internal string FormatItemCount(int count)
        {
            if (count < 1000)
            {
                return count.ToString();
            }

            if (count < 1000000)
            {
                return $"{count / 1000f:0.#}{"MN_CountSuffixK".Translate()}";
            }

            return $"{count / 1000000f:0.#}{"MN_CountSuffixMil".Translate()}";
        }

        private static int CompareStoredEntries(StoredThingEntry a, StoredThingEntry b)
        {
            int labelCompare = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            return b.StackCount.CompareTo(a.StackCount);
        }

        private static bool ContainsIgnoreCase(string value, string term)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(term))
            {
                return false;
            }

            return value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
