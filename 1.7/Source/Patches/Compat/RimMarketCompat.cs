using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    internal static class RimMarketCompat
    {
        private const string PackageId = "forti.rimmarket";
        private const string StallTypeName = "RimMarket.Buildings.Building_MarketStall";
        private const BindingFlags DeclaredMembers = BindingFlags.DeclaredOnly | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static Type stallType;
        private static FieldInfo stallField;
        private static MethodInfo colonyStallGetter;
        private static MethodInfo rowFor;
        private static MethodInfo count;
        private static Func<Thing, ThingDef> stockDef;
        private static Func<ThingDef, bool> canSell;
        private static Func<Thing, bool> tradeable;

        internal static void Install(Harmony harmony)
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }

            try
            {
                Assembly assembly = LoadedModManager.RunningMods
                    .Where(mod => string.Equals(mod.PackageIdPlayerFacing, PackageId, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(mod => mod.assemblies.loadedAssemblies)
                    .FirstOrDefault(candidate => candidate.GetType(StallTypeName) != null)
                    ?? throw new TypeLoadException($"{StallTypeName} was not found in the active {PackageId} assemblies.");
                InstallHooks(harmony, assembly);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Matter Network] RimMarket compatibility could not be installed. {ex.Message}");
            }
        }

        private static void InstallHooks(Harmony harmony, Assembly assembly)
        {
            stallType = assembly.GetType(StallTypeName, throwOnError: true);
            if (!typeof(Building).IsAssignableFrom(stallType) || !typeof(IThingHolder).IsAssignableFrom(stallType))
            {
                throw new TypeLoadException($"Unexpected building or holder type for {StallTypeName}.");
            }

            Type dialogType = assembly.GetType("RimMarket.UI.Dialog_ColonyStall", throwOnError: true);
            Type colonyStallsType = assembly.GetType("RimMarket.Market.ColonyStalls", throwOnError: true);
            Type marketUtilityType = assembly.GetType("RimMarket.Market.MarketUtility", throwOnError: true);
            Type rowType = dialogType.GetNestedType("Row", BindingFlags.NonPublic)
                ?? throw new TypeLoadException($"{dialogType.FullName}.Row was not found.");

            stallField = dialogType.GetField("stall", DeclaredMembers);
            if (stallField == null || stallField.IsStatic || stallField.FieldType != stallType)
            {
                throw new MissingFieldException(dialogType.FullName, "stall");
            }

            rowFor = RequireMethod(dialogType, "RowFor", rowType, false, typeof(ThingDef));
            count = RequireMethod(dialogType, "Count", typeof(void), true, rowType, typeof(Thing), typeof(bool));
            colonyStallGetter = RequireMethod(stallType, "get_ColonyStall", typeof(bool), false);
            stockDef = RequireMethod(colonyStallsType, "StockDef", typeof(ThingDef), true, typeof(Thing))
                .CreateDelegate<Func<Thing, ThingDef>>();
            canSell = RequireMethod(colonyStallsType, "CanSell", typeof(bool), true, typeof(ThingDef))
                .CreateDelegate<Func<ThingDef, bool>>();
            tradeable = RequireMethod(marketUtilityType, "Tradeable", typeof(bool), true, typeof(Thing))
                .CreateDelegate<Func<Thing, bool>>();

            MethodInfo refresh = RequireMethod(dialogType, "Refresh", typeof(void), false);
            MethodInfo thingsInGroup = RequireMethod(typeof(ListerThings), nameof(ListerThings.ThingsInGroup), typeof(List<Thing>), false, typeof(ThingRequestGroup));
            MethodInfo spareFor = RequireMethod(stallType, "SpareFor", typeof(int), false, typeof(ThingDef), typeof(Thing));
            MethodInfo homeCount = RequireMethod(colonyStallsType, "HomeCount", typeof(int), true, typeof(Map), typeof(ThingDef));
            MethodInfo accepts = RequireMethod(stallType, "Accepts", typeof(bool), false, typeof(Thing));
            MethodInfo carryCheck = RequireMethod(typeof(Toils_Haul), nameof(Toils_Haul.ErrorCheckForCarry), typeof(bool), true, typeof(Pawn), typeof(Thing), typeof(bool));
            MethodInfo transfer = RequireMethod(typeof(ThingOwner), nameof(ThingOwner.TryTransferToContainer), typeof(int), false, typeof(Thing), typeof(ThingOwner), typeof(int), typeof(bool));

            RequireSingleCall(refresh, thingsInGroup);
            RequireSingleCall(spareFor, homeCount);

            var patches = new (MethodInfo Target, string Patch, MethodInfo InnerTarget, bool IsPrefix)[]
            {
                (refresh, nameof(CountNetworkStock), thingsInGroup, false),
                (spareFor, nameof(AddNetworkHomeCount), homeCount, false),
                (accepts, nameof(CheckNetworkStockEligibility), null, false),
                (carryCheck, nameof(CapPickupCount), null, false),
                (transfer, nameof(CapTransferCount), null, true)
            };

            for (int index = 0; index < patches.Length; index++)
            {
                var (target, patch, innerTarget, isPrefix) = patches[index];
                try
                {
                    HarmonyMethod method = new HarmonyMethod(GetPatchMethod(patch));
                    if (innerTarget != null)
                    {
                        method.innerTarget = new InnerTarget(innerTarget);
                        harmony.CreateProcessor(target).AddInnerPostfix(method).Patch();
                    }
                    else if (isPrefix)
                    {
                        harmony.Patch(target, prefix: method);
                    }
                    else
                    {
                        harmony.Patch(target, postfix: method);
                    }
                }
                catch (Exception ex)
                {
                    List<string> rollbackErrors = new List<string>();
                    for (int previous = index - 1; previous >= 0; previous--)
                    {
                        try
                        {
                            harmony.Unpatch(patches[previous].Target, GetPatchMethod(patches[previous].Patch));
                        }
                        catch (Exception rollbackError)
                        {
                            rollbackErrors.Add($"{patches[previous].Target.FullDescription()}: {rollbackError.Message}");
                        }
                    }

                    string details = rollbackErrors.Count == 0 ? string.Empty : $" Rollback failed for {string.Join(", ", rollbackErrors)}.";
                    throw new InvalidOperationException($"Patching {target.FullDescription()} failed. {ex.GetBaseException().Message}{details}", ex);
                }
            }
        }

        private static MethodInfo RequireMethod(Type type, string name, Type returnType, bool isStatic, params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, DeclaredMembers, null, parameters, null);
            if (method == null || method.ReturnType != returnType || method.IsStatic != isStatic || method.ContainsGenericParameters)
            {
                throw new MissingMethodException(type.FullName, name);
            }
            return method;
        }

        private static MethodInfo GetPatchMethod(string name) => AccessTools.DeclaredMethod(typeof(RimMarketCompat), name);

        private static void RequireSingleCall(MethodInfo outer, MethodInfo inner)
        {
            int calls = PatchProcessor.GetOriginalInstructions(outer).Count(instruction => instruction.Calls(inner));
            if (calls != 1)
            {
                throw new InvalidOperationException($"Expected one call to {inner.FullDescription()} in {outer.FullDescription()}, found {calls}.");
            }
        }

        private static void CountNetworkStock(ThingRequestGroup group, [HarmonyOuter] object __instance)
        {
            if (group != ThingRequestGroup.HaulableEver || stallField.GetValue(__instance) is not Building stall
                || !IsPlayerColonyStall(stall))
            {
                return;
            }

            object[] rowArgs = new object[1];
            object[] countArgs = { null, null, true };
            foreach (Thing item in AvailableNetworkStock(stall.Map))
            {
                ThingDef def = stockDef(item);
                if (!canSell(def) || !tradeable(item))
                {
                    continue;
                }

                rowArgs[0] = def;
                countArgs[0] = rowFor.Invoke(__instance, rowArgs);
                countArgs[1] = item;
                count.Invoke(null, countArgs);
            }
        }

        private static void AddNetworkHomeCount(Map map, ThingDef def, [HarmonyOuter] Thing coming,
            [HarmonyOuter] object __instance, ref int __result)
        {
            if (__instance is not Building stall || !IsPlayerColonyStall(stall))
            {
                return;
            }

            foreach (Thing item in AvailableNetworkStock(map))
            {
                if (stockDef(item) == def)
                {
                    __result += item.stackCount;
                }
            }

            // SpareFor adds any unspawned incoming stack after HomeCount. A controller stack
            // contributes through the scan above only while its network can supply the stall.
            if (GetHoldingController(coming) is NetworkBuildingController controller && controller.MapHeld == map)
            {
                __result -= coming.stackCount;
            }
        }

        private static void CheckNetworkStockEligibility(Building __instance, Thing t, ref bool __result)
        {
            if (__result && GetHoldingController(t) is NetworkBuildingController controller && IsPlayerColonyStall(__instance))
            {
                DataNetwork network = controller.ParentNetwork;
                __result = network?.CanExtractItems == true && network.ActiveController == controller
                    && IsAvailableController(controller, __instance.Map) && IsAvailableItem(t, controller)
                    && canSell(stockDef(t)) && tradeable(t);
            }
        }

        private static IEnumerable<Thing> AvailableNetworkStock(Map map)
        {
            NetworksMapComponent mapComponent = map?.GetComponent<NetworksMapComponent>();
            if (mapComponent == null)
            {
                yield break;
            }

            foreach (DataNetwork network in mapComponent.ExtractionEnabledNetworks)
            {
                NetworkBuildingController controller = network.ActiveController;
                if (!IsAvailableController(controller, map))
                {
                    continue;
                }

                foreach (Thing item in network.StoredItems)
                {
                    if (IsAvailableItem(item, controller))
                    {
                        yield return item;
                    }
                }
            }
        }

        private static bool IsAvailableController(NetworkBuildingController controller, Map map)
        {
            return controller != null && controller.Spawned && controller.Map == map && controller.Faction == Faction.OfPlayer
                && controller.Position.InBounds(map) && !controller.Position.Fogged(map);
        }

        private static bool IsAvailableItem(Thing item, NetworkBuildingController controller)
        {
            return item != null && item.stackCount > 0 && !item.Destroyed && !item.Spawned
                && ReferenceEquals(item.holdingOwner, controller.innerContainer) && !item.IsForbidden(Faction.OfPlayer);
        }

        private static NetworkBuildingController GetHoldingController(Thing item)
        {
            ThingOwner owner = item?.holdingOwner;
            return owner?.Owner is NetworkBuildingController controller && !item.Spawned
                && ReferenceEquals(owner, controller.innerContainer) ? controller : null;
        }

        private static bool IsPlayerColonyStall(Thing stall)
        {
            return stallType.IsInstanceOfType(stall) && stall.Spawned && stall.Faction == Faction.OfPlayer
                && (bool)colonyStallGetter.Invoke(stall, null);
        }

        private static ThingOwner GetStockingDestination(Pawn pawn, Job job)
        {
            if (job?.def != JobDefOf.HaulToContainer)
            {
                return null;
            }

            Thing stall = job.GetTarget(TargetIndex.B).Thing;
            return IsPlayerColonyStall(stall) && stall.Map == pawn.Map
                ? ((IThingHolder)stall).GetDirectlyHeldThings() : null;
        }

        private static void CapPickupCount(Pawn pawn, Thing haulThing, ref bool __result)
        {
            Job job = pawn.CurJob;
            if (__result || job == null || job.GetTarget(TargetIndex.A).Thing != haulThing)
            {
                return;
            }

            ThingOwner destination = GetStockingDestination(pawn, job);
            if (destination == null)
            {
                return;
            }

            job.count = Math.Min(job.count, destination.GetCountCanAccept(haulThing));
            if (job.count <= 0)
            {
                __result = true;
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            }
        }

        private static void CapTransferCount(ThingOwner __instance, Thing item, ThingOwner otherContainer, ref int count,
            bool canMergeWithExistingStacks)
        {
            if (__instance.Owner is Pawn_CarryTracker carryTracker && carryTracker.CarriedThing == item
                && otherContainer != null && ReferenceEquals(otherContainer, GetStockingDestination(carryTracker.pawn, carryTracker.pawn.CurJob)))
            {
                count = Math.Min(count, otherContainer.GetCountCanAccept(item, canMergeWithExistingStacks));
            }
        }
    }
}
