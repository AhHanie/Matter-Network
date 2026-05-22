using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    internal enum NetworkStorageSubTab
    {
        Overview,
        Power,
        ByDef,
        ByStack,
        Quotas
    }

    internal enum NetworkStorageQuotaMode
    {
        All,
        Configured,
        Stored,
        Disallowed
    }

    internal struct QuotaItemEntry
    {
        public ThingDef Def;
        public int StoredCount;
        public int ConfiguredMax;
        public bool HasConfiguredMax;
        public bool CurrentlyAllowed;
        public int Remaining;

        public QuotaItemEntry(ThingDef def, int storedCount, int configuredMax, bool hasConfiguredMax, bool currentlyAllowed, int remaining)
        {
            Def = def;
            StoredCount = storedCount;
            ConfiguredMax = configuredMax;
            HasConfiguredMax = hasConfiguredMax;
            CurrentlyAllowed = currentlyAllowed;
            Remaining = remaining;
        }
    }

    internal struct GroupedItemEntry
    {
        public ThingDef Def;
        public int TotalCount;
        public int StackEntries;

        public GroupedItemEntry(ThingDef def, int totalCount, int stackEntries)
        {
            Def = def;
            TotalCount = totalCount;
            StackEntries = stackEntries;
        }
    }

    internal struct StoredThingEntry
    {
        public Thing Thing;
        public string Label;
        public int StackCount;

        public StoredThingEntry(Thing thing)
        {
            Thing = thing;
            Label = thing.LabelCap;
            StackCount = thing.stackCount;
        }
    }

    internal sealed class NetworkStorageTabDataSnapshot
    {
        internal static readonly NetworkStorageTabDataSnapshot Empty = new NetworkStorageTabDataSnapshot(
            new List<GroupedItemEntry>(),
            new List<StoredThingEntry>(),
            0,
            0,
            0);

        internal List<GroupedItemEntry> GroupedEntries { get; }
        internal List<StoredThingEntry> StoredEntries { get; }
        internal int TotalUnits { get; }
        internal int UniqueDefCount { get; }
        internal int StoredStackCount { get; }

        internal NetworkStorageTabDataSnapshot(
            List<GroupedItemEntry> groupedEntries,
            List<StoredThingEntry> storedEntries,
            int totalUnits,
            int uniqueDefCount,
            int storedStackCount)
        {
            GroupedEntries = groupedEntries;
            StoredEntries = storedEntries;
            TotalUnits = totalUnits;
            UniqueDefCount = uniqueDefCount;
            StoredStackCount = storedStackCount;
        }
    }
}
