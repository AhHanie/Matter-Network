using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    internal sealed class NetworkStorageTabState
    {
        private string quotaSearchText = string.Empty;
        private NetworkStorageQuotaMode quotaMode = NetworkStorageQuotaMode.All;
        private bool quotaEntriesDirty = true;
        private List<QuotaItemEntry> cachedQuotaEntries = new List<QuotaItemEntry>();

        internal string ByDefSearchText { get; set; } = string.Empty;
        internal string ByStackSearchText { get; set; } = string.Empty;
        internal string QuotaSearchText
        {
            get => quotaSearchText;
            set
            {
                if (quotaSearchText == value)
                {
                    return;
                }

                quotaSearchText = value;
                InvalidateQuotaEntries();
            }
        }

        internal Vector2 OverviewScrollPosition = Vector2.zero;
        internal Vector2 ByDefScrollPosition = Vector2.zero;
        internal Vector2 ByStackScrollPosition = Vector2.zero;
        internal Vector2 QuotaScrollPosition = Vector2.zero;
        internal NetworkStorageQuotaMode QuotaMode
        {
            get => quotaMode;
            set
            {
                if (quotaMode == value)
                {
                    return;
                }

                quotaMode = value;
                InvalidateQuotaEntries();
            }
        }

        internal Dictionary<ThingDef, string> QuotaInputBuffers { get; } = new Dictionary<ThingDef, string>();
        internal NetworkStorageSubTab SelectedSubTab { get; set; } = NetworkStorageSubTab.Overview;

        internal List<QuotaItemEntry> GetQuotaEntries(NetworkStorageTabDataSource dataSource, DataNetwork network)
        {
            if (quotaEntriesDirty)
            {
                cachedQuotaEntries = dataSource.BuildQuotaEntries(network, quotaSearchText, quotaMode);
                quotaEntriesDirty = false;
            }

            return cachedQuotaEntries;
        }

        internal void InvalidateQuotaEntries()
        {
            quotaEntriesDirty = true;
        }

        internal void Reset()
        {
            ByDefSearchText = string.Empty;
            ByStackSearchText = string.Empty;
            quotaSearchText = string.Empty;
            OverviewScrollPosition = Vector2.zero;
            ByDefScrollPosition = Vector2.zero;
            ByStackScrollPosition = Vector2.zero;
            QuotaScrollPosition = Vector2.zero;
            quotaMode = NetworkStorageQuotaMode.All;
            QuotaInputBuffers.Clear();
            InvalidateQuotaEntries();
            SelectedSubTab = NetworkStorageSubTab.Overview;
        }
    }
}
