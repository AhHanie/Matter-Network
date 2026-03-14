using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    public static class ListerThingsExtensions
    {
        public static List<Thing> AllThingsInNetworks(this ListerThings listerThings)
        {
            List<Thing> thingsInNetworks = new List<Thing>();
            Map map = ResolveMap(listerThings);
            if (map == null)
            {
                return thingsInNetworks;
            }

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            foreach (DataNetwork network in mapComp.Networks)
            {
                foreach (Thing thing in network.StoredItems)
                {
                    thingsInNetworks.Add(thing);
                }
            }

            return thingsInNetworks;
        }

        public static List<Thing> AllThingsOutsideNetworks(this ListerThings listerThings)
        {
            List<Thing> thingsOutsideNetworks = new List<Thing>();
            Map map = ResolveMap(listerThings);
            if (map == null)
            {
                thingsOutsideNetworks.AddRange(listerThings.AllThings);
                return thingsOutsideNetworks;
            }

            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();

            foreach (Thing thing in listerThings.AllThings)
            {
                if (!mapComp.TryGetItemNetwork(thing, out _))
                {
                    thingsOutsideNetworks.Add(thing);
                }
            }

            return thingsOutsideNetworks;
        }

        private static Map ResolveMap(ListerThings listerThings)
        {
            if (Current.Game == null)
            {
                return null;
            }

            List<Map> maps = Current.Game.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map.listerThings == listerThings)
                {
                    return map;
                }
            }

            return null;
        }
    }
}
