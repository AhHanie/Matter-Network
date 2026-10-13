using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class TradeNetworkCompat
    {
        // ── Reflected TradeNetwork types ──────────────────────────────────────
        private static readonly Type _dialogCreateRequestType =
            AccessTools.TypeByName("TradeNetwork.Dialog_CreateRequest");
        private static readonly Type _silverHandlerType =
            AccessTools.TypeByName("TradeNetwork.SilverHandler");
        private static readonly Type _dialogFulfillMessageType =
            AccessTools.TypeByName("TradeNetwork.Dialog_FulfillMessage");
        private static readonly Type _mainTabWindowType =
            AccessTools.TypeByName("TradeNetwork.MainTabWindow_PawnMarket");
        private static readonly Type _thingClonerType =
            AccessTools.TypeByName("ThingCloner");

        // ── Dialog_FulfillMessage reflected members ───────────────────────────
        private static readonly FieldInfo _fulfillMapField =
            AccessTools.Field(_dialogFulfillMessageType, "map");
        private static readonly FieldInfo _despawnTargetsField =
            AccessTools.Field(_dialogFulfillMessageType, "_despawnTargets");

        // ── ThingCloner.SerializeToJsonString ─────────────────────────────────
        private static readonly MethodInfo _serializeMethod =
            AccessTools.Method(_thingClonerType, "SerializeToJsonString");

        // ── Newtonsoft helpers (resolved lazily via reflection) ───────────────
        private static bool _jsonReady;
        private static Type _jArrayType;
        private static Type _jObjectType;
        private static Type _jTokenType;
        private static Type _jValueType;
        private static MethodInfo _jTokenFromObject;
        private static MethodInfo _jTokenParse;
        private static MethodInfo _jValueCreateNull;
        private static MethodInfo _jArrayAdd;
        private static PropertyInfo _jObjectIndexer;

        private static bool EnsureJson()
        {
            if (_jsonReady) return _jArrayType != null;
            _jsonReady = true;
            _jArrayType = AccessTools.TypeByName("Newtonsoft.Json.Linq.JArray");
            _jObjectType = AccessTools.TypeByName("Newtonsoft.Json.Linq.JObject");
            _jTokenType = AccessTools.TypeByName("Newtonsoft.Json.Linq.JToken");
            _jValueType = AccessTools.TypeByName("Newtonsoft.Json.Linq.JValue");
            if (_jTokenType != null)
            {
                _jTokenFromObject = AccessTools.Method(_jTokenType, "FromObject", new[] { typeof(object) });
                _jTokenParse = AccessTools.Method(_jTokenType, "Parse", new[] { typeof(string) });
            }
            if (_jValueType != null)
                _jValueCreateNull = AccessTools.Method(_jValueType, "CreateNull");
            if (_jArrayType != null && _jTokenType != null)
                _jArrayAdd = AccessTools.Method(_jArrayType, "Add", new[] { _jTokenType });
            if (_jObjectType != null)
                _jObjectIndexer = _jObjectType.GetProperty("Item", new[] { typeof(string) });
            return _jArrayType != null;
        }

        private static object NewJArray() => Activator.CreateInstance(_jArrayType);
        private static object NewJObject() => Activator.CreateInstance(_jObjectType);
        private static object JTokenFrom(object val) => _jTokenFromObject?.Invoke(null, new[] { val });
        private static object JTokenParse(string json) => json != null ? _jTokenParse?.Invoke(null, new[] { (object)json }) : null;
        private static object JNull() => _jValueCreateNull?.Invoke(null, null);
        private static void JArrayAdd(object arr, object tok) => _jArrayAdd?.Invoke(arr, new[] { tok });
        private static void JObjSet(object obj, string key, object tok) =>
            _jObjectIndexer?.SetValue(obj, tok, new object[] { key });

        // ── Availability guards ───────────────────────────────────────────────
        private const string PackageId = "drilledhead.tradenetwork";

        private static bool IsAvailable() =>
            ModsConfig.IsActive(PackageId)
            && _dialogCreateRequestType != null
            && _silverHandlerType != null;

        private static bool IsFulfillAvailable() =>
            ModsConfig.IsActive(PackageId)
            && _dialogFulfillMessageType != null
            && _fulfillMapField != null
            && _despawnTargetsField != null
            && _serializeMethod != null;

        // ── Shared helpers ────────────────────────────────────────────────────

        private static IEnumerable<Thing> NetworkTradeItems(Map map)
        {
            foreach (Thing t in NetworkItemSearchUtility.AllNetworkItems(map))
            {
                if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                if (t.def.category != ThingCategory.Item) continue;
                if (!t.def.EverStorable(willMinifyIfPossible: false)) continue;
                if (t is Corpse) continue;
                yield return t;
            }
        }

        // Consume silver from powered orbital beacon cells.
        private static int ConsumeSpawnedBeaconSilver(Map map, int amount)
        {
            if (amount <= 0) return 0;
            int consumed = 0;
            HashSet<Thing> visited = new HashSet<Thing>();
            foreach (Building_OrbitalTradeBeacon beacon in Building_OrbitalTradeBeacon.AllPowered(map))
            {
                foreach (IntVec3 cell in beacon.TradeableCells)
                {
                    foreach (Thing thing in map.thingGrid.ThingsAt(cell))
                    {
                        if (thing.def != ThingDefOf.Silver || !visited.Add(thing)) continue;
                        int take = Math.Min(amount - consumed, thing.stackCount);
                        if (take <= 0) return consumed;
                        thing.SplitOff(take).Destroy();
                        consumed += take;
                        if (consumed >= amount) return consumed;
                    }
                }
            }
            return consumed;
        }

        // Consume silver from all networks on the map.
        private static int ConsumeNetworkSilver(Map map, int amount)
        {
            int consumed = 0;
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            foreach (DataNetwork network in mapComp.Networks)
            {
                List<Thing> items = new List<Thing>(network.StoredItems);
                foreach (Thing thing in items)
                {
                    if (thing.def != ThingDefOf.Silver) continue;
                    int take = Math.Min(amount - consumed, thing.stackCount);
                    if (take <= 0) return consumed;
                    thing.SplitOff(take).Destroy();
                    network.MarkBytesDirty();
                    consumed += take;
                    if (consumed >= amount) return consumed;
                }
            }
            return consumed;
        }

        // ── PATCH 1 ───────────────────────────────────────────────────────────
        // Make TradeNetwork item selectors see Matter Network items.
        // Covers: reward selector, gift selector, and local item listing.
        [HarmonyPatch]
        public static class Patch_AllLaunchableThingDefsForTrade
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_dialogCreateRequestType, "AllLaunchableThingDefsForTrade");

            public static IEnumerable<Thing> Postfix(IEnumerable<Thing> values, Map map)
            {
                HashSet<Thing> yielded = new HashSet<Thing>();
                if (values != null)
                {
                    foreach (Thing t in values)
                    {
                        if (t != null && yielded.Add(t))
                            yield return t;
                    }
                }
                if (map == null) yield break;
                foreach (Thing t in NetworkTradeItems(map))
                {
                    if (t.def != ThingDefOf.Silver && yielded.Add(t))
                        yield return t;
                }
            }
        }

        // ── PATCH 2 ───────────────────────────────────────────────────────────
        // Replace DeductSilver to consume spawned beacon silver first, then network silver.
        [HarmonyPatch]
        public static class Patch_DeductSilver
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_silverHandlerType, "DeductSilver");

            public static bool Prefix(Map map, int amount)
            {
                if (map == null || amount <= 0) return false;
                int remaining = amount;
                remaining -= ConsumeSpawnedBeaconSilver(map, remaining);
                if (remaining > 0)
                    ConsumeNetworkSilver(map, remaining);
                return false; // skip TradeUtility.LaunchSilver
            }
        }

        // ── PATCH 3 ───────────────────────────────────────────────────────────
        // When TryCollectAndSerialize cannot fill a request from beacon cells alone,
        // supplement from Matter Network. Re-collects from both beacons and network
        // since the original reserves nothing when it fails.
        [HarmonyPatch]
        public static class Patch_TryCollectAndSerialize
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsFulfillAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_dialogFulfillMessageType, "TryCollectAndSerialize");

            // __args layout: [0]=beacons, [1]=needed, [2]=out serializedItems, [3]=out failReason
            public static void Postfix(object __instance, ref bool __result, object[] __args)
            {
                if (__result) return;
                if (!EnsureJson()) return;

                Map map = _fulfillMapField.GetValue(__instance) as Map;
                if (map == null) return;

                Dictionary<string, int> needed = __args?[1] as Dictionary<string, int>;
                if (needed == null || needed.Count == 0) return;

                // Re-collect from beacon cells (original reserved nothing on failure).
                Dictionary<string, int> cover = new Dictionary<string, int>(needed);
                List<(Thing thing, int take, string defName)> picks =
                    new List<(Thing, int, string)>();

                List<Building_OrbitalTradeBeacon> beacons =
                    __args[0] as List<Building_OrbitalTradeBeacon>;
                if (beacons != null)
                {
                    HashSet<Thing> visited = new HashSet<Thing>();
                    foreach (Building_OrbitalTradeBeacon beacon in beacons)
                    {
                        foreach (IntVec3 cell in beacon.TradeableCells)
                        {
                            foreach (Thing t in cell.GetThingList(map))
                            {
                                if (t.def.category != ThingCategory.Item || !visited.Add(t)) continue;
                                Thing inner = (t is MinifiedThing mt) ? mt.InnerThing : t;
                                if (inner == null) continue;
                                string defName = inner.def.defName;
                                if (!cover.TryGetValue(defName, out int need) || need <= 0) continue;
                                int take = Math.Min(need, t.stackCount);
                                picks.Add((t, take, defName));
                                cover[defName] -= take;
                            }
                        }
                    }
                }

                // Fill remaining from network.
                foreach (Thing t in NetworkTradeItems(map))
                {
                    string defName = (t is MinifiedThing mt2 && mt2.InnerThing != null)
                        ? mt2.InnerThing.def.defName : t.def.defName;
                    if (!cover.TryGetValue(defName, out int need) || need <= 0) continue;
                    int take = Math.Min(need, t.stackCount);
                    picks.Add((t, take, defName));
                    cover[defName] -= take;
                }

                // Abort if any item is still unmet.
                foreach (KeyValuePair<string, int> kv in cover)
                    if (kv.Value > 0) return;

                // Build serialized JArray.
                object jArray = NewJArray();
                List<(Thing, int)> despawnList = new List<(Thing, int)>();
                foreach (var (thing, take, defName) in picks)
                {
                    Thing inner = (thing is MinifiedThing mt3 && mt3.InnerThing != null)
                        ? mt3.InnerThing : thing;
                    string jsonStr = null;
                    try { jsonStr = _serializeMethod.Invoke(null, new object[] { inner }) as string; }
                    catch { }

                    object jObj = NewJObject();
                    JObjSet(jObj, "defName", JTokenFrom(defName));
                    JObjSet(jObj, "stackCount", JTokenFrom(take));
                    JObjSet(jObj, "productData", jsonStr != null ? JTokenParse(jsonStr) : JNull());
                    JArrayAdd(jArray, jObj);
                    despawnList.Add((thing, take));
                }

                _despawnTargetsField.SetValue(__instance, despawnList);
                if (__args != null && __args.Length > 3)
                {
                    __args[2] = jArray;  // out serializedItems
                    __args[3] = null;    // out failReason
                }
                __result = true;
            }
        }

        // ── PATCH 4 ───────────────────────────────────────────────────────────
        // Mark all networks dirty after DespawnCollected removes items for
        // request fulfillment. DespawnCollected is synchronous, so this fires
        // immediately after network items are split/removed.
        [HarmonyPatch]
        public static class Patch_DespawnCollected
        {
            [HarmonyPrepare]
            public static bool Prepare() => IsFulfillAvailable();

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_dialogFulfillMessageType, "DespawnCollected");

            public static void Postfix(object __instance)
            {
                Map map = _fulfillMapField.GetValue(__instance) as Map;
                if (map == null) return;
                MarkAllNetworksDirty(map);
            }
        }

        // ── PATCH 5 ───────────────────────────────────────────────────────────
        // Mark all networks dirty after EnsureLocalItemsBuilt rebuilds the
        // local item list. This method is called after every successful listing
        // or silver deposit operation, making it a reliable post-trade hook.
        [HarmonyPatch]
        public static class Patch_EnsureLocalItemsBuilt
        {
            [HarmonyPrepare]
            public static bool Prepare() =>
                ModsConfig.IsActive(PackageId) && _mainTabWindowType != null;

            public static MethodBase TargetMethod() =>
                AccessTools.Method(_mainTabWindowType, "EnsureLocalItemsBuilt");

            public static void Postfix()
            {
                Map map = Find.CurrentMap;
                if (map == null) return;
                MarkAllNetworksDirty(map);
            }
        }

        private static void MarkAllNetworksDirty(Map map)
        {
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            if (mapComp == null) return;
            foreach (DataNetwork network in mapComp.Networks)
                network.MarkBytesDirty();
        }
    }
}
