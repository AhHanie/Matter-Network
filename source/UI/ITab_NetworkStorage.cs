using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LessUI;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public class ITab_NetworkStorage : ITab
    {
        private StrongBox<string> searchTextBox = new StrongBox<string>("");
        private const float WindowWidth = 900f;
        private const float WindowHeight = 600f;
        private const float LeftColumnWidth = 90f;
        private const float ItemSlotSize = 75f;
        private const float Padding = 10f;
        private const float ItemButtonHeight = 60f;
        private StrongBox<Vector2> scrollPosition;
        private List<Button> renderedButtons;

        private NetworkBuildingNetworkInterface SelectedInterface => SelThing as NetworkBuildingNetworkInterface;
        private NetworkBuildingNetworkInterface oldSelected;
        private bool itemsCached;
        List<KeyValuePair<ThingDef, int>> cachedItems;

        public bool ItemsCached
        {
            get => itemsCached; 
            set => itemsCached = value;
        }

        public ITab_NetworkStorage()
        {
            size = new Vector2(WindowWidth, WindowHeight);
            labelKey = "MN_NetworkStorageTab";
            scrollPosition = new StrongBox<Vector2>(Vector2.zero);
            itemsCached = false;
            renderedButtons = new List<Button>();
            oldSelected = null;
        }

        protected override void FillTab()
        {
            if (SelectedInterface == null || SelectedInterface.ParentNetwork == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0, 0, size.x, size.y), "No network connected");
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            // Used for when switching between different network interfaces on different networks without actually closing the tab GUI
            if (oldSelected != SelectedInterface)
            {
                itemsCached = false;
            }

            Rect mainRect = new Rect(0, 0, size.x, size.y).ContractedBy(Padding);

            Dictionary<ThingDef, int> networkItems = SelectedInterface.ParentNetwork.ItemDefToStackCount;
            List<KeyValuePair<ThingDef, int>> filteredItems = FilterItems(networkItems);
            DrawUI(mainRect, filteredItems);
        }

        private void DrawUI(Rect rect, List<KeyValuePair<ThingDef, int>> items)
        {
            Rect leftColumnRect = new Rect(rect.x, rect.y, LeftColumnWidth, rect.height);

            Rect contentRect = new Rect(
                rect.x + LeftColumnWidth + Padding,
                rect.y,
                rect.width - LeftColumnWidth - Padding,
                rect.height
            );

            DrawLeftColumn(leftColumnRect);

            DrawContentArea(contentRect, items);
        }

        private void DrawLeftColumn(Rect rect)
        {
            Canvas canvas = new Canvas(rect: rect);
            Stack stack = new Stack(widthMode: SizeMode.Fill, verticalSpacing: 5f);

            stack.AddChild(new Label("Controls"));
            // TODO: Future buttons will be added here

            canvas.AddChild(stack);
            canvas.Render();
        }

        private void DrawContentArea(Rect rect, List<KeyValuePair<ThingDef, int>> items)
        {
            Canvas canvas = new Canvas(rect: rect);
            Stack mainStack = new Stack(widthMode: SizeMode.Fill, heightMode: SizeMode.Fill, verticalSpacing: Padding);

            Row searchRow = new Row(horizontalSpacing: 5f, widthMode: SizeMode.Fill, heightMode: SizeMode.Content);
            searchRow.AddChild(new Label("Search:", widthMode: SizeMode.Content));

            TextEntry searchField = new TextEntry(
                text: searchTextBox,
                widthMode: SizeMode.Fill,
                height: 30f
            );
            searchRow.AddChild(searchField);

            mainStack.AddChild(searchRow);

            int itemsPerRow = Mathf.FloorToInt(rect.width / (ItemSlotSize + 5f));
            itemsPerRow = Mathf.Max(1, itemsPerRow);

            ScrollContainer scrollCanvas = new ScrollContainer(
                widthMode: SizeMode.Fill,
                heightMode: SizeMode.Fill,
                scrollPosition: scrollPosition,
                padding: 5f
            );

            Grid itemGrid = new Grid(
                columns: itemsPerRow,
                cellWidth: ItemSlotSize,
                cellHeight: ItemSlotSize + ItemButtonHeight,
                columnSpacing: 10f,
                rowSpacing: 10f
            );

            foreach (var kvp in items)
            {
                var (stack, button) = CreateItemSlot(kvp.Key, kvp.Value);
                itemGrid.AddChild(stack);
                renderedButtons.Add(button);
            }

            scrollCanvas.AddChild(itemGrid);
            mainStack.AddChild(scrollCanvas);

            canvas.AddChild(mainStack);
            canvas.Render();

            if (searchField.Changed)
            {
                itemsCached = false;
            }

            for (int i=0;i<renderedButtons.Count;i++)
            {
                if (renderedButtons[i].Clicked)
                {
                    DropItem(items.ToList()[i].Key);
                }
            }

            renderedButtons.Clear();
        }

        private (UIElement, Button) CreateItemSlot(ThingDef thingDef, int count)
        {
            Stack itemStack = new Stack(
                widthMode: SizeMode.Fixed,
                heightMode: SizeMode.Fixed,
                width: ItemSlotSize,
                height: ItemSlotSize + ItemButtonHeight,
                verticalSpacing: 2f
            );

            Texture2D texture = GetThingTexture(thingDef);

            Icon itemIcon = new Icon(
                texture: texture,
                widthMode: SizeMode.Fixed,
                heightMode: SizeMode.Fixed,
                width: ItemSlotSize,
                height: ItemSlotSize,
                tooltip: thingDef.description
            );
            itemStack.AddChild(itemIcon);

            Label nameLabel = new Label(
                text: thingDef.LabelCap.Truncate(ItemSlotSize),
                widthMode: SizeMode.Fixed,
                width: ItemSlotSize
            );
            itemStack.AddChild(nameLabel);

            Label countLabel = new Label(
                text: $"x{count}",
                widthMode: SizeMode.Fixed,
                width: ItemSlotSize
            );
            itemStack.AddChild(countLabel);

            Button dropButton = new Button(
                text: "Drop",
                widthMode: SizeMode.Fill,
                height: 20f
            );

            itemStack.AddChild(dropButton);

            return (itemStack, dropButton);
        }

        private Texture2D GetThingTexture(ThingDef thingDef)
        {
            if (thingDef.DrawMatSingle != null && thingDef.DrawMatSingle.mainTexture != null)
            {
                Texture2D icon = Widgets.GetIconFor(thingDef);
                return thingDef.DrawMatSingle.mainTexture as Texture2D;
            }

            if (thingDef.uiIcon != null)
            {
                return thingDef.uiIcon;
            }

            return BaseContent.BadTex;
        }

        private void DropItem(ThingDef thingDef)
        {
            if (SelectedInterface == null || SelectedInterface.ParentNetwork == null)
            {
                return;
            }

            Thing itemToDrop = SelectedInterface.ParentNetwork.StoredItems
                .FirstOrDefault(t => t.def == thingDef);

            if (itemToDrop == null)
            {
                Log.Warning($"Could not find {thingDef.defName} in network storage");
                return;
            }

            int toSplit = Mathf.Min(itemToDrop.def.stackLimit, itemToDrop.stackCount);
            Thing newItem = itemToDrop.SplitOff(toSplit);

            SelectedInterface.ParentNetwork.RemoveItem(itemToDrop, toSplit);

            GenPlace.TryPlaceThing(newItem, SelectedInterface.Position, SelectedInterface.Map, ThingPlaceMode.Near);
            Log.Message($"Dropped {itemToDrop.LabelShort} near network interface at {SelectedInterface.Position}");
        }

        private List<KeyValuePair<ThingDef, int>> FilterItems(Dictionary<ThingDef, int> items)
        {
            if (itemsCached)
            {
                return cachedItems;
            }

            itemsCached = true;

            if (string.IsNullOrWhiteSpace(searchTextBox.Value))
            {
                cachedItems = items.OrderBy(kvp => kvp.Key.label).ToList();
                return cachedItems;
            }

            string searchLower = searchTextBox.Value.ToLower();
            cachedItems = items
                .Where(kvp => kvp.Key.label.ToLower().Contains(searchLower))
                .OrderBy(kvp => kvp.Key.label)
                .ToList();
            return cachedItems;
        }

        protected override void UpdateSize()
        {
            base.UpdateSize();
            size = new Vector2(WindowWidth, WindowHeight);
        }

        protected override void CloseTab()
        {
            SelectedInterface.ParentNetwork.CurrentTab = null;
            oldSelected = null;
            base.CloseTab();
        }

        public override void OnOpen()
        {
            itemsCached = false;
            SelectedInterface.ParentNetwork.CurrentTab = this;
            oldSelected = SelectedInterface;
        }

        public override void Notify_ClickOutsideWindow()
        {
            SelectedInterface.ParentNetwork.CurrentTab = null;
            oldSelected = null;
        }
    }
}
