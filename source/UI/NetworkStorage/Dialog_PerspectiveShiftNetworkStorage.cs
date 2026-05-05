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

        private readonly NetworkBuildingNetworkInterface selectedInterface;
        private readonly Pawn pawn;
        private readonly NetworkStorageTabDataSource dataSource = new NetworkStorageTabDataSource();
        private readonly NetworkStorageChromeDrawer chromeDrawer = new NetworkStorageChromeDrawer();

        private Thing selectedItem;
        private Vector2 scrollPosition;
        private string searchText = string.Empty;

        public override Vector2 InitialSize => new Vector2(700f, 640f);

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
            Rect gridRect = new Rect(inRect.x, toolbarRect.yMax + 8f, inRect.width, bottomRect.y - toolbarRect.yMax - 16f);

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
                DrawActionBar(bottomRect);
                return;
            }

            DrawSearchToolbar(toolbarRect, network);
            DrawItemGrid(gridRect, network);
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
            Rect titleRect = new Rect(innerRect.x, innerRect.y + 1f, summaryRect.x - innerRect.x - 12f, 26f);
            Rect subtitleRect = new Rect(innerRect.x, titleRect.yMax, titleRect.width, 18f);

            Text.Font = GameFont.Medium;
            GUI.color = NetworkStorageUiConstants.PrimaryTextColor;
            Widgets.Label(titleRect, "MN_PSNetworkStorageTitle".Translate());

            Text.Font = GameFont.Small;
            GUI.color = NetworkStorageUiConstants.MutedTextColor;
            Widgets.Label(subtitleRect, selectedInterface.LabelCap);

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
            if (selectedItem == item)
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
                selectedItem = item;
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

            bool hasSelection = HasSelectedItem();
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

            DrawTextActionButton(depositRect, "MN_PSNetworkStorageDepositCarried".Translate(), CanDepositCarriedItem(), delegate(int button)
            {
                TryDepositCarriedItem(button == 1 ? 1 : int.MaxValue);
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

        private bool HasSelectedItem()
        {
            DataNetwork network = Network;
            return selectedItem != null && !selectedItem.Destroyed && IsUsableNetwork(network) && network.ActiveController.innerContainer.Contains(selectedItem);
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

            bool added = network.ActiveController.innerContainer.TryAdd(item, canMergeWithExistingStacks: true);
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

            if (!network.AcceptsItem(item))
            {
                Messages.Message("MN_PSNetworkStorageDoesNotAccept".Translate(item.LabelCap), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            int available = Math.Max(0, network.TotalCapacityBytes - network.UsedBytes);
            acceptedCount = Math.Min(Math.Min(requestedCount, item.stackCount), available);
            if (acceptedCount <= 0)
            {
                Messages.Message("MN_PSNetworkStorageNoCapacity".Translate(item.LabelCap), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return true;
        }

        private bool TryMoveSelectedToInventory(int requestedCount)
        {
            if (!HasSelectedItem())
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
                    selectedItem = null;
                }

                toAdd.def.soundDrop?.PlayOneShot(pawn);
                return true;
            }

            TryAddToNetwork(toAdd);
            return false;
        }

        private bool TryMoveSelectedToCarryTracker(int requestedCount)
        {
            if (!HasSelectedItem())
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
                    selectedItem = null;
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
            if (!HasSelectedItem() || !IsWearOrEquipItem(selectedItem))
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

            selectedItem = null;
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

        private bool CanDepositCarriedItem()
        {
            Thing carried = pawn.carryTracker.CarriedThing;
            return carried != null && !carried.Destroyed && !(carried is Pawn) && !(carried is Corpse) && IsUsableNetwork(Network);
        }

        private bool TryDepositCarriedItem(int requestedCount)
        {
            Thing carried = pawn.carryTracker.CarriedThing;
            if (carried == null || carried.Destroyed)
            {
                return false;
            }

            if (!CanInsertIntoNetwork(carried, requestedCount, out int acceptedCount))
            {
                return false;
            }

            DataNetwork network = Network;
            int moved = pawn.carryTracker.innerContainer.TryTransferToContainer(carried, network.ActiveController.innerContainer, acceptedCount);
            if (moved > 0)
            {
                network.MarkBytesDirty();
                carried.def.soundDrop?.PlayOneShot(pawn);
                return true;
            }

            return false;
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
