using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public static class ModSettingsWindow
    {
        public static void Draw(Rect parent)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(parent);
            listing.CheckboxLabeled(
                "MN_SettingsEnableLoggingLabel".Translate(),
                ref ModSettings.EnableLogging,
                "MN_SettingsEnableLoggingDescription".Translate());
            listing.End();
        }
    }
}
