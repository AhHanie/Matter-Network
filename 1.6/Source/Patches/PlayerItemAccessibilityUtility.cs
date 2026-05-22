using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_PlayerItemAccessibilityUtility
    {
        [HarmonyPatch(typeof(PlayerItemAccessibilityUtility), nameof(PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas), new[] { typeof(ThingFilter) })]
        public static class PlayerOrQuestRewardHas_ThingFilter
        {
            public static void Postfix(ThingFilter thingFilter, ref bool __result)
            {
                if (__result)
                {
                    return;
                }

                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    if (NetworkItemSearchUtility.AnyMatchingThing(maps[i], item => thingFilter.Allows(item)))
                    {
                        __result = true;
                        return;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(PlayerItemAccessibilityUtility), nameof(PlayerItemAccessibilityUtility.PlayerOrQuestRewardHas), new[] { typeof(ThingDef), typeof(int) })]
        public static class PlayerOrQuestRewardHas_ThingDef
        {
            public static void Postfix(ThingDef thingDef, int count, ref bool __result)
            {
                if (__result || count <= 0)
                {
                    return;
                }

                int num = 0;
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    if (count == 1)
                    {
                        if (MapHasThing(maps[i], thingDef))
                        {
                            __result = true;
                            return;
                        }

                        continue;
                    }

                    num += GetMapThingCount(maps[i], thingDef);
                    if (num >= count)
                    {
                        __result = true;
                        return;
                    }
                }

                List<Caravan> caravans = Find.WorldObjects.Caravans;
                for (int j = 0; j < caravans.Count; j++)
                {
                    if (!caravans[j].IsPlayerControlled)
                    {
                        continue;
                    }

                    List<Thing> list = CaravanInventoryUtility.AllInventoryItems(caravans[j]);
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (list[k].def == thingDef)
                        {
                            num += list[k].stackCount;
                            if (num >= count)
                            {
                                __result = true;
                                return;
                            }
                        }
                    }
                }

                List<Site> sites = Find.WorldObjects.Sites;
                for (int l = 0; l < sites.Count; l++)
                {
                    for (int m = 0; m < sites[l].parts.Count; m++)
                    {
                        SitePart sitePart = sites[l].parts[m];
                        if (sitePart.things == null)
                        {
                            continue;
                        }

                        for (int n = 0; n < sitePart.things.Count; n++)
                        {
                            if (sitePart.things[n].def == thingDef)
                            {
                                num += sitePart.things[n].stackCount;
                                if (num >= count)
                                {
                                    __result = true;
                                    return;
                                }
                            }
                        }
                    }

                    DefeatAllEnemiesQuestComp component = sites[l].GetComponent<DefeatAllEnemiesQuestComp>();
                    if (component == null)
                    {
                        continue;
                    }

                    ThingOwner rewards = component.rewards;
                    for (int num2 = 0; num2 < rewards.Count; num2++)
                    {
                        if (rewards[num2].def == thingDef)
                        {
                            num += rewards[num2].stackCount;
                            if (num >= count)
                            {
                                __result = true;
                                return;
                            }
                        }
                    }
                }
            }

            private static bool MapHasThing(Map map, ThingDef thingDef)
            {
                if (map.listerThings.ThingsOfDef(thingDef).Count > 0)
                {
                    return true;
                }

                return NetworkItemSearchUtility.AnyMatchingThing(map, item => item.def == thingDef);
            }

            private static int GetMapThingCount(Map map, ThingDef thingDef)
            {
                int networkCount = NetworkItemSearchUtility.CountMatchingThingStacks(map, item => item.def == thingDef);

                return Mathf.Max(map.resourceCounter.GetCount(thingDef), map.listerThings.ThingsOfDef(thingDef).Count + networkCount);
            }
        }
    }
}
