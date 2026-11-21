using HarmonyLib;
using SK_Matter_Network.Patches;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public class Mod: Verse.Mod
    {
        public static Harmony instance;

        public Mod(ModContentPack content): base(content)
        {
            LongEventHandler.QueueLongEvent(Init, "MN_LoadingLabel", doAsynchronously: true, null);
        }

        private void Init()
        {
            GetSettings<ModSettings>();
            ModSettings.InitializeNetworkBuildingPowerDefaults();
            ModSettings.ApplyNetworkBuildingPowerUsageOverrides();
            Harmony harmony = new Harmony("rimworld.sk.matternetwork");
            harmony.PatchAll();
            RimMarketCompat.Install(harmony);
        }

        public override string SettingsCategory()
        {
            return "MN_SettingsTitle".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            ModSettingsWindow.Draw(inRect);
            base.DoSettingsWindowContents(inRect);
        }

        public override void WriteSettings()
        {
            ModSettings.NotifyStoredItemPowerDrawSettingsChanged();
            ModSettings.ApplyNetworkBuildingPowerUsageOverrides();
            base.WriteSettings();
        }
    }
}
