using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    public enum MatterIOPortMode
    {
        Input,
        Output
    }

    public class CompProperties_MatterIOPort : CompProperties
    {
        public int defaultTickInterval = 600;
        public int minTickInterval = 60;
        public int maxTickInterval = 6000;
        public int maxInputStacksPerTick = 1;
        public MatterIOPortMode defaultMode = MatterIOPortMode.Input;

        public CompProperties_MatterIOPort()
        {
            compClass = typeof(CompMatterIOPort);
        }

        public override void DrawGhost(IntVec3 center, Rot4 rot, ThingDef thingDef, Color ghostCol, AltitudeLayer drawAltitude, Thing thing = null)
        {
            IntVec3 targetCell = center + rot.FacingCell;
            GenDraw.DrawFieldEdges(new List<IntVec3> { targetCell }, ghostCol);
            GenDraw.DrawArrowRotated(targetCell.ToVector3Shifted(), rot.AsAngle, ghost: true);
        }
    }

    public class CompMatterIOPort : ThingComp, INetworkTickable, IStoreSettingsParent
    {
        private MatterIOPortMode mode;
        private Rot4 direction = Rot4.North;
        private int tickInterval;
        private int nextTick;
        private int outputRoundRobinIndex;
        private StorageSettings settings;
        private string lastStatus;
        private int lastMovedCount;

        private readonly List<Thing> tmpThings = new List<Thing>();
        private readonly List<ThingDef> tmpCandidateDefs = new List<ThingDef>();
        private readonly List<Thing> tmpOutputStacks = new List<Thing>();

        public CompProperties_MatterIOPort Props => (CompProperties_MatterIOPort)props;
        public MatterIOPortMode Mode => mode;
        public Rot4 Direction => direction;
        public int TickInterval => tickInterval;
        public int LastMovedCount => lastMovedCount;
        public string LastStatus => lastStatus ?? "MN_MatterIOPortStatusIdle".Translate();
        public IntVec3 TargetCell => parent.Position + direction.FacingCell;
        public bool StorageTabVisible => true;

        private NetworkBuilding NetworkBuilding => parent as NetworkBuilding;
        private DataNetwork Network => NetworkBuilding?.ParentNetwork;

        public void SetMode(MatterIOPortMode newMode)
        {
            mode = newMode;
            lastStatus = "MN_MatterIOPortStatusModeChanged".Translate(ModeLabel);
        }

        public string ModeLabel
        {
            get
            {
                switch (mode)
                {
                    case MatterIOPortMode.Input:
                        return "MN_MatterIOPortModeInput".Translate();
                    case MatterIOPortMode.Output:
                        return "MN_MatterIOPortModeOutput".Translate();
                    default:
                        return mode.ToString();
                }
            }
        }

        public void SetDirection(Rot4 newDirection)
        {
            if (!newDirection.IsValid)
            {
                return;
            }

            direction = newDirection;
            lastStatus = "MN_MatterIOPortStatusDirectionChanged".Translate(DirectionLabel);
        }

        public string DirectionLabel
        {
            get
            {
                if (direction == Rot4.North) return "North".Translate();
                if (direction == Rot4.East) return "East".Translate();
                if (direction == Rot4.South) return "South".Translate();
                if (direction == Rot4.West) return "West".Translate();
                return direction.ToString();
            }
        }

        public void SetTickInterval(int value)
        {
            tickInterval = Mathf.Clamp(value, Props.minTickInterval, Props.maxTickInterval);
        }

        public StorageSettings GetStoreSettings()
        {
            return settings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            return parent.def.building.fixedStorageSettings ?? StorageSettings.EverStorableFixedSettings();
        }

        public void Notify_SettingsChanged()
        {
        }

        public void NetworkTick(int currentTick)
        {
            if (currentTick < nextTick)
            {
                return;
            }

            nextTick = currentTick + tickInterval;
            lastMovedCount = 0;
            TryRunTransfer();
        }

        private void TryRunTransfer()
        {
            DataNetwork network = Network;
            if (parent.IsForbidden(parent.Faction))
            {
                lastStatus = "MN_MatterIOPortStatusForbidden".Translate();
                return;
            }

            if (network == null)
            {
                lastStatus = "MN_MatterIOPortStatusNoNetwork".Translate();
                return;
            }

            if (!network.IsOperational || network.ActiveController?.innerContainer == null)
            {
                lastStatus = "MN_MatterIOPortStatusNetworkOffline".Translate(network.PowerModeLabel);
                return;
            }

            if (!TryResolveAdapter(out IMatterInventoryAdapter adapter))
            {
                lastStatus = "MN_MatterIOPortStatusNoTarget".Translate(TargetCell);
                return;
            }

            if (mode == MatterIOPortMode.Input)
            {
                RunInput(network, adapter);
            }
            else
            {
                RunOutput(network, adapter);
            }
        }

        public bool TryResolveAdapter(out IMatterInventoryAdapter adapter)
        {
            adapter = null;
            if (!TargetCell.InBounds(parent.Map))
            {
                return false;
            }

            return MatterInventoryAdapterResolver.TryResolve(parent.Map, TargetCell, parent, out adapter);
        }

        private void RunInput(DataNetwork network, IMatterInventoryAdapter adapter)
        {
            tmpThings.Clear();
            tmpThings.AddRange(adapter.GetStoredThings().Where(thing => CanMoveThing(thing, adapter)));
            tmpThings.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

            int stacksMoved = 0;
            for (int i = 0; i < tmpThings.Count && stacksMoved < Props.maxInputStacksPerTick; i++)
            {
                Thing item = tmpThings[i];
                if (item.Destroyed || !settings.AllowedToAccept(item))
                {
                    continue;
                }

                int count = System.Math.Min(item.stackCount, network.CanAcceptCount(item));
                if (count <= 0 || !adapter.CanPull(item, count))
                {
                    continue;
                }

                int moved = adapter.PullTo(item, count, network.ActiveController.innerContainer);
                if (moved <= 0)
                {
                    continue;
                }

                lastMovedCount += moved;
                stacksMoved++;
                network.MarkBytesDirty();
            }

            lastStatus = lastMovedCount > 0
                ? "MN_MatterIOPortStatusInputMoved".Translate(lastMovedCount, adapter.Label)
                : "MN_MatterIOPortStatusInputIdle".Translate(adapter.Label);
        }

        private void RunOutput(DataNetwork network, IMatterInventoryAdapter adapter)
        {
            BuildOutputCandidateDefs(network);
            if (tmpCandidateDefs.Count == 0)
            {
                outputRoundRobinIndex = 0;
                lastStatus = "MN_MatterIOPortStatusOutputNoItems".Translate();
                return;
            }

            int startIndex = PositiveModulo(outputRoundRobinIndex, tmpCandidateDefs.Count);
            for (int offset = 0; offset < tmpCandidateDefs.Count; offset++)
            {
                int index = (startIndex + offset) % tmpCandidateDefs.Count;
                ThingDef def = tmpCandidateDefs[index];
                if (TryOutputDef(network, adapter, def))
                {
                    outputRoundRobinIndex = (index + 1) % tmpCandidateDefs.Count;
                    network.MarkBytesDirty();
                    lastStatus = "MN_MatterIOPortStatusOutputMoved".Translate(lastMovedCount, def.label, adapter.Label);
                    return;
                }
            }

            outputRoundRobinIndex = (startIndex + 1) % tmpCandidateDefs.Count;
            lastStatus = "MN_MatterIOPortStatusOutputIdle".Translate(adapter.Label);
        }

        private void BuildOutputCandidateDefs(DataNetwork network)
        {
            tmpCandidateDefs.Clear();
            List<Thing> contents = network.ActiveController.innerContainer.InnerListForReading;
            for (int i = 0; i < contents.Count; i++)
            {
                Thing thing = contents[i];
                if (!CanMoveThing(thing) || !settings.AllowedToAccept(thing))
                {
                    continue;
                }

                if (!tmpCandidateDefs.Contains(thing.def))
                {
                    tmpCandidateDefs.Add(thing.def);
                }
            }

            tmpCandidateDefs.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
        }

        private bool TryOutputDef(DataNetwork network, IMatterInventoryAdapter adapter, ThingDef def)
        {
            tmpOutputStacks.Clear();
            int totalCount = 0;
            List<Thing> contents = network.ActiveController.innerContainer.InnerListForReading;
            for (int i = 0; i < contents.Count; i++)
            {
                Thing thing = contents[i];
                if (thing.def != def || !CanMoveThing(thing) || !settings.AllowedToAccept(thing) || !adapter.Accepts(thing))
                {
                    continue;
                }

                tmpOutputStacks.Add(thing);
                totalCount += thing.stackCount;
            }

            if (tmpOutputStacks.Count == 0 || totalCount <= 0)
            {
                return false;
            }

            tmpOutputStacks.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            int remaining = System.Math.Min(totalCount, def.stackLimit);
            for (int i = 0; i < tmpOutputStacks.Count && remaining > 0; i++)
            {
                Thing stack = tmpOutputStacks[i];
                if (stack.Destroyed)
                {
                    continue;
                }

                int countCanAccept = adapter.CountCanAccept(stack);
                int count = System.Math.Min(remaining, System.Math.Min(stack.stackCount, countCanAccept));
                if (count <= 0)
                {
                    continue;
                }

                int moved = adapter.PushFrom(stack, count);
                if (moved <= 0)
                {
                    continue;
                }

                lastMovedCount += moved;
                remaining -= moved;
            }

            return lastMovedCount > 0;
        }

        private bool CanMoveThing(Thing thing, IMatterInventoryAdapter adapter = null)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0 || thing.def.category != ThingCategory.Item)
            {
                return false;
            }

            if (!thing.def.EverStorable(willMinifyIfPossible: false))
            {
                return false;
            }

            if (thing.Spawned && IsForbiddenForInput(thing, adapter))
            {
                return false;
            }

            if (thing is MinifiedThing minifiedThing && minifiedThing.InnerThing is NetworkBuilding)
            {
                return false;
            }

            return true;
        }

        private bool IsForbiddenForInput(Thing thing, IMatterInventoryAdapter adapter)
        {
            IMatterInputForbiddenPolicy forbiddenPolicy = adapter as IMatterInputForbiddenPolicy;
            if (forbiddenPolicy != null && forbiddenPolicy.UseDirectForbiddenCheckForInput)
            {
                return thing is ThingWithComps thingWithComps && thingWithComps.TryGetComp<CompForbiddable>()?.Forbidden == true;
            }

            return thing.IsForbidden(parent.Faction);
        }

        public void DrawSelectionOverlay()
        {
            if (!parent.Spawned || !TargetCell.InBounds(parent.Map))
            {
                return;
            }

            Color color = Color.red;
            if (TryResolveAdapter(out IMatterInventoryAdapter _))
            {
                DataNetwork network = Network;
                color = network != null && network.IsOperational ? Color.white : Color.yellow;
            }

            GenDraw.DrawLineBetween(parent.TrueCenter(), TargetCell.ToVector3Shifted(), SimpleColor.White);
            GenDraw.DrawFieldEdges(new List<IntVec3> { TargetCell }, color);
            GenDraw.DrawArrowRotated(TargetCell.ToVector3Shifted(), direction.AsAngle, ghost: false);
        }

        public string StatusInspectString()
        {
            return "MN_MatterIOPortInspect".Translate(ModeLabel, DirectionLabel, tickInterval, LastStatus);
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureSettings();
            EnsureInitialized();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureSettings();
            EnsureInitialized();

            if (nextTick <= 0)
            {
                nextTick = Find.TickManager.TicksGame + parent.thingIDNumber % 120;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref mode, "mode", Props.defaultMode);
            Scribe_Values.Look(ref direction, "direction", Rot4.North);
            Scribe_Values.Look(ref tickInterval, "tickInterval", Props.defaultTickInterval);
            Scribe_Values.Look(ref nextTick, "nextTick", 0);
            Scribe_Values.Look(ref outputRoundRobinIndex, "outputRoundRobinIndex", 0);
            Scribe_Deep.Look(ref settings, "settings", this);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureSettings();
                EnsureInitialized();
            }
        }

        private void EnsureSettings()
        {
            if (settings == null)
            {
                settings = new StorageSettings(this);
                if (parent.def.building.defaultStorageSettings != null)
                {
                    settings.CopyFrom(parent.def.building.defaultStorageSettings);
                }
            }
            else
            {
                settings.owner = this;
            }
        }

        private void EnsureInitialized()
        {
            if (tickInterval <= 0)
            {
                tickInterval = Props.defaultTickInterval;
            }

            tickInterval = Mathf.Clamp(tickInterval, Props.minTickInterval, Props.maxTickInterval);
            if (!direction.IsValid)
            {
                direction = Rot4.North;
            }
        }

        private static int PositiveModulo(int value, int modulo)
        {
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }
    }
}
