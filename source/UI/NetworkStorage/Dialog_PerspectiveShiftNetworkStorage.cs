using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace SK_Matter_Network
{
    public sealed class Dialog_PerspectiveShiftNetworkStorage : Window
    {
        private const int ItemsPerRow = 6;
        private const float CellSpacing = 10f;
        private const float IconPadding = 10f;
        private const float HeaderHeight = 64f;
        private const float ToolbarHeight = 34f;
        private const float BottomHeight = 76f;
        private const float CellSize = 86f;
        private const float InventoryHeaderHeight = 24f;
        private const float InventoryBarHeight = 94f;
        private const float InventoryGap = 10f;
        private const float InventoryCellSize = 76f;
        private const float InventoryCellSpacing = 10f;

        private enum SelectedSource
        {
            None,
            Network,
            Inventory,
            Held
        }

        private struct InventoryDisplayItem
        {
            public Thing Thing;
            public bool IsHeld;

            public InventoryDisplayItem(Thing thing, bool isHeld)
            {
                Thing = thing;
                IsHeld = isHeld;
            }
        }

        private readonly NetworkBuildingNetworkInterface selectedInterface;
        private readonly Pawn pawn;
        private readonly NetworkStorageTabDataSource dataSource = new NetworkStorageTabDataSource();
        private readonly NetworkStorageChromeDrawer chromeDrawer = new NetworkStorageChromeDrawer();

        private Thing selectedItem;
        private SelectedSource selectedSource;
        private Thing selectedInventoryItem;
        private Vector2 scrollPosition;
        private Vector2 inventoryScrollPosition;
        private string searchText = string.Empty;

        public override Vector2 InitialSize => new Vector2(700f, 760f);

        private DataNetwork Network => selectedInterface.ParentNetwork;

        public Dialog_PerspectiveShiftNetworkStorage(NetworkBuildingNetworkInterface selectedInterface, Pawn pawn)
        {
            this.selectedInterface = selectedInterface;
            this.pawn = pawn;
            closeOnClickedOutside = true;
            doCloseButton = false;
            doCloseX = true;
            absorbInputAroundWindow = false;
            forcePause = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            DataNetwork network = Network;
            Rect headerRect = new Rect(inRect.x, inRect.y, inRect.width, HeaderHeight);
            Rect toolbarRect = new Rect(inRect.x, headerRect.yMax + 8f, inRect.width, ToolbarHeight);
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - BottomHeight, inRect.width, BottomHeight);
            float inventoryHeight = InventoryHeaderHeight + InventoryBarHeight;
            Rect inventoryRect = new Rect(inRect.x, bottomRect.y - InventoryGap - inventoryHeight, inRect.width, inventoryHeight);
            Rect gridRect = new Rect(inRect.x, toolbarRect.yMax + 8f, inRect.width, inventoryRect.y - toolbarRect.yMax - InventoryGap - 8f);

            DrawHeader(headerRect, network);

            if (!IsUsableNetwork(network))
            {
                chromeDrawer.DrawPanel(gridRect, strong: false);
                string message = network == null
                    ? "MN_NetworkStorageNoNetwork".Translate().ToString()
                    : network.HasActiveController
                        ? "MN_NetworkStorageOffline".Translate(network.PowerModeLabel).ToString()
                        : "MN_NetworkStorageNoController".Translate().ToString();
                chromeDrawer.DrawCenteredMessage(gridRect.ContractedBy(10f), message, GameFont.Medium, NetworkStorageUiConstants.WarningColor);
                DrawInventoryBar(inventoryRect);
                DrawActionBar(bottomRect);
                return;
            }

            DrawSearchToolbar(toolbarRect, network);
            DrawItemGrid(gridRect, network);
            DrawInventoryBar(inventoryRect);
            DrawActionBar(bottomRect);
        }

        private void DrawHeader(Rect rect, DataNetwork network)
        {
            chromeDrawer.DrawPanel(rect, strong: true);
            Rect innerRect = rect.ContractedBy(10f);
            Rect statusRect = new Rect(
                innerRect.xMax - 150f,
                innerRect.y + ((innerRect.height - NetworkStorageUiConstants.StatusPillHeight) / 2f),
                150f,
                NetworkStorageUiConstants.StatusPillHeight);
            Rect summaryRect = new Rect(statusRect.x - 220f, statusRect.y, 210f, statusRect.height);
            Rect titleRect = new Rect(innerRect.x, innerRect.y + ((innerRect.height - 30f) / 2f), summaryRect.x - innerRect.x - 12f, 30f);

            Text.Font = GameFont.Medium;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(titleRect, "MN_PSNetworkStorageTitle".Translate());

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
            string summary = network == null
                ? string.Empty
                : "MN_NetworkStorageHeaderSummary".Translate(dataSource.FormatItemCount(network.UsedBytes), dataSource.FormatItemCount(network.TotalCapacityBytes)).ToString();
            Widgets.Label(summaryRect, summary);
            Text.Anchor = TextAnchor.UpperLeft;

            DrawStatusPill(statusRect, network);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawStatusPill(Rect rect, DataNetwork network)
        {
            Color color = NetworkStorageUiConstants.OkColor;
            string label = "MN_NetworkStorageStatusOnline".Translate().ToString();

            if (network == null)
            {
                color = NetworkStorageUiConstants.WarningColor;
                label = "MN_NetworkStorageNoNetwork".Translate().ToString();
            }
            else if (!network.HasActiveController)
            {
                color = NetworkStorageUiConstants.WarningColor;
                label = "MN_NetworkStorageNoController".Translate().ToString();
            }
            else if (!network.IsOperational)
            {
                color = NetworkStorageUiConstants.ErrorColor;
                label = "MN_NetworkStorageStatusOffline".Translate().ToString();
            }
            else if (network.PowerMode == NetworkPowerMode.ReservePowered)
            {
                color = NetworkStorageUiConstants.WarningColor;
                label = "MN_NetworkStorageStatusReserve".Translate().ToString();
            }

            Color previousColor = GUI.color;
            Widgets.DrawBoxSolid(rect, new Color(color.r, color.g, color.b, 0.22f));
            GUI.color = color;
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, color);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previousColor;
        }

        private void DrawSearchToolbar(Rect rect, DataNetwork network)
        {
            Rect labelRect = new Rect(rect.x, rect.y + 5f, 56f, NetworkStorageUiConstants.SearchHeight);
            Rect summaryRect = new Rect(rect.xMax - 190f, rect.y + 5f, 190f, NetworkStorageUiConstants.SearchHeight);
            Rect searchRect = new Rect(labelRect.xMax + 6f, rect.y + 3f, rect.width - labelRect.width - summaryRect.width - 18f, NetworkStorageUiConstants.SearchHeight);

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, "MN_NetworkStorageSearchLabel".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            string updated = Widgets.TextField(searchRect, searchText);
            if (updated != searchText)
            {
                searchText = updated;
                scrollPosition = Vector2.zero;
            }

            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(summaryRect, "MN_NetworkStorageByStackSummary".Translate(BuildFilteredItems(network).Count));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawItemGrid(Rect rect, DataNetwork network)
        {
            chromeDrawer.DrawPanel(rect, strong: false);
            Rect innerRect = rect.ContractedBy(10f);
            List<Thing> items = BuildFilteredItems(network);
            int tileCount = items.Count;

            if (tileCount == 0)
            {
                chromeDrawer.DrawCenteredMessage(innerRect, string.IsNullOrWhiteSpace(searchText)
                    ? "MN_NetworkStorageNoStoredStacks".Translate()
                    : "MN_NetworkStorageNoItemsFiltered".Translate(), GameFont.Small, NetworkStorageUiConstants.SecondaryTextColor);
                return;
            }

            int columns = Math.Max(1, Math.Min(ItemsPerRow, Mathf.FloorToInt((innerRect.width + CellSpacing) / (CellSize + CellSpacing))));
            int rows = Mathf.CeilToInt(tileCount / (float)columns);
            float viewHeight = Mathf.Max(innerRect.height - 4f, rows * (CellSize + CellSpacing) - CellSpacing);
            Rect viewRect = new Rect(0f, 0f, innerRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(innerRect, ref scrollPosition, viewRect);
            for (int i = 0; i < tileCount; i++)
            {
                int row = i / columns;
                int column = i % columns;
                Rect cellRect = new Rect(column * (CellSize + CellSpacing), row * (CellSize + CellSpacing), CellSize, CellSize);
                DrawItemCell(cellRect, items[i]);
            }
            Widgets.EndScrollView();
        }

        private void DrawItemCell(Rect rect, Thing item)
        {
            chromeDrawer.DrawPanel(rect, strong: false);
            Widgets.DrawHighlightIfMouseover(rect);
            if (selectedSource == SelectedSource.Network && selectedItem == item)
            {
                Widgets.DrawBoxSolidWithOutline(rect, Color.clear, NetworkStorageUiConstants.AccentColor);
            }

            Rect iconRect = new Rect(rect.x + IconPadding, rect.y + IconPadding, rect.width - (IconPadding * 2f), rect.height - (IconPadding * 2f));
            Widgets.ThingIcon(iconRect, item);

            if (item.stackCount > 1 || item.def.stackLimit > 1)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width - 4f, rect.height - 3f), item.stackCount.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            TooltipHandler.TipRegion(rect, item.LabelCap + "\n" + dataSource.BuildThingMetadata(item));

            if (Mouse.IsOver(rect) && Event.current.type == EventType.MouseDown && (Event.current.button == 0 || Event.current.button == 1))
            {
                if (selectedSource == SelectedSource.Network && selectedItem == item)
                {
                    ClearSelection();
                }
                else
                {
                    SelectNetworkItem(item);
                }

                Event.current.Use();
            }
        }

        private void DrawInventoryBar(Rect rect)
        {
            chromeDrawer.DrawPanel(rect, strong: false);

            Rect labelRect = new Rect(rect.x + 10f, rect.y + 3f, rect.width - 20f, InventoryHeaderHeight - 4f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
            Widgets.Label(labelRect, "MN_PSNetworkStorageInventoryLabel".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            Rect stripRect = new Rect(rect.x + 10f, rect.y + InventoryHeaderHeight, rect.width - 20f, InventoryBarHeight);
            List<InventoryDisplayItem> items = BuildInventoryItems();

            if (items.Count == 0)
            {
                chromeDrawer.DrawCenteredMessage(stripRect, "MN_PSNetworkStorageNoInventoryItems".Translate(), GameFont.Small, NetworkStorageUiConstants.SecondaryTextColor);
                return;
            }

            float contentWidth = items.Count * (InventoryCellSize + InventoryCellSpacing) - InventoryCellSpacing;
            Rect viewRect = new Rect(0f, 0f, Mathf.Max(stripRect.width - 16f, contentWidth), InventoryCellSize);

            Widgets.BeginScrollView(stripRect, ref inventoryScrollPosition, viewRect);
            for (int i = 0; i < items.Count; i++)
            {
                Rect cellRect = new Rect(i * (InventoryCellSize + InventoryCellSpacing), 0f, InventoryCellSize, InventoryCellSize);
                DrawInventoryItemCell(cellRect, items[i]);
            }
            Widgets.EndScrollView();
        }

        private void DrawInventoryItemCell(Rect rect, InventoryDisplayItem displayItem)
        {
            Thing item = displayItem.Thing;
            chromeDrawer.DrawPanel(rect, strong: false);
            Widgets.DrawHighlightIfMouseover(rect);

            bool selected = selectedInventoryItem == item
                && ((displayItem.IsHeld && selectedSource == SelectedSource.Held)
                    || (!displayItem.IsHeld && selectedSource == SelectedSource.Inventory));
            if (selected)
            {
                Widgets.DrawBoxSolidWithOutline(rect, Color.clear, NetworkStorageUiConstants.AccentColor);
            }

            Rect iconRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
            Widgets.ThingIcon(iconRect, item);

            if (displayItem.IsHeld)
            {
                Rect heldRect = new Rect(rect.x + 4f, rect.y + 3f, rect.width - 8f, 18f);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = NetworkStorageUiConstants.SecondaryTextColor;
                Widgets.Label(heldRect, "MN_PSNetworkStorageHeldIndicator".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            if (item.stackCount > 1 || item.def.stackLimit > 1)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.LowerRight;
                GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width - 4f, rect.height - 3f), item.stackCount.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            string tooltip = item.LabelCap + "\n" + dataSource.BuildThingMetadata(item);
            if (displayItem.IsHeld)
            {
                tooltip = "MN_PSNetworkStorageHeldIndicator".Translate() + "\n" + tooltip;
            }

            TooltipHandler.TipRegion(rect, tooltip);

            if (Mouse.IsOver(rect) && Event.current.type == EventType.MouseDown && (Event.current.button == 0 || Event.current.button == 1))
            {
                if (selected)
                {
                    ClearSelection();
                }
                else
                {
                    SelectInventoryItem(item, displayItem.IsHeld);
                }

                Event.current.Use();
            }
        }

        private void DrawActionBar(Rect rect)
        {
            float spacing = 10f;
            float buttonWidth = (rect.width - (spacing * 3f)) / 4f;
            Rect keepRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect holdRect = new Rect(keepRect.xMax + spacing, rect.y, buttonWidth, rect.height);
            Rect equipRect = new Rect(holdRect.xMax + spacing, rect.y, buttonWidth, rect.height);
            Rect depositRect = new Rect(equipRect.xMax + spacing, rect.y, buttonWidth, rect.height);

            bool hasSelection = HasSelectedNetworkItem();
            bool canEquip = hasSelection && IsWearOrEquipItem(selectedItem);
            string equipLabel = hasSelection && selectedItem is Apparel
                ? "MN_PSNetworkStorageWear".Translate().ToString()
                : "MN_PSNetworkStorageEquip".Translate().ToString();

            DrawTextActionButton(keepRect, "MN_PSNetworkStorageKeep".Translate(), hasSelection, delegate(int button)
            {
                int count = button == 1 ? 1 : selectedItem.stackCount;
                TryMoveSelectedToInventory(count);
            });

            DrawTextActionButton(holdRect, "MN_PSNetworkStorageHold".Translate(), hasSelection, delegate(int button)
            {
                int count = button == 1 ? 1 : selectedItem.stackCount;
                if (TryMoveSelectedToCarryTracker(count))
                {
                    Close();
                }
            });

            DrawTextActionButton(equipRect, equipLabel, canEquip, delegate(int button)
            {
                TryWearOrEquipSelectedItem();
            });

            DrawTextActionButton(depositRect, "MN_PSNetworkStorageDeposit".Translate(), CanDepositSelectedItem(), delegate(int button)
            {
                TryDepositSelectedItem(button == 1 ? 1 : selectedInventoryItem.stackCount);
            });
        }

        private void DrawTextActionButton(Rect rect, string label, bool enabled, Action<int> onClick)
        {
            chromeDrawer.DrawPanel(rect, strong: false);
            Color previousColor = GUI.color;
            GUI.color = enabled ? NetworkStorageUiConstants.PrimaryTextColor : new Color(1f, 1f, 1f, 0.35f);

            if (enabled && Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
                if (Event.current.type == EventType.MouseUp && (Event.current.button == 0 || Event.current.button == 1))
                {
                    onClick(Event.current.button);
                    Event.current.Use();
                }
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect.ContractedBy(6f), label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = previousColor;
        }

        private List<Thing> BuildFilteredItems(DataNetwork network)
        {
            List<Thing> items = new List<Thing>();

            foreach (Thing thing in network.ActiveController.innerContainer.InnerListForReading)
            {
                if (!thing.Destroyed && MatchesSearch(thing))
                {
                    items.Add(thing);
                }
            }

            items.Sort(CompareThings);
            return items;
        }

        private List<InventoryDisplayItem> BuildInventoryItems()
        {
            List<InventoryDisplayItem> items = new List<InventoryDisplayItem>();

            Thing carried = pawn.carryTracker.CarriedThing;
            if (carried != null && !carried.Destroyed && !(carried is Pawn) && !(carried is Corpse))
            {
                items.Add(new InventoryDisplayItem(carried, true));
            }

            foreach (Thing thing in pawn.inventory.innerContainer.InnerListForReading)
            {
                if (!thing.Destroyed)
                {
                    items.Add(new InventoryDisplayItem(thing, false));
                }
            }

            return items;
        }

        private bool MatchesSearch(Thing thing)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            string term = searchText.Trim();
            return ContainsIgnoreCase(thing.LabelCap, term)
                || ContainsIgnoreCase(thing.def.label, term)
                || ContainsIgnoreCase(thing.def.defName, term)
                || ContainsIgnoreCase(dataSource.BuildThingMetadata(thing), term);
        }

        private static int CompareThings(Thing a, Thing b)
        {
            int labelCompare = string.Compare(a.LabelCap, b.LabelCap, StringComparison.OrdinalIgnoreCase);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            return b.stackCount.CompareTo(a.stackCount);
        }

        private static bool ContainsIgnoreCase(string value, string term)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(term))
            {
                return false;
            }

            return value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ClearSelection()
        {
            selectedSource = SelectedSource.None;
            selectedItem = null;
            selectedInventoryItem = null;
        }

        private void SelectNetworkItem(Thing item)
        {
            selectedSource = SelectedSource.Network;
            selectedItem = item;
            selectedInventoryItem = null;
        }

        private void SelectInventoryItem(Thing item, bool isHeld)
        {
            selectedSource = isHeld ? SelectedSource.Held : SelectedSource.Inventory;
            selectedInventoryItem = item;
            selectedItem = null;
        }

        private bool HasSelectedNetworkItem()
        {
            DataNetwork network = Network;
            return selectedSource == SelectedSource.Network
                && selectedItem != null
                && !selectedItem.Destroyed
                && IsUsableNetwork(network)
                && network.ActiveController.innerContainer.Contains(selectedItem);
        }

        private bool HasSelectedInventoryItem()
        {
            return selectedSource == SelectedSource.Inventory
                && selectedInventoryItem != null
                && !selectedInventoryItem.Destroyed
                && pawn.inventory.innerContainer.Contains(selectedInventoryItem);
        }

        private bool HasSelectedHeldItem()
        {
            return selectedSource == SelectedSource.Held
                && selectedInventoryItem != null
                && !selectedInventoryItem.Destroyed
                && pawn.carryTracker.CarriedThing == selectedInventoryItem;
        }

        private bool HasSelectedDepositableItem()
        {
            return IsUsableNetwork(Network) && (HasSelectedInventoryItem() || HasSelectedHeldItem());
        }

        private static int MaxPickupStackCount(Thing item)
        {
            return Math.Min(item.stackCount, Math.Max(1, item.def.stackLimit));
        }

        private Thing RemoveFromNetwork(Thing item, int requestedCount)
        {
            DataNetwork network = Network;
            if (!IsUsableNetwork(network) || item.Destroyed)
            {
                return null;
            }

            int count = Math.Min(requestedCount, item.stackCount);
            if (count <= 0)
            {
                return null;
            }

            Thing removed;
            if (count >= item.stackCount)
            {
                if (!network.ActiveController.innerContainer.Remove(item))
                {
                    return null;
                }

                removed = item;
            }
            else
            {
                removed = item.SplitOff(count);
            }

            network.MarkBytesDirty();
            ClearItemReservations(removed);
            removed.def.soundPickup?.PlayOneShot(pawn);
            return removed;
        }

        private bool TryAddToNetwork(Thing item)
        {
            DataNetwork network = Network;
            if (!IsUsableNetwork(network) || item.Destroyed)
            {
                return false;
            }

            bool added = network.ActiveController.innerContainer.TryAddExistingNetworkItem(item, canMergeWithExistingStacks: true);
            if (added)
            {
                network.MarkBytesDirty();
            }

            return added;
        }

        private bool CanInsertIntoNetwork(Thing item, int requestedCount, out int acceptedCount)
        {
            acceptedCount = 0;
            DataNetwork network = Network;
            if (!IsUsableNetwork(network) || item.Destroyed)
            {
                Messages.Message("MN_PSNetworkStorageOffline".Translate(), selectedInterface, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!network.StorageSettingsAllow(item))
            {
                Messages.Message("MN_PSNetworkStorageDoesNotAccept".Translate(item.LabelCap), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            acceptedCount = Math.Min(Math.Min(requestedCount, item.stackCount), network.CanAcceptCount(item));
            if (acceptedCount <= 0)
            {
                Messages.Message("MN_PSNetworkStorageNoCapacity".Translate(item.LabelCap), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return true;
        }

        private bool TryMoveSelectedToInventory(int requestedCount)
        {
            if (!HasSelectedNetworkItem())
            {
                return false;
            }

            int count = Math.Min(requestedCount, MaxPickupStackCount(selectedItem));
            int maxCount = MassUtility.CountToPickUpUntilOverEncumbered(pawn, selectedItem);
            count = Math.Min(count, maxCount);
            if (count <= 0)
            {
                Messages.Message("MN_PSNetworkStorageCannotCarryMore".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            bool tookAll = count >= selectedItem.stackCount;
            Thing toAdd = RemoveFromNetwork(selectedItem, count);
            if (toAdd == null)
            {
                return false;
            }

            if (pawn.inventory.innerContainer.TryAdd(toAdd, true))
            {
                if (tookAll)
                {
                    ClearSelection();
                }

                toAdd.def.soundDrop?.PlayOneShot(pawn);
                return true;
            }

            TryAddToNetwork(toAdd);
            return false;
        }

        private bool TryMoveSelectedToCarryTracker(int requestedCount)
        {
            if (!HasSelectedNetworkItem())
            {
                return false;
            }

            if (pawn.carryTracker.CarriedThing != null)
            {
                Messages.Message("MN_PSNetworkStorageAlreadyHolding".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            int count = MaxCarryCountForSelected(requestedCount);
            if (count <= 0)
            {
                Messages.Message("MN_PSNetworkStorageCannotCarryMore".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            bool tookAll = count >= selectedItem.stackCount;
            Thing toCarry = RemoveFromNetwork(selectedItem, count);
            if (toCarry == null)
            {
                return false;
            }

            int originalCount = toCarry.stackCount;
            int carried = pawn.carryTracker.TryStartCarry(toCarry, originalCount, reserve: false);
            if (carried > 0)
            {
                if (carried < originalCount && !toCarry.Destroyed)
                {
                    TryAddToNetwork(toCarry);
                    selectedItem = toCarry;
                }
                else if (tookAll)
                {
                    ClearSelection();
                }

                toCarry.def.soundDrop?.PlayOneShot(pawn);
                return true;
            }

            TryAddToNetwork(toCarry);
            return false;
        }

        private int MaxCarryCountForSelected(int requestedCount)
        {
            int maxStackCount = selectedItem.def.stackLimit > 0 ? selectedItem.def.stackLimit : selectedItem.stackCount;
            int maxMassCount = MassUtility.CountToPickUpUntilOverEncumbered(pawn, selectedItem);
            return Math.Min(Math.Min(requestedCount, selectedItem.stackCount), Math.Min(maxStackCount, maxMassCount));
        }

        private bool TryWearOrEquipSelectedItem()
        {
            if (!HasSelectedNetworkItem() || !IsWearOrEquipItem(selectedItem))
            {
                return false;
            }

            Job unusedJob;
            if (!TryMakeWearOrEquipJob(pawn, selectedItem, out unusedJob))
            {
                return false;
            }

            Thing item = RemoveFromNetwork(selectedItem, selectedItem.stackCount);
            if (item == null)
            {
                return false;
            }

            ClearSelection();
            if (!item.Spawned)
            {
                GenSpawn.Spawn(item, pawn.Position, pawn.Map, WipeMode.Vanish);
            }

            if (TryMakeWearOrEquipJob(pawn, item, out Job job))
            {
                pawn.jobs.TryTakeOrderedJob(job);
                Close();
                return true;
            }

            return false;
        }

        private bool CanDepositSelectedItem()
        {
            return HasSelectedDepositableItem();
        }

        private bool TryDepositSelectedItem(int requestedCount)
        {
            if (HasSelectedHeldItem())
            {
                return TryDepositHeldItem(requestedCount);
            }

            if (HasSelectedInventoryItem())
            {
                return TryDepositInventoryItem(requestedCount);
            }

            return false;
        }

        private bool TryDepositInventoryItem(int requestedCount)
        {
            if (!HasSelectedInventoryItem())
            {
                return false;
            }

            Thing item = selectedInventoryItem;
            if (!CanInsertIntoNetwork(item, requestedCount, out int acceptedCount))
            {
                return false;
            }

            DataNetwork network = Network;
            int moved = pawn.inventory.innerContainer.TryTransferToContainer(item, network.ActiveController.innerContainer, acceptedCount);
            if (moved > 0)
            {
                network.MarkBytesDirty();
                item.def.soundDrop?.PlayOneShot(pawn);

                if (!pawn.inventory.innerContainer.Contains(item))
                {
                    ClearSelection();
                }

                return true;
            }

            return false;
        }

        private bool TryDepositHeldItem(int requestedCount)
        {
            Thing carried = pawn.carryTracker.CarriedThing;
            if (!HasSelectedHeldItem() || carried == null || carried.Destroyed)
            {
                return false;
            }

            if (!CanInsertIntoNetwork(carried, requestedCount, out int acceptedCount))
            {
                return false;
            }

            DataNetwork network = Network;
            int requestedToMove = Math.Min(requestedCount, carried.stackCount);
            int moved = pawn.carryTracker.innerContainer.TryTransferToContainer(carried, network.ActiveController.innerContainer, acceptedCount);
            if (moved > 0)
            {
                network.MarkBytesDirty();
                DropCarriedOverflowAtInterface(requestedToMove - moved);
                carried.def.soundDrop?.PlayOneShot(pawn);

                if (pawn.carryTracker.CarriedThing != carried)
                {
                    ClearSelection();
                }

                return true;
            }

            return false;
        }

        private void DropCarriedOverflowAtInterface(int count)
        {
            if (count <= 0)
            {
                return;
            }

            Thing carried = pawn.carryTracker.CarriedThing;
            int dropCount = Math.Min(count, carried.stackCount);
            if (dropCount > 0)
            {
                pawn.carryTracker.TryDropCarriedThing(selectedInterface.Position, dropCount, ThingPlaceMode.Near, out Thing _);
            }
        }

        private void ClearItemReservations(Thing item)
        {
            HashSet<Pawn> reservers = new HashSet<Pawn>();
            pawn.Map.reservationManager.ReserversOf(item, reservers);
            foreach (Pawn reserver in reservers.ToList())
            {
                if (reserver != pawn)
                {
                    reserver.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }

            pawn.Map.reservationManager.ReleaseAllForTarget(item);
            pawn.Map.physicalInteractionReservationManager.ReleaseAllForTarget(item);
        }

        private static bool IsUsableNetwork(DataNetwork network)
        {
            return network != null && network.IsOperational;
        }

        private static bool IsWearOrEquipItem(Thing item)
        {
            return item is Apparel
                || item is ThingWithComps thingWithComps && thingWithComps.def.equipmentType == EquipmentType.Primary;
        }

        private static bool TryMakeWearOrEquipJob(Pawn pawn, Thing item, out Job job)
        {
            job = null;
            if (item is Apparel apparel)
            {
                if (!apparel.PawnCanWear(pawn, ignoreGender: true) || !ApparelUtility.HasPartsToWear(pawn, apparel.def))
                {
                    return false;
                }

                job = JobMaker.MakeJob(JobDefOf.Wear, item);
                return true;
            }

            if (item is ThingWithComps thingWithComps && thingWithComps.def.equipmentType == EquipmentType.Primary)
            {
                if (thingWithComps.def.IsWeapon && pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    return false;
                }

                if (thingWithComps.def.IsRangedWeapon && pawn.WorkTagIsDisabled(WorkTags.Shooting))
                {
                    return false;
                }

                if (!EquipmentUtility.CanEquip(item, pawn, out string _))
                {
                    return false;
                }

                job = JobMaker.MakeJob(JobDefOf.Equip, item);
                return true;
            }

            return false;
        }
    }
}
