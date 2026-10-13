using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public static class MatterIOPortSettingsClipboard
    {
        private static readonly StorageSettings storageSettings = new StorageSettings();
        private static MatterIOPortMode mode;
        private static Rot4 direction;
        private static int tickInterval;
        private static bool copied;

        public static bool HasCopiedSettings => copied;

        public static void CopyFrom(CompMatterIOPort comp)
        {
            if (comp == null)
            {
                return;
            }

            storageSettings.CopyFrom(comp.GetStoreSettings());
            mode = comp.Mode;
            direction = comp.Direction;
            tickInterval = comp.TickInterval;
            copied = true;

            Messages.Message("MN_MatterIOPortSettingsCopied".Translate(), null, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public static void PasteInto(CompMatterIOPort comp)
        {
            if (comp == null || !copied)
            {
                return;
            }

            comp.GetStoreSettings().CopyFrom(storageSettings);
            comp.SetMode(mode);
            comp.SetDirection(direction);
            comp.SetTickInterval(tickInterval);

            Messages.Message("MN_MatterIOPortSettingsPasted".Translate(), null, MessageTypeDefOf.NeutralEvent, historical: false);
        }
    }
}
