using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class ModSettings : Verse.ModSettings
    {
        public const int MinNetworkBuildingPowerUsage = 0;
        public const int MaxNetworkBuildingPowerUsage = 5000;
        public const int NetworkBuildingPowerUsageStep = 50;
        public const int MinStoredItemPowerDrawPer100Bytes = 5;
        public const int MaxStoredItemPowerDrawPer100Bytes = 100;
        public const int StoredItemPowerDrawPer100BytesStep = 5;

        public static bool EnableLogging = false;
        public static bool DisableNetworkItemsForWealth = false;
        public static bool EnableStoredItemPowerDraw = true;
        public static int StoredItemPowerDrawPer100Bytes = 5;
        public static Dictionary<string, int> NetworkBuildingPowerUsageOverrides = new Dictionary<string, int>();

        private static readonly Dictionary<string, int> defaultNetworkBuildingPowerUsageByDefName = new Dictionary<string, int>();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref EnableLogging, "EnableLogging", false);
            Scribe_Values.Look(ref DisableNetworkItemsForWealth, "DisableNetworkItemsForWealth", false);
            Scribe_Values.Look(ref EnableStoredItemPowerDraw, "EnableStoredItemPowerDraw", true);
            Scribe_Values.Look(ref StoredItemPowerDrawPer100Bytes, "StoredItemPowerDrawPer100Bytes", 5);
            Scribe_Collections.Look(ref NetworkBuildingPowerUsageOverrides, "NetworkBuildingPowerUsageOverrides", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && NetworkBuildingPowerUsageOverrides == null)
            {
                NetworkBuildingPowerUsageOverrides = new Dictionary<string, int>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                StoredItemPowerDrawPer100Bytes = ClampAndRoundStoredItemPowerDraw(StoredItemPowerDrawPer100Bytes);
                RemoveMissingNetworkBuildingPowerUsageOverrides();
            }
        }

        public static IEnumerable<ThingDef> NetworkBuildingDefs
        {
            get
            {
                return DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(def => def.GetCompProperties<CompProperties_NetworkBuilding>() != null)
                    .OrderBy(def => def.label)
                    .ThenBy(def => def.defName);
            }
        }

        public static void InitializeNetworkBuildingPowerDefaults()
        {
            defaultNetworkBuildingPowerUsageByDefName.Clear();

            foreach (ThingDef def in NetworkBuildingDefs)
            {
                CompProperties_NetworkBuilding props = def.GetCompProperties<CompProperties_NetworkBuilding>();
                defaultNetworkBuildingPowerUsageByDefName[def.defName] = props.powerUsage;
            }
        }

        public static int GetDefaultNetworkBuildingPowerUsage(ThingDef def)
        {
            if (defaultNetworkBuildingPowerUsageByDefName.TryGetValue(def.defName, out int value))
            {
                return value;
            }

            CompProperties_NetworkBuilding props = def.GetCompProperties<CompProperties_NetworkBuilding>();
            defaultNetworkBuildingPowerUsageByDefName[def.defName] = props.powerUsage;
            return props.powerUsage;
        }

        public static int GetEffectiveNetworkBuildingPowerUsage(ThingDef def)
        {
            if (NetworkBuildingPowerUsageOverrides.TryGetValue(def.defName, out int value))
            {
                return value;
            }

            return GetDefaultNetworkBuildingPowerUsage(def);
        }

        public static void SetNetworkBuildingPowerUsageOverride(ThingDef def, int value)
        {
            NetworkBuildingPowerUsageOverrides[def.defName] = ClampAndRoundPowerUsage(value);
            ApplyNetworkBuildingPowerUsageOverrides();
        }

        public static void ClearNetworkBuildingPowerUsageOverride(ThingDef def)
        {
            NetworkBuildingPowerUsageOverrides.Remove(def.defName);
            ApplyNetworkBuildingPowerUsageOverrides();
        }

        public static void RemoveMissingNetworkBuildingPowerUsageOverride(string defName)
        {
            NetworkBuildingPowerUsageOverrides.Remove(defName);
        }

        public static void RemoveMissingNetworkBuildingPowerUsageOverrides()
        {
            List<string> missingDefNames = NetworkBuildingPowerUsageOverrides.Keys
                .Where(defName => !DefExists(defName))
                .ToList();

            for (int i = 0; i < missingDefNames.Count; i++)
            {
                NetworkBuildingPowerUsageOverrides.Remove(missingDefNames[i]);
            }
        }

        public static bool IsNetworkBuildingPowerUsageOverridden(ThingDef def)
        {
            return NetworkBuildingPowerUsageOverrides.ContainsKey(def.defName);
        }

        public static void ApplyNetworkBuildingPowerUsageOverrides()
        {
            foreach (ThingDef def in NetworkBuildingDefs)
            {
                CompProperties_NetworkBuilding props = def.GetCompProperties<CompProperties_NetworkBuilding>();
                props.powerUsage = GetEffectiveNetworkBuildingPowerUsage(def);
            }

            RefreshLoadedNetworkPowerCaches();
        }

        public static bool DefExists(string defName)
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(defName) != null;
        }

        public static int ClampAndRoundPowerUsage(int value)
        {
            int stepped = UnityEngine.Mathf.RoundToInt((float)value / NetworkBuildingPowerUsageStep) * NetworkBuildingPowerUsageStep;
            return UnityEngine.Mathf.Clamp(stepped, MinNetworkBuildingPowerUsage, MaxNetworkBuildingPowerUsage);
        }

        public static int ClampAndRoundStoredItemPowerDraw(int value)
        {
            int stepped = UnityEngine.Mathf.RoundToInt((float)value / StoredItemPowerDrawPer100BytesStep) * StoredItemPowerDrawPer100BytesStep;
            return UnityEngine.Mathf.Clamp(stepped, MinStoredItemPowerDrawPer100Bytes, MaxStoredItemPowerDrawPer100Bytes);
        }

        public static void SetStoredItemPowerDrawPer100Bytes(int value)
        {
            StoredItemPowerDrawPer100Bytes = ClampAndRoundStoredItemPowerDraw(value);
            RefreshLoadedNetworkPowerCaches();
        }

        public static void NotifyStoredItemPowerDrawSettingsChanged()
        {
            StoredItemPowerDrawPer100Bytes = ClampAndRoundStoredItemPowerDraw(StoredItemPowerDrawPer100Bytes);
            RefreshLoadedNetworkPowerCaches();
        }

        private static void RefreshLoadedNetworkPowerCaches()
        {
            if (Current.Game == null)
            {
                return;
            }

            for (int i = 0; i < Find.Maps.Count; i++)
            {
                NetworksMapComponent mapComp = Find.Maps[i].GetComponent<NetworksMapComponent>();
                for (int j = 0; j < mapComp.Networks.Count; j++)
                {
                    mapComp.Networks[j].RebuildPowerCaches();
                }
            }
        }
    }
}
