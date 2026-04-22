using UnityEngine;
using Verse;
using RimWorld;

namespace SK_Matter_Network
{
    public class ITab_NetworkStorage : ITab
    {
        private readonly NetworkStorageTabState state = new NetworkStorageTabState();
        private readonly NetworkStorageTabDataSource dataSource = new NetworkStorageTabDataSource();
        private readonly NetworkStorageTabActions actions = new NetworkStorageTabActions();
        private readonly NetworkStorageChromeDrawer chromeDrawer;
        private readonly NetworkStorageOverviewDrawer overviewDrawer;
        private readonly NetworkStorageItemsDrawer itemsDrawer;

        private NetworkBuildingNetworkInterface oldSelected;
        private bool snapshotDirty = true;
        private NetworkStorageTabDataSnapshot snapshot = NetworkStorageTabDataSnapshot.Empty;

        private NetworkBuildingNetworkInterface SelectedInterface => SelThing as NetworkBuildingNetworkInterface;

        public bool ItemsCached
        {
            get => !snapshotDirty;
            set => snapshotDirty = !value;
        }

        public ITab_NetworkStorage()
        {
            size = new Vector2(NetworkStorageUiConstants.WindowWidth, NetworkStorageUiConstants.WindowHeight);
            labelKey = "MN_NetworkStorageTab";

            chromeDrawer = new NetworkStorageChromeDrawer();
            overviewDrawer = new NetworkStorageOverviewDrawer(chromeDrawer, dataSource);
            itemsDrawer = new NetworkStorageItemsDrawer(chromeDrawer, dataSource, actions);
        }

        protected override void FillTab()
        {
            DataNetwork network = SelectedInterface?.ParentNetwork;
            Rect outerRect = new Rect(0f, 0f, size.x, size.y).ContractedBy(NetworkStorageUiConstants.OuterPadding);

            if (network == null)
            {
                chromeDrawer.DrawCenteredMessage(outerRect, "MN_NetworkStorageNoNetwork".Translate());
                return;
            }

            if (oldSelected != SelectedInterface)
            {
                state.Reset();
                snapshotDirty = true;
                oldSelected = SelectedInterface;
            }

            EnsureSnapshot(network);

            Rect headerRect = new Rect(outerRect.x, outerRect.y, outerRect.width, NetworkStorageUiConstants.HeaderHeight);
            Rect bodyRect = new Rect(
                outerRect.x,
                headerRect.yMax + NetworkStorageUiConstants.HeaderGap,
                outerRect.width,
                outerRect.height - NetworkStorageUiConstants.HeaderHeight - NetworkStorageUiConstants.HeaderGap);
            Rect tabContentRect = new Rect(
                bodyRect.x,
                bodyRect.y + NetworkStorageUiConstants.TabContentTopOffset,
                bodyRect.width,
                bodyRect.height - NetworkStorageUiConstants.TabContentTopOffset);

            chromeDrawer.DrawHeader(headerRect, network, dataSource.FormatItemCount);
            state.SelectedSubTab = chromeDrawer.DrawTabStrip(tabContentRect, state.SelectedSubTab);

            switch (state.SelectedSubTab)
            {
                case NetworkStorageSubTab.Overview:
                    overviewDrawer.Draw(tabContentRect, network, snapshot, state);
                    break;
                case NetworkStorageSubTab.ByDef:
                    itemsDrawer.DrawByDefTab(tabContentRect, network, snapshot, state, SelectedInterface);
                    break;
                case NetworkStorageSubTab.ByStack:
                    itemsDrawer.DrawByStackTab(tabContentRect, network, snapshot, state, SelectedInterface);
                    break;
            }
        }

        protected override void UpdateSize()
        {
            base.UpdateSize();
            size = new Vector2(NetworkStorageUiConstants.WindowWidth, NetworkStorageUiConstants.WindowHeight);
        }

        protected override void CloseTab()
        {
            if (SelectedInterface?.ParentNetwork != null)
            {
                SelectedInterface.ParentNetwork.CurrentTab = null;
            }

            oldSelected = null;
            base.CloseTab();
        }

        public override void OnOpen()
        {
            state.Reset();
            snapshotDirty = true;
            if (SelectedInterface?.ParentNetwork != null)
            {
                SelectedInterface.ParentNetwork.CurrentTab = this;
            }

            oldSelected = SelectedInterface;
        }

        public override void Notify_ClickOutsideWindow()
        {
            if (SelectedInterface?.ParentNetwork != null)
            {
                SelectedInterface.ParentNetwork.CurrentTab = null;
            }

            oldSelected = null;
        }

        private void EnsureSnapshot(DataNetwork network)
        {
            if (!snapshotDirty)
            {
                return;
            }

            snapshot = dataSource.BuildSnapshot(network);
            snapshotDirty = false;
        }
    }
}
