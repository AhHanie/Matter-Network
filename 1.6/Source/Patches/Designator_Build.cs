using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Designator_Build
    {
        private static readonly AccessTools.FieldRef<Designator_Build, bool> WriteStuffRef =
            AccessTools.FieldRefAccess<Designator_Build, bool>("writeStuff");

        private static readonly MethodInfo CheckCanInteractMethod =
            AccessTools.Method(typeof(Designator), "CheckCanInteract");

        [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.ProcessInput))]
        public static class ProcessInput
        {
            public static bool Prefix(Designator_Build __instance, Event ev)
            {
                ThingDef thingDef = __instance.PlacingDef as ThingDef;
                if (thingDef == null || !thingDef.MadeFromStuff)
                {
                    return true;
                }

                if (!CanInteract(__instance))
                {
                    return false;
                }

                Map map = __instance.Map;

                List<ThingDef> availableStuffDefs = GetAvailableStuffDefs(map, thingDef);
                if (availableStuffDefs.Count == 0)
                {
                    Messages.Message("NoStuffsToBuildWith".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                    return false;
                }

                List<FloatMenuOption> list = new List<FloatMenuOption>();
                foreach (ThingDef item in availableStuffDefs)
                {
                    ThingDef localStuffDef = item;
                    string label;
                    if (__instance.sourcePrecept == null)
                    {
                        label = GenLabel.ThingLabel(__instance.PlacingDef, localStuffDef);
                    }
                    else
                    {
                        label = "ThingMadeOfStuffLabel".Translate(localStuffDef.LabelAsStuff, __instance.sourcePrecept.Label).Resolve();
                    }

                    label = label.CapitalizeFirst();
                    FloatMenuOption floatMenuOption = new FloatMenuOption(label, delegate
                    {
                        __instance.CurActivateSound?.PlayOneShotOnCamera();
                        Find.DesignatorManager.Select(__instance);
                        __instance.SetStuffDef(localStuffDef);
                        WriteStuffRef(__instance) = true;
                    }, item);

                    floatMenuOption.tutorTag = "SelectStuff-" + thingDef.defName + "-" + localStuffDef.defName;
                    list.Add(floatMenuOption);
                }

                FloatMenu floatMenu = new FloatMenu(list);
                floatMenu.onCloseCallback = delegate
                {
                    WriteStuffRef(__instance) = true;
                };

                Find.WindowStack.Add(floatMenu);
                Find.DesignatorManager.Select(__instance);
                return false;
            }
        }

        private static bool CanInteract(Designator_Build designator)
        {
            return (bool)CheckCanInteractMethod.Invoke(designator, Array.Empty<object>());
        }

        private static List<ThingDef> GetAvailableStuffDefs(Map map, ThingDef buildingDef)
        {
            HashSet<ThingDef> candidateDefs = new HashSet<ThingDef>(map.resourceCounter.AllCountedAmounts.Keys);
            foreach (Thing item in map.listerThings.AllThings)
            {
                candidateDefs.Add(item.def);
            }

            foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                candidateDefs.Add(item.def);
            }

            return candidateDefs
                .Where(def => def.IsStuff && def.stuffProps.CanMake(buildingDef) && (DebugSettings.godMode || HasUsableStuff(map, def)))
                .OrderByDescending(def => def.stuffProps?.commonality ?? float.PositiveInfinity)
                .ThenBy(def => def.BaseMarketValue)
                .ToList();
        }

        private static bool HasUsableStuff(Map map, ThingDef stuffDef)
        {
            if (map.listerThings.ThingsOfDef(stuffDef).Count > 0)
            {
                return true;
            }

            return NetworkItemSearchUtility.AnyMatchingThing(map, item => item.def == stuffDef);
        }
    }
}
