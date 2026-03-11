using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Game_Patch
    {
        [HarmonyPatch(typeof(Game), "FinalizeInit")]
        public static class FinalizeInit
        {
            public static void Postfix()
            {
            }
        }
    }
}
