using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public class ITab_NetworkStorage : ITab
    {
        private const float WindowWidth = 900f;
        private const float WindowHeight = 600f;
        private const float OuterPadding = 10f;
        private const float InnerSpacing = 8f;
        private const float LeftRailWidth = 100f;
        private const float HeaderHeight = 40f;
        private const float SearchHeight = 32f;
        private const float CardWidth = 92f;
        private const float CardHeight = 138f;
        private const float IconSize = 64f;
        private const float ButtonHeight = 24f;
        private const float GridSpacing = 10f;

        private static readonly Color RailColor = new Color(0.2f, 0.2f, 0.2f);
        private static readonly Color RailHighlightColor = new Color(0.28f, 0.31f, 0.35f);
        private static readonly Color CardColor = new Color(0.15f, 0.15f, 0.15f);
        private static readonly Color CardOutlineColor = new Color(0.32f, 0.32f, 0.32f);
        private static readonly Color AccentColor = new Color(0.42f, 0.75f, 0.82f);

        private string searchText = string.Empty;
        private Vector2 scrollPosition = Vector2.zero;
        private NetworkBuildingNetworkInterface oldSelected;
        private bool itemsCached;
        private List<KeyValuePair<ThingDef, int>> cachedItems;

        private NetworkBuildingNetworkInterface SelectedInterface => SelThing as NetworkBuildingNetworkInterface;

        public bool ItemsCached
        {
            get => itemsCached;
            set => itemsCached = value;
        }

        public ITab_NetworkStorage()
        {
            size = new Vector2(WindowWidth, WindowHeight);
            labelKey = "MN_NetworkStorageTab";
            oldSelected = null;
        }

        protected override void FillTab()
        {
            if (SelectedInterface == null || SelectedInterface.ParentNetwork == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0f, 0f, size.x, size.y), "No network connected");
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            if (oldSelected != SelectedInterface)
            {
                itemsCached = false;
                scrollPosition = Vector2.zero;
                oldSelected = SelectedInterface;
            }

            Rect mainRect = new Rect(0f, 0f, size.x, size.y).ContractedBy(OuterPadding);
            List<KeyValuePair<ThingDef, int>> filteredItems = FilterItems(SelectedInterface.ParentNetwork.ItemDefToStackCount);
            DrawUI(mainRect, filteredItems);
        }

        private void DrawUI(Rect rect, List<KeyValuePair<ThingDef, int>> items)
        {
            Widgets.DrawMenuSection(rect);

            Rect innerRect = rect.ContractedBy(InnerSpacing);
            Rect leftRailRect = new Rect(innerRect.x, innerRect.y, LeftRailWidth, innerRect.height);
            Rect contentRect = new Rect(
                leftRailRect.xMax + InnerSpacing,
                innerRect.y,
                innerRect.width - LeftRailWidth - InnerSpacing,
                innerRect.height);

            DrawLeftRail(leftRailRect);
            DrawContentArea(contentRect, items);
        }

        private void DrawLeftRail(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, RailColor);
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, CardOutlineColor);

            Rect controlsRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, 34f);
            Widgets.DrawBoxSolid(controlsRect, RailHighlightColor);
            Widgets.DrawBoxSolidWithOutline(controlsRect, Color.clear, AccentColor);

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            GUI.color = AccentColor;
            Widgets.Label(controlsRect, "Controls");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawContentArea(Rect rect, List<KeyValuePair<ThingDef, int>> items)
        {
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            Rect searchLabelRect = new Rect(rect.x, headerRect.yMax + InnerSpacing, 55f, SearchHeight);
            Rect searchFieldRect = new Rect(
                searchLabelRect.xMax + 6f,
                searchLabelRect.y,
                rect.width - searchLabelRect.width - 6f,
                SearchHeight);
            Rect gridRect = new Rect(
                rect.x,
                searchFieldRect.yMax + InnerSpacing,
                rect.width,
                rect.height - HeaderHeight - SearchHeight - (InnerSpacing * 2f));

            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            GUI.color = AccentColor;
            Widgets.Label(headerRect, "Matter Network");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Small;
            Widgets.Label(searchLabelRect, "Search:");
            string updatedSearch = Widgets.TextField(searchFieldRect, searchText);
            if (updatedSearch != searchText)
            {
                searchText = updatedSearch;
                itemsCached = false;
                scrollPosition = Vector2.zero;
                items = FilterItems(SelectedInterface.ParentNetwork.ItemDefToStackCount);
            }

            DrawItemsGrid(gridRect, items);
        }

        private void DrawItemsGrid(Rect rect, List<KeyValuePair<ThingDef, int>> items)
        {
            Widgets.DrawBoxSolid(rect, RailColor);
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, CardOutlineColor);

            Rect viewRect = rect.ContractedBy(6f);
            int columns = Mathf.Max(1, Mathf.FloorToInt((viewRect.width + GridSpacing) / (CardWidth + GridSpacing)));
            int rows = Mathf.CeilToInt(items.Count / (float)columns);
            float contentHeight = Mathf.Max(viewRect.height, rows * (CardHeight + GridSpacing) - GridSpacing);
            Rect scrollOutRect = viewRect;
            Rect scrollViewRect = new Rect(0f, 0f, viewRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(scrollOutRect, ref scrollPosition, scrollViewRect);

            if (items.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0f, 0f, scrollViewRect.width, 40f), "No items match the current filter.");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    int row = i / columns;
                    int column = i % columns;
                    Rect cardRect = new Rect(
                        column * (CardWidth + GridSpacing),
                        row * (CardHeight + GridSpacing),
                        CardWidth,
                        CardHeight);

                    DrawItemCard(cardRect, items[i].Key, items[i].Value);
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawItemCard(Rect rect, ThingDef thingDef, int count)
        {
            Widgets.DrawBoxSolid(rect, CardColor);
            Widgets.DrawBoxSolidWithOutline(rect, Color.clear, CardOutlineColor);

            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            Rect iconRect = new Rect(rect.x + 14f, rect.y + 8f, IconSize, IconSize);
            Rect nameRect = new Rect(rect.x + 6f, iconRect.yMax + 6f, rect.width - 12f, 34f);
            Rect countRect = new Rect(rect.x + 6f, nameRect.yMax + 2f, rect.width - 12f, 18f);
            Rect buttonRect = new Rect(rect.x + 6f, rect.yMax - ButtonHeight - 6f, rect.width - 12f, ButtonHeight);

            DrawThingTexture(iconRect, thingDef);
            TooltipHandler.TipRegion(iconRect, thingDef.description ?? thingDef.LabelCap);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(nameRect, thingDef.LabelCap.Truncate(nameRect.width));
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = AccentColor;
            Widgets.Label(countRect, $"x{count}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonText(buttonRect, "Drop"))
            {
                DropItem(thingDef);
            }
        }

        private void DrawThingTexture(Rect rect, ThingDef thingDef)
        {
            Texture2D texture = GetThingTexture(thingDef);
            GUI.color = Color.white;
            Widgets.DrawTextureFitted(rect, texture, 1f);
        }

        private Texture2D GetThingTexture(ThingDef thingDef)
        {
            if (thingDef.DrawMatSingle != null && thingDef.DrawMatSingle.mainTexture != null)
            {
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
            itemsCached = false;
        }

        private List<KeyValuePair<ThingDef, int>> FilterItems(Dictionary<ThingDef, int> items)
        {
            if (itemsCached)
            {
                return cachedItems;
            }

            itemsCached = true;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                cachedItems = items.OrderBy(kvp => kvp.Key.label).ToList();
                return cachedItems;
            }

            string searchLower = searchText.ToLower();
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
            if (SelectedInterface?.ParentNetwork != null)
            {
                SelectedInterface.ParentNetwork.CurrentTab = null;
            }

            oldSelected = null;
            base.CloseTab();
        }

        public override void OnOpen()
        {
            itemsCached = false;
            scrollPosition = Vector2.zero;

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
    }
}
