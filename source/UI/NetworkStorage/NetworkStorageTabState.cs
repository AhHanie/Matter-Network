using UnityEngine;

namespace SK_Matter_Network
{
    internal sealed class NetworkStorageTabState
    {
        internal string ByDefSearchText { get; set; } = string.Empty;
        internal string ByStackSearchText { get; set; } = string.Empty;
        internal Vector2 OverviewScrollPosition = Vector2.zero;
        internal Vector2 ByDefScrollPosition = Vector2.zero;
        internal Vector2 ByStackScrollPosition = Vector2.zero;
        internal NetworkStorageSubTab SelectedSubTab { get; set; } = NetworkStorageSubTab.Overview;

        internal void Reset()
        {
            ByDefSearchText = string.Empty;
            ByStackSearchText = string.Empty;
            OverviewScrollPosition = Vector2.zero;
            ByDefScrollPosition = Vector2.zero;
            ByStackScrollPosition = Vector2.zero;
            SelectedSubTab = NetworkStorageSubTab.Overview;
        }
    }
}
