using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    // Lets Unique Weapons Unbound's customization dialog and haul planners
    // (Sequential/Sweep/Thorough) treat Matter Network stored items as
    // ordinary haulable ingredients: counted in the dialog, added to the
    // planner candidate pool, and extractable by the customize job even
    // though they are unspawned. UWU is resolved entirely through
    // AccessTools since Matter Network has no compile-time reference to it.
    public static class UniqueWeaponsUnboundCompat
    {
        private const string PackageId = "shunter.uniqueweaponsunbound";

        private static readonly Type ingredientReservationType =
            AccessTools.TypeByName("UniqueWeaponsUnbound.HaulPlanning.IngredientReservation");
        private static readonly Type haulCandidateType =
            AccessTools.TypeByName("UniqueWeaponsUnbound.HaulPlanning.HaulCandidate");
        private static readonly Type haulPlannerType =
            AccessTools.TypeByName("UniqueWeaponsUnbound.HaulPlanning.IHaulPlanner");
        private static readonly Type jobDriverType =
            AccessTools.TypeByName("UniqueWeaponsUnbound.JobDriver_CustomizeWeapon");

        private static readonly MethodInfo countAvailableMethod = ingredientReservationType == null ? null :
            AccessTools.Method(ingredientReservationType, "CountAvailable", new[] { typeof(Map), typeof(ThingDef), typeof(Pawn) });
        private static readonly MethodInfo buildHaulPoolMethod = ingredientReservationType == null || haulPlannerType == null ? null :
            AccessTools.Method(ingredientReservationType, "BuildHaulPool",
                new[] { typeof(Pawn), typeof(Dictionary<ThingDef, int>), haulPlannerType });

        private static readonly MethodInfo gotoIngredientFailConditionMethod = jobDriverType == null ? null :
            AccessTools.Method(jobDriverType, "GotoIngredientFailCondition");
        private static readonly MethodInfo doCarryTrackerPickupMethod = jobDriverType == null ? null :
            AccessTools.Method(jobDriverType, "DoCarryTrackerPickup");
        private static readonly MethodInfo doInventoryPickupMethod = jobDriverType == null ? null :
            AccessTools.Method(jobDriverType, "DoInventoryPickup");
        private static readonly MethodInfo setBailMessageMethod = jobDriverType == null ? null :
            AccessTools.Method(jobDriverType, "SetBailMessage");
        private static readonly PropertyInfo weaponLabelProperty = jobDriverType == null ? null :
            AccessTools.Property(jobDriverType, "WeaponLabel");
        private static readonly FieldInfo currentTripInvLoadField = jobDriverType == null ? null :
            AccessTools.Field(jobDriverType, "currentTripInvLoad");
        private static readonly FieldInfo pickupDestinationsField = jobDriverType == null ? null :
            AccessTools.Field(jobDriverType, "pickupDestinations");
        private static readonly FieldInfo pickupLastInTripField = jobDriverType == null ? null :
            AccessTools.Field(jobDriverType, "pickupLastInTrip");
        private static readonly FieldInfo currentPickupLastInTripField = jobDriverType == null ? null :
            AccessTools.Field(jobDriverType, "currentPickupLastInTrip");

        private static readonly FieldInfo candidateThingField = haulCandidateType == null ? null :
            AccessTools.Field(haulCandidateType, "Thing");
        private static readonly FieldInfo candidatePositionField = haulCandidateType == null ? null :
            AccessTools.Field(haulCandidateType, "Position");
        private static readonly FieldInfo candidateAvailableCountField = haulCandidateType == null ? null :
            AccessTools.Field(haulCandidateType, "AvailableCount");
        private static readonly FieldInfo candidateMassPerUnitField = haulCandidateType == null ? null :
            AccessTools.Field(haulCandidateType, "MassPerUnit");
        private static readonly FieldInfo candidateGroupIdField = haulCandidateType == null ? null :
            AccessTools.Field(haulCandidateType, "GroupId");

        private static readonly PropertyInfo plannerMultiplierProperty = haulPlannerType == null ? null :
            AccessTools.Property(haulPlannerType, "CandidatePoolMultiplier");
        private static readonly PropertyInfo plannerCapProperty = haulPlannerType == null ? null :
            AccessTools.Property(haulPlannerType, "CandidatePoolCap");
        private static readonly PropertyInfo plannerGroupPoolProperty = haulPlannerType == null ? null :
            AccessTools.Property(haulPlannerType, "GroupPoolBySlotGroup");

        private static readonly HashSet<string> warnedCapabilities = new HashSet<string>();

        private static bool CapCounting => countAvailableMethod != null;

        private static bool CapPlanner =>
            ingredientReservationType != null
            && haulCandidateType != null
            && haulPlannerType != null
            && buildHaulPoolMethod != null
            && candidateThingField != null
            && candidatePositionField != null
            && candidateAvailableCountField != null
            && candidateMassPerUnitField != null
            && candidateGroupIdField != null
            && plannerMultiplierProperty != null
            && plannerCapProperty != null
            && plannerGroupPoolProperty != null;

        private static bool CapFailCondition =>
            jobDriverType != null
            && gotoIngredientFailConditionMethod != null
            && setBailMessageMethod != null
            && weaponLabelProperty != null;

        private static bool CapCarryPickup =>
            jobDriverType != null
            && doCarryTrackerPickupMethod != null
            && pickupDestinationsField != null
            && pickupLastInTripField != null
            && currentPickupLastInTripField != null
            && setBailMessageMethod != null
            && weaponLabelProperty != null;

        private static bool CapInventoryPickup =>
            jobDriverType != null
            && doInventoryPickupMethod != null
            && currentTripInvLoadField != null
            && setBailMessageMethod != null
            && weaponLabelProperty != null;

        private static bool CapDialogAndPlan => CapCounting && CapPlanner && CapFailCondition;

        private static string DescribeMissing(params (string label, bool present)[] checks)
        {
            List<string> missing = new List<string>();
            foreach ((string label, bool present) in checks)
            {
                if (!present) missing.Add(label);
            }
            return missing.Count > 0 ? string.Join(", ", missing) : "(unknown)";
        }

        private static void WarnMissingCapability(string capability, string missingMembers)
        {
            if (!warnedCapabilities.Add(capability)) return;

            Assembly uwuAssembly = ingredientReservationType?.Assembly ?? jobDriverType?.Assembly;
            string version = uwuAssembly?.GetName().Version?.ToString() ?? "unknown";
            Logger.Warning("[UWU Compat] Unique Weapons Unbound (assembly v" + version
                + ") is loaded but is missing: " + missingMembers
                + ". Network-hauling compatibility for '" + capability + "' is disabled.");
        }

        private static bool Prepare_DialogAndPlan()
        {
            if (!ModsConfig.IsActive(PackageId)) return false;
            if (CapDialogAndPlan) return true;

            WarnMissingCapability("dialog count / haul planner", DescribeMissing(
                ("IngredientReservation type", ingredientReservationType != null),
                ("HaulCandidate type", haulCandidateType != null),
                ("IHaulPlanner type", haulPlannerType != null),
                ("IngredientReservation.CountAvailable(Map,ThingDef,Pawn)", countAvailableMethod != null),
                ("IngredientReservation.BuildHaulPool(Pawn,Dictionary<ThingDef,int>,IHaulPlanner)", buildHaulPoolMethod != null),
                ("HaulCandidate.Thing/Position/AvailableCount/MassPerUnit/GroupId fields",
                    candidateThingField != null && candidatePositionField != null
                        && candidateAvailableCountField != null && candidateMassPerUnitField != null
                        && candidateGroupIdField != null),
                ("IHaulPlanner.CandidatePoolMultiplier/CandidatePoolCap/GroupPoolBySlotGroup",
                    plannerMultiplierProperty != null && plannerCapProperty != null && plannerGroupPoolProperty != null),
                ("JobDriver_CustomizeWeapon.GotoIngredientFailCondition (required as the baseline extraction path)",
                    CapFailCondition)));
            return false;
        }

        private static bool Prepare_FailCondition()
        {
            if (!ModsConfig.IsActive(PackageId)) return false;
            if (CapFailCondition) return true;

            WarnMissingCapability("ingredient goto fail condition", DescribeMissing(
                ("JobDriver_CustomizeWeapon type", jobDriverType != null),
                ("JobDriver_CustomizeWeapon.GotoIngredientFailCondition", gotoIngredientFailConditionMethod != null),
                ("JobDriver_CustomizeWeapon.SetBailMessage", setBailMessageMethod != null),
                ("JobDriver_CustomizeWeapon.WeaponLabel", weaponLabelProperty != null)));
            return false;
        }

        private static bool Prepare_CarryPickup()
        {
            if (!ModsConfig.IsActive(PackageId)) return false;
            if (CapCarryPickup) return true;

            WarnMissingCapability("carry-tracker hybrid pickup", DescribeMissing(
                ("JobDriver_CustomizeWeapon type", jobDriverType != null),
                ("JobDriver_CustomizeWeapon.DoCarryTrackerPickup", doCarryTrackerPickupMethod != null),
                ("JobDriver_CustomizeWeapon.pickupDestinations", pickupDestinationsField != null),
                ("JobDriver_CustomizeWeapon.pickupLastInTrip", pickupLastInTripField != null),
                ("JobDriver_CustomizeWeapon.currentPickupLastInTrip", currentPickupLastInTripField != null),
                ("JobDriver_CustomizeWeapon.SetBailMessage", setBailMessageMethod != null),
                ("JobDriver_CustomizeWeapon.WeaponLabel", weaponLabelProperty != null)));
            return false;
        }

        private static bool Prepare_InventoryPickup()
        {
            if (!ModsConfig.IsActive(PackageId)) return false;
            if (CapInventoryPickup) return true;

            WarnMissingCapability("inventory hybrid pickup", DescribeMissing(
                ("JobDriver_CustomizeWeapon type", jobDriverType != null),
                ("JobDriver_CustomizeWeapon.DoInventoryPickup", doInventoryPickupMethod != null),
                ("JobDriver_CustomizeWeapon.currentTripInvLoad", currentTripInvLoadField != null),
                ("JobDriver_CustomizeWeapon.SetBailMessage", setBailMessageMethod != null),
                ("JobDriver_CustomizeWeapon.WeaponLabel", weaponLabelProperty != null)));
            return false;
        }

        // ---- shared helpers ----

        private static bool IsNetworkHeld(Thing thing)
        {
            Map map = thing?.MapHeld;
            if (map == null) return false;
            NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
            return mapComp != null && mapComp.TryGetItemNetwork(thing, out _);
        }

        // Usable + reservable check in one place: not destroyed/empty, not
        // spawned (defensive - network items should never be), not forbidden
        // to the player or the pawn, network extraction operational and
        // reachable, and still reservable for this pawn.
        private static bool TryGetNetworkIngredient(Pawn pawn, Thing item, out DataNetwork network, out int reservable)
        {
            network = null;
            reservable = 0;
            if (item == null || item.Destroyed || item.stackCount <= 0) return false;
            if (item.Spawned) return false;
            if (item.IsForbidden(Faction.OfPlayer) || item.IsForbidden(pawn)) return false;
            if (!NetworkItemSearchUtility.IsUsableNetworkItemForExtraction(pawn, item, out network)) return false;

            reservable = NetworkItemSearchUtility.GetReservableNetworkStackCount(pawn, item, 1);
            return reservable > 0;
        }

        private static string GetWeaponLabel(object driver)
        {
            return weaponLabelProperty.GetValue(driver) as string ?? "";
        }

        private static void SetBailMessage(object driver, string text)
        {
            setBailMessageMethod.Invoke(driver, new object[] { text });
        }

        private static void BailIngredientLost(JobDriver driver)
        {
            SetBailMessage(driver, "UWU_BailIngredientLost".Translate(GetWeaponLabel(driver)));
            driver.EndJobWith(JobCondition.Incompletable);
        }

        private static IList GetOrCreateList(object driver, FieldInfo field, Type elementType)
        {
            object value = field.GetValue(driver);
            if (value == null)
            {
                value = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                field.SetValue(driver, value);
            }

            return (IList)value;
        }

        // ---- 1. Dialog availability counts ----

        [HarmonyPatch]
        public static class Patch_CountAvailable
        {
            [HarmonyPrepare]
            public static bool Prepare() => Prepare_DialogAndPlan();

            public static MethodBase TargetMethod() => countAvailableMethod;

            public static void Postfix(Map map, ThingDef thingDef, Pawn pawn, ref int __result)
            {
                if (map == null || thingDef == null || pawn == null) return;

                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (item.def != thingDef) continue;
                    if (!TryGetNetworkIngredient(pawn, item, out _, out int reservable)) continue;
                    __result += reservable;
                }
            }
        }

        // ---- 2. Haul planner candidate pool ----

        [HarmonyPatch]
        public static class Patch_BuildHaulPool
        {
            [HarmonyPrepare]
            public static bool Prepare() => Prepare_DialogAndPlan();

            public static MethodBase TargetMethod() => buildHaulPoolMethod;

            public static void Postfix(Pawn pawn, Dictionary<ThingDef, int> demand, object planner, object __result)
            {
                if (pawn?.Map == null || demand == null || demand.Count == 0) return;
                if (planner == null) return;
                if (!(__result is IDictionary pool)) return;

                Map map = pawn.Map;
                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                if (mapComp == null || mapComp.Networks.Count == 0) return;

                float multiplier = (float)plannerMultiplierProperty.GetValue(planner);
                int cap = (int)plannerCapProperty.GetValue(planner);
                bool grouped = (bool)plannerGroupPoolProperty.GetValue(planner);

                foreach (KeyValuePair<ThingDef, int> entry in demand)
                {
                    ThingDef def = entry.Key;
                    int needed = entry.Value;
                    if (needed <= 0) continue;
                    if (!pool.Contains(def)) continue;
                    if (!(pool[def] is IList list)) continue;

                    int targetCount = Mathf.CeilToInt(needed * multiplier);

                    int cumulative = 0;
                    foreach (object existing in list)
                    {
                        cumulative += (int)candidateAvailableCountField.GetValue(existing);
                    }
                    if (cumulative >= targetCount) continue;

                    int representedGroups = 0;
                    HashSet<int> seenGroupIds = null;
                    if (grouped)
                    {
                        seenGroupIds = new HashSet<int>();
                        foreach (object existing in list)
                        {
                            int gid = (int)candidateGroupIdField.GetValue(existing);
                            if (gid < 0 || seenGroupIds.Add(gid)) representedGroups++;
                        }
                        if (representedGroups >= cap) continue;
                    }
                    else if (list.Count >= cap)
                    {
                        continue;
                    }

                    List<Thing> candidates = new List<Thing>();
                    foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                    {
                        if (item.def != def) continue;
                        if (!TryGetNetworkIngredient(pawn, item, out _, out _)) continue;
                        candidates.Add(item);
                    }
                    if (candidates.Count == 0) continue;

                    candidates.Sort((a, b) =>
                        NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn, a)
                            .CompareTo(NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn, b)));

                    foreach (Thing item in candidates)
                    {
                        if (cumulative >= targetCount) break;
                        if (grouped ? representedGroups >= cap : list.Count >= cap) break;

                        if (!mapComp.TryGetItemNetwork(item, out DataNetwork network)) continue;

                        NetworkBuildingNetworkInterface closestInterface =
                            Patch_Toils_Goto.GotoThing.FindClosestReachableInterface(pawn, network);
                        if (closestInterface == null) continue;

                        int reservable = NetworkItemSearchUtility.GetReservableNetworkStackCount(pawn, item, 1);
                        if (reservable <= 0) continue;

                        object candidate = Activator.CreateInstance(haulCandidateType);
                        candidateThingField.SetValue(candidate, item);
                        candidatePositionField.SetValue(candidate, closestInterface.InteractionCell);
                        candidateAvailableCountField.SetValue(candidate, reservable);
                        candidateMassPerUnitField.SetValue(candidate, item.GetStatValue(StatDefOf.Mass));
                        candidateGroupIdField.SetValue(candidate, -1);

                        list.Add(candidate);
                        cumulative += reservable;
                        if (grouped) representedGroups++;
                    }
                }
            }
        }

        // ---- 3. Accept network-held ingredients in the goto fail condition ----

        [HarmonyPatch]
        public static class Patch_GotoIngredientFailCondition
        {
            [HarmonyPrepare]
            public static bool Prepare() => Prepare_FailCondition();

            public static MethodBase TargetMethod() => gotoIngredientFailConditionMethod;

            public static bool Prefix(JobDriver __instance, ref bool __result)
            {
                Thing ing = __instance.job.GetTarget(TargetIndex.A).Thing;
                if (ing == null || !IsNetworkHeld(ing)) return true;

                if (TryGetNetworkIngredient(__instance.pawn, ing, out _, out _))
                {
                    __result = false;
                    return false;
                }

                SetBailMessage(__instance, "UWU_BailIngredientLost".Translate(GetWeaponLabel(__instance)));
                __result = true;
                return false;
            }
        }

        // ---- 4. Carry-tracker pickup from a network (Sweep/Thorough hybrid path) ----

        [HarmonyPatch]
        public static class Patch_DoCarryTrackerPickup
        {
            [HarmonyPrepare]
            public static bool Prepare() => Prepare_CarryPickup();

            public static MethodBase TargetMethod() => doCarryTrackerPickupMethod;

            public static bool Prefix(JobDriver __instance)
            {
                Thing thing = __instance.job.GetTarget(TargetIndex.A).Thing;
                if (thing == null || !IsNetworkHeld(thing)) return true;

                Pawn pawn = __instance.pawn;
                if (!TryGetNetworkIngredient(pawn, thing, out DataNetwork network, out _))
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                Job job = __instance.job;
                int requested = job.count;
                int volAvail = pawn.carryTracker.AvailableStackSpace(thing.def);
                if (volAvail <= 0)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                int take = Mathf.Min(Mathf.Min(requested, thing.stackCount), volAvail);
                if (take <= 0)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                int actualTaken = pawn.carryTracker.TryStartCarry(thing, take, reserve: true);
                network.MarkBytesDirty();
                if (actualTaken <= 0)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                int residual = requested - actualTaken;
                if (residual > 0)
                {
                    if (job.targetQueueA == null) job.targetQueueA = new List<LocalTargetInfo>();
                    if (job.countQueue == null) job.countQueue = new List<int>();
                    IList destinations = GetOrCreateList(__instance, pickupDestinationsField, typeof(int));
                    IList lastInTrip = GetOrCreateList(__instance, pickupLastInTripField, typeof(bool));

                    job.targetQueueA.Insert(0, thing);
                    job.countQueue.Insert(0, residual);
                    destinations.Insert(0, 0); // PickupDestination.CarryTracker == 0
                    lastInTrip.Insert(0, true);
                    currentPickupLastInTripField.SetValue(__instance, true);
                }

                return false;
            }
        }

        // ---- 5. Inventory pickup from a network (Sweep/Thorough hybrid path) ----

        [HarmonyPatch]
        public static class Patch_DoInventoryPickup
        {
            [HarmonyPrepare]
            public static bool Prepare() => Prepare_InventoryPickup();

            public static MethodBase TargetMethod() => doInventoryPickupMethod;

            public static bool Prefix(JobDriver __instance)
            {
                Thing thing = __instance.job.GetTarget(TargetIndex.A).Thing;
                if (thing == null || !IsNetworkHeld(thing)) return true;

                Pawn pawn = __instance.pawn;
                if (!TryGetNetworkIngredient(pawn, thing, out DataNetwork network, out _))
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                int requested = __instance.job.count;
                if (thing.stackCount < requested)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                Thing splitOff = thing.SplitOff(requested);
                if (splitOff == null)
                {
                    BailIngredientLost(__instance);
                    return false;
                }

                if (!pawn.inventory.innerContainer.TryAdd(splitOff, canMergeWithExistingStacks: true))
                {
                    if (splitOff != thing && !splitOff.Destroyed)
                    {
                        thing.TryAbsorbStack(splitOff, respectStackLimit: false);
                    }

                    network.MarkBytesDirty();
                    SetBailMessage(__instance, "UWU_BailIngredientPlacementFailed".Translate(GetWeaponLabel(__instance)));
                    __instance.EndJobWith(JobCondition.Incompletable);
                    return false;
                }

                network.MarkBytesDirty();

                IList tripLoad = GetOrCreateList(__instance, currentTripInvLoadField, typeof(ThingDefCountClass));
                tripLoad.Add(new ThingDefCountClass(thing.def, requested));

                return false;
            }
        }
    }
}
