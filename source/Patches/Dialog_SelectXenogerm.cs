using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Dialog_SelectXenogerm
    {
        [HarmonyPatch(typeof(Dialog_SelectXenogerm), MethodType.Constructor, new[] { typeof(Pawn), typeof(Map), typeof(Xenogerm), typeof(Action<Xenogerm>) })]
        public static class Ctor
        {
            public static void Postfix(Dialog_SelectXenogerm __instance, Map map, Xenogerm initialSelected)
            {
                List<Xenogerm> xenogerms = __instance.xenogerms;
                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (item is Xenogerm xenogerm && !xenogerms.Contains(xenogerm))
                    {
                        xenogerms.Add(xenogerm);
                    }
                }

                if (initialSelected != null && xenogerms.Contains(initialSelected))
                {
                    __instance.selected = initialSelected;
                }
            }
        }
    }
}
