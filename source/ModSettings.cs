using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace SK_Matter_Network
{
    public class ModSettings : Verse.ModSettings
    {
        public static bool EnableLogging = false;
        public static bool DisableNetworkItemsForWealth = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref EnableLogging, "EnableLogging", false);
            Scribe_Values.Look(ref DisableNetworkItemsForWealth, "DisableNetworkItemsForWealth", false);
        }
    }
}
