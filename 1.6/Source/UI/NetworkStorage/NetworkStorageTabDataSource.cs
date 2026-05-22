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
                if (thing.Destroyed)
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

        internal List<QuotaItemEntry> BuildQuotaEntries(DataNetwork network, string searchText, NetworkStorageQuotaMode mode)
        {
            List<QuotaItemEntry> entries = new List<QuotaItemEntry>();
            IReadOnlyDictionary<ThingDef, int> counts = network.ItemCountByDef;
            string term = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
            HashSet<ThingDef> quotaDefs = new HashSet<ThingDef>();

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (IsQuotaCandidate(network, def))
                {
                    quotaDefs.Add(def);
                }
            }

            foreach (ThingDef def in network.ItemQuotaByDef.Keys)
            {
                quotaDefs.Add(def);
            }

            foreach (ThingDef def in quotaDefs)
            {
                if (term != null && !ContainsIgnoreCase(def.LabelCap, term) && !ContainsIgnoreCase(def.label, term) && !ContainsIgnoreCase(def.defName, term))
                {
                    continue;
                }

                counts.TryGetValue(def, out int storedCount);
                bool hasQuota = network.TryGetItemQuota(def, out int quota);
                bool allowed = network.StorageSettingsAllow(def);
                int remaining = hasQuota ? network.RemainingQuotaFor(def) : int.MaxValue;

                QuotaItemEntry entry = new QuotaItemEntry(def, storedCount, quota, hasQuota, allowed, remaining);
                if (!QuotaModeAllows(entry, mode))
                {
                    continue;
                }

                entries.Add(entry);
            }

            entries.Sort(CompareQuotaEntries);
            return entries;
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

        private static int CompareQuotaEntries(QuotaItemEntry a, QuotaItemEntry b)
        {
            int labelCompare = string.Compare(a.Def.label, b.Def.label, StringComparison.OrdinalIgnoreCase);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            return string.Compare(a.Def.defName, b.Def.defName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsQuotaCandidate(DataNetwork network, ThingDef def)
        {
            if (def.category != ThingCategory.Item)
            {
                return false;
            }

            if (!def.EverStorable(willMinifyIfPossible: false))
            {
                return false;
            }

            return network.FixedStorageSettingsAllow(def);
        }

        private static bool QuotaModeAllows(QuotaItemEntry entry, NetworkStorageQuotaMode mode)
        {
            switch (mode)
            {
                case NetworkStorageQuotaMode.Configured:
                    return entry.HasConfiguredMax;
                case NetworkStorageQuotaMode.Stored:
                    return entry.StoredCount > 0;
                case NetworkStorageQuotaMode.Disallowed:
                    return !entry.CurrentlyAllowed;
                default:
                    return true;
            }
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
