using Verse;
using HarmonyLib;

namespace SK_Matter_Network
{
    public class Mod: Verse.Mod
    {
        public static Harmony instance;

        public Mod(ModContentPack content): base(content)
        {
            instance = new Harmony("rimworld.sk.matternetwork");
            LongEventHandler.QueueLongEvent(Init, "MN.LoadingLabel", doAsynchronously: true, null);
        }

        public static void Init()
        {
            instance.PatchAll();
        }
    }
}
