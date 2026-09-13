using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        #region mdk macros

        // This script was last deployed at $MDK_DATETIME$

        #endregion mdk macros

        #region mdk preserve

        //
        // CONFIGURATION
        // Defaults are overridden by the programmable block's [tagger] Custom Data section.
        //

        private const string ConfigSection = "tagger";
        private const string GeneralSection = "general";
        private const string FacingTagPrefix = "facing_";
        private const string PositionTagPrefix = "position_";

        private static readonly Dictionary<string, string[]> blockTypeMappings = new Dictionary<string, string[]>
        {
            {"AdvancedDoor", new [] {"door"}},
            {"AirtightHangarDoor", new [] {"door"}},
            {"AirtightSlideDoor", new [] {"door"}},
            {"AirVent", new [] {"vent"}},
            {"ArtificialMassBlock", new [] {"mass"}},
            {"Assembler", new [] {"assembler", "production"}},
            {"BasicMissionBlock", new [] {"mission"}},
            {"BatteryBlock", new [] {"battery", "power"}},
            {"Beacon", new [] {"beacon", "signal"}},
            {"BroadcastController", new [] {"broadcast_controller"}},
            {"ButtonPanel", new [] {"panel"}},
            {"CameraBlock", new [] {"camera"}},
            {"CargoContainer", new [] {"cargo"}},
            {"Collector", new [] {"collector", "conveyor"}},
            {"ControlPanel", new [] {"panel"}},
            {"ConveyorSorter", new [] {"sorter", "conveyor"}},
            {"CryoChamber", new [] {"cryo_chamber"}},
            {"Decoy", new [] {"decoy", "signal"}},
            {"DefensiveCombatBlock", new [] {"ai", "flight"}},
            {"EmotionControllerBlock", new [] {"ai", "emotion_controller"}},
            {"EventControllerBlock", new [] {"ai", "event_controller"}},
            {"ExtendedPistonBase", new [] {"piston"}},
            {"FlightMovementBlock", new [] {"ai", "flight"}},
            {"GasGenerator", new [] {"power"}},
            {"GasTank", new [] {"tank"}},
            {"GravityGenerator", new [] {"gravity"}},
            {"GravityGeneratorSphere", new [] {"gravity"}},
            {"Gyro", new [] {"gyro", "flight"}},
            {"HeatVent", new [] {"vent"}},
            {"InteriorLight", new [] {"light"}},
            {"JumpDrive", new [] {"jump_drive", "flight"}},
            {"LandingGear", new [] {"landing_gear"}},
            {"LargeGatlingTurret", new [] {"weapon", "turret", "gatling", "turret_gatling"}},
            {"LargeInteriorTurret", new [] {"weapon", "turret", "interior", "turret_interior"}},
            {"LargeMissileTurret", new [] {"weapon", "turret", "missile", "turret_missile"}},
            {"LaserAntenna", new [] {"antenna", "signal", "laser_antenna"}},
            {"MedicalRoom", new [] {"medical"}},
            {"MotorAdvancedStator", new [] {"rotor", "conveyor"}},
            {"MotorSuspension", new [] {"suspension"}},
            {"OffensiveCombatBlock", new [] {"ai", "flight"}},
            {"OreDetector", new [] {"ore_detector"}},
            {"OxygenFarm", new [] {"oxygen_farm"}},
            {"Parachute", new [] {"parachute"}},
            {"PathRecorderBlock", new [] {"ai", "flight"}},
            {"ProgrammableBlock", new [] {"programmable_block"}},
            {"Projector", new [] {"projector"}},
            {"RadioAntenna", new [] {"antenna", "signal"}},
            {"Reactor", new [] {"reactor", "power"}},
            {"Refinery", new [] {"refinery", "production"}},
            {"ReflectorLight", new [] {"light"}},
            {"RemoteControl", new [] {"remote_control", "ai", "flight"}},
            {"SafeZoneBlock", new [] {"safe_zone"}},
            {"Searchlight", new [] {"light"}},
            {"SensorBlock", new [] {"sensor"}},
            {"ShipConnector", new [] {"connector", "conveyor"}},
            {"ShipDrill", new [] {"drill", "tool"}},
            {"ShipGrinder", new [] {"grinder", "tool"}},
            {"ShipMergeBlock", new [] {"merge"}},
            {"ShipWelder", new [] {"welder", "tool"}},
            {"SmallGatlingGun", new [] {"weapon", "gatling", "small_gatling"}},
            {"SmallMissileLauncherReload", new [] {"weapon", "missile", "small_missile"}},
            {"SolarPanel", new [] {"solar_panel", "power"}},
            {"SoundBlock", new [] {"sound"}},
            {"SpaceBall", new [] {"space_ball"}},
            {"StoreBlock", new [] {"store"}},
            {"TargetDummyBlock", new [] {"target_dummy", "signal"}},
            {"TextPanel", new [] {"lcd"}},
            {"Thrust", new [] {"thrust", "flight"}},
            {"TimerBlock", new [] {"timer", "ai"}},
            {"Transponder", new [] {"transponder", "signal"}},
            {"TurretControlBlock", new [] {"turret_control"}},
            {"UpgradeModule", new [] {"upgrade_module"}},
            {"VirtualMass", new [] {"mass", "gravity"}},
            {"Warhead", new [] {"warhead", "explosive", "weapon"}},
            {"WindTurbine", new [] {"wind_turbine", "power"}},
        };

        //
        // SCRIPT
        // Don't change anything below this line unless you *really* know what you're doing
        //

        #endregion mdk preserve

        private class MechanicalLink
        {
            public IMyCubeGrid Parent;
            public IMyCubeGrid Child;
            public IMyTerminalBlock Joint;
        }

        private class MechanicalComponent
        {
            public List<IMyTerminalBlock> Blocks;
            public HashSet<IMyCubeGrid> Grids;
            public List<MechanicalLink> Links;
            public IMyCubeGrid Root;
        }

        private class SpatialContext
        {
            public IMyTerminalBlock Reference;
            public double MinimumForward;
            public double MaximumForward;
            public double MinimumRight;
            public double MaximumRight;
            public double MinimumUp;
            public double MaximumUp;
        }

        private CLI cli;
        private IniBool configIncludeConnectedGrids;
        private IniDouble configRunEveryMinutes;
        private bool includeConnectedGrids;
        private double runEveryMinutes;
        private double elapsedSeconds;

        public Program()
        {
            cli = new CLI(this, "Tag and Name", "2.0");
            cli.add("run", "Tag and rename blocks", DoRun);
            cli.add("tag", "Add INI tags to blocks", DoBlockTagging);
            cli.add("name", "Rename blocks from their grid and subgrid path", DoNaming);
            cli.add("clear", "Clear [general] tags from blocks", DoClearTags);
            cli.add("dump", "Write a block list to this block's Custom Data", DoDump);
            cli.add("config", "Show the active configuration", DoConfig);
            cli.set_default("run");

            configIncludeConnectedGrids = new IniBool(
                Me,
                ConfigSection,
                "include_connected_grids",
                false,
                "Also process grids joined through connectors.");
            configRunEveryMinutes = new IniDouble(
                Me,
                ConfigSection,
                "run_every_minutes",
                0,
                "Automatic interval in minutes. Zero disables automatic runs.");

            LoadConfiguration();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if ((updateSource & UpdateType.Update100) != 0)
            {
                elapsedSeconds += Runtime.TimeSinceLastRun.TotalSeconds;
                if (runEveryMinutes > 0 && elapsedSeconds >= runEveryMinutes * 60)
                {
                    elapsedSeconds = 0;
                    LoadConfiguration();
                    DoRun();
                }
            }

            if ((updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0)
            {
                LoadConfiguration();
                cli.run(argument);
            }
        }

        private void LoadConfiguration()
        {
            includeConnectedGrids = configIncludeConnectedGrids.Get();
            runEveryMinutes = Math.Max(0, configRunEveryMinutes.Get());
            Runtime.UpdateFrequency = runEveryMinutes > 0 ? UpdateFrequency.Update100 : UpdateFrequency.None;
        }

        public void DoConfig(string argument = null)
        {
            Echo("[tagger]");
            Echo("include_connected_grids=" + includeConnectedGrids.ToString().ToLower());
            Echo("run_every_minutes=" + runEveryMinutes);
        }

        public void DoRun(string argument = null)
        {
            var components = GetScopedComponents();
            var renamed = RenameBlocks(components);
            var tagged = TagBlocks(components);
            Echo("Complete: renamed " + renamed + ", tagged " + tagged);
        }

        public void DoNaming(string argument = null)
        {
            var components = GetScopedComponents();
            Echo("Renamed " + RenameBlocks(components) + " blocks");
        }

        public void DoBlockTagging(string argument = null)
        {
            var components = GetScopedComponents();
            Echo("Tagged " + TagBlocks(components) + " blocks");
        }

        public void DoDump(string argument = null)
        {
            var blocks = GetScopedComponents().SelectMany(component => component.Blocks).ToList();
            var ini = new MyIni();
            MyIniParseResult result;
            if (!string.IsNullOrWhiteSpace(Me.CustomData) && !ini.TryParse(Me.CustomData, out result))
            {
                Echo("Cannot write dump: " + result);
                return;
            }

            var blockNames = blocks
                .Select(b => b.CustomName + ", " + b.BlockDefinition.TypeIdString + ", " + b.BlockDefinition.SubtypeId)
                .ToList();
            ini.Set("tagger.debug", "blocks", string.Join("\n", blockNames));
            Me.CustomData = ini.ToString();
            Echo("Dumped " + blocks.Count + " blocks to Custom Data");
        }

        public void DoClearTags(string argument = null)
        {
            var blocks = GetScopedComponents().SelectMany(component => component.Blocks).ToList();
            var cleared = 0;

            foreach (var block in blocks)
            {
                var ini = new MyIni();
                MyIniParseResult result;
                if (!ini.TryParse(block.CustomData, out result))
                {
                    Echo("Skipped invalid Custom Data: " + block.CustomName);
                    continue;
                }

                if (ini.ContainsKey(GeneralSection, "tags"))
                {
                    ini.Delete(GeneralSection, "tags");
                    block.CustomData = ini.ToString();
                    cleared++;
                }
            }

            Echo("Cleared tags from " + cleared + " blocks");
        }

        private int TagBlocks(List<MechanicalComponent> components)
        {
            var tagged = 0;
            var skipped = 0;

            foreach (var component in components)
            {
                var reference = GetReferenceBlock(component.Blocks, component.Root);
                var spatialContext = reference == null
                    ? null
                    : CreateSpatialContext(component.Blocks, reference);

                foreach (var block in component.Blocks)
                {
                    var ini = new MyIni();
                    MyIniParseResult result;
                    if (!ini.TryParse(block.CustomData, out result))
                    {
                        Echo("Skipped invalid Custom Data: " + block.CustomName);
                        skipped++;
                        continue;
                    }

                    var tags = GetBlockTags(block, ini, spatialContext, component.Root.IsStatic);
                    ini.Set(GeneralSection, "tags", string.Join(", ", tags));
                    block.CustomData = ini.ToString();
                    tagged++;
                }
            }

            if (skipped > 0)
            {
                Echo("Skipped tagging " + skipped + " blocks with invalid Custom Data");
            }
            return tagged;
        }

        private List<string> GetBlockTags(
            IMyTerminalBlock block,
            MyIni ini,
            SpatialContext spatialContext,
            bool isStatic)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existing = ini.Get(GeneralSection, "tags").ToString();
            foreach (var tag in existing.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedTag = tag.Trim();
                if (!IsSpatialTag(trimmedTag))
                {
                    tags.Add(trimmedTag);
                }
            }

            tags.Add("all");
            var typeId = block.BlockDefinition.TypeIdString;
            var mapped = false;

            foreach (var mapping in blockTypeMappings)
            {
                if (!typeId.Contains(mapping.Key))
                {
                    continue;
                }

                mapped = true;
                foreach (var tag in mapping.Value)
                {
                    tags.Add(tag);
                }
            }

            if (!mapped)
            {
                tags.Add(typeId.Replace("MyObjectBuilder_", "").ToLower());
            }

            if (!isStatic && spatialContext != null)
            {
                AddPositionTags(tags, block, spatialContext);
                AddFacingTag(tags, block, spatialContext);
            }

            return tags.OrderBy(tag => tag).ToList();
        }

        private bool IsSpatialTag(string tag)
        {
            return tag.StartsWith(FacingTagPrefix, StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith(PositionTagPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private List<MechanicalComponent> GetScopedComponents()
        {
            var allBlocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(allBlocks);

            var links = GetMechanicalLinks(allBlocks);
            var components = GetMechanicalComponents(allBlocks, links);
            if (!includeConnectedGrids)
            {
                components = components.Where(component => component.Grids.Contains(Me.CubeGrid)).ToList();
            }

            var blockCount = components.Sum(component => component.Blocks.Count);
            Echo(includeConnectedGrids
                ? "Found " + blockCount + " blocks, including connected grids"
                : "Found " + blockCount + " blocks on this grid and its mechanical subgrids");
            return components;
        }

        private List<MechanicalComponent> GetMechanicalComponents(
            List<IMyTerminalBlock> blocks,
            List<MechanicalLink> links)
        {
            var components = new List<MechanicalComponent>();
            var remaining = new HashSet<IMyCubeGrid>(blocks.Select(block => block.CubeGrid));

            while (remaining.Count > 0)
            {
                var seed = remaining.First();
                var grids = new HashSet<IMyCubeGrid>();
                var pending = new Queue<IMyCubeGrid>();
                grids.Add(seed);
                pending.Enqueue(seed);

                while (pending.Count > 0)
                {
                    var grid = pending.Dequeue();
                    foreach (var link in links)
                    {
                        var neighbour = link.Parent == grid
                            ? link.Child
                            : link.Child == grid ? link.Parent : null;
                        if (neighbour != null && remaining.Contains(neighbour) && grids.Add(neighbour))
                        {
                            pending.Enqueue(neighbour);
                        }
                    }
                }

                foreach (var grid in grids)
                {
                    remaining.Remove(grid);
                }

                var componentLinks = links
                    .Where(link => grids.Contains(link.Parent) && grids.Contains(link.Child))
                    .ToList();
                var childGrids = new HashSet<IMyCubeGrid>(componentLinks.Select(link => link.Child));
                components.Add(new MechanicalComponent
                {
                    Blocks = blocks.Where(block => grids.Contains(block.CubeGrid)).ToList(),
                    Grids = grids,
                    Links = componentLinks,
                    Root = grids.FirstOrDefault(grid => !childGrids.Contains(grid)) ?? seed
                });
            }

            return components;
        }

        private IMyTerminalBlock GetReferenceBlock(List<IMyTerminalBlock> blocks, IMyCubeGrid rootGrid)
        {
            var rootBlocks = blocks.Where(block => block.CubeGrid == rootGrid).ToList();
            var mainCockpit = rootBlocks.OfType<IMyCockpit>().FirstOrDefault(cockpit => cockpit.IsMainCockpit)
                ?? blocks.OfType<IMyCockpit>().FirstOrDefault(cockpit => cockpit.IsMainCockpit);
            if (mainCockpit != null)
            {
                return mainCockpit;
            }

            var controller = rootBlocks.OfType<IMyShipController>().FirstOrDefault()
                ?? blocks.OfType<IMyShipController>().FirstOrDefault();
            if (controller != null)
            {
                return controller;
            }

            if (blocks.Any(block => block.CubeGrid == Me.CubeGrid))
            {
                return Me;
            }

            return rootBlocks.FirstOrDefault() ?? blocks.FirstOrDefault();
        }

        private SpatialContext CreateSpatialContext(
            List<IMyTerminalBlock> blocks,
            IMyTerminalBlock reference)
        {
            var context = new SpatialContext
            {
                Reference = reference,
                MinimumForward = double.PositiveInfinity,
                MaximumForward = double.NegativeInfinity,
                MinimumRight = double.PositiveInfinity,
                MaximumRight = double.NegativeInfinity,
                MinimumUp = double.PositiveInfinity,
                MaximumUp = double.NegativeInfinity
            };

            foreach (var block in blocks)
            {
                var offset = block.GetPosition() - reference.GetPosition();
                var forward = Vector3D.Dot(offset, reference.WorldMatrix.Forward);
                var right = Vector3D.Dot(offset, reference.WorldMatrix.Right);
                var up = Vector3D.Dot(offset, reference.WorldMatrix.Up);
                context.MinimumForward = Math.Min(context.MinimumForward, forward);
                context.MaximumForward = Math.Max(context.MaximumForward, forward);
                context.MinimumRight = Math.Min(context.MinimumRight, right);
                context.MaximumRight = Math.Max(context.MaximumRight, right);
                context.MinimumUp = Math.Min(context.MinimumUp, up);
                context.MaximumUp = Math.Max(context.MaximumUp, up);
            }

            return context;
        }

        private void AddPositionTags(HashSet<string> tags, IMyTerminalBlock block, SpatialContext context)
        {
            var offset = block.GetPosition() - context.Reference.GetPosition();
            var forward = Vector3D.Dot(offset, context.Reference.WorldMatrix.Forward);
            var right = Vector3D.Dot(offset, context.Reference.WorldMatrix.Right);
            var up = Vector3D.Dot(offset, context.Reference.WorldMatrix.Up);

            var positionTags = new[]
            {
                GetThird(forward, context.MinimumForward, context.MaximumForward, "position_stern", "position_bow"),
                GetThird(right, context.MinimumRight, context.MaximumRight, "position_port", "position_starboard"),
                GetThird(up, context.MinimumUp, context.MaximumUp, "position_lower", "position_upper")
            };

            foreach (var positionTag in positionTags.Where(tag => tag != null))
            {
                tags.Add(positionTag);
            }
            if (positionTags.All(tag => tag == null))
            {
                tags.Add("position_central");
            }
        }

        private string GetThird(
            double value,
            double minimum,
            double maximum,
            string lowTag,
            string highTag)
        {
            var span = maximum - minimum;
            if (span < 0.001)
            {
                return null;
            }

            var proportion = (value - minimum) / span;
            if (proportion < 1.0 / 3.0)
            {
                return lowTag;
            }
            if (proportion > 2.0 / 3.0)
            {
                return highTag;
            }
            return null;
        }

        private void AddFacingTag(HashSet<string> tags, IMyTerminalBlock block, SpatialContext context)
        {
            if (!(block is IMyThrust)
                && !(block is IMyLightingBlock)
                && !(block is IMyDoor)
                && !(block is IMyCameraBlock))
            {
                return;
            }

            var facing = block.WorldMatrix.Forward;
            var forward = Vector3D.Dot(facing, context.Reference.WorldMatrix.Forward);
            var right = Vector3D.Dot(facing, context.Reference.WorldMatrix.Right);
            var up = Vector3D.Dot(facing, context.Reference.WorldMatrix.Up);

            if (Math.Abs(forward) >= Math.Abs(right) && Math.Abs(forward) >= Math.Abs(up))
            {
                tags.Add(forward >= 0 ? "facing_forward" : "facing_backward");
            }
            else if (Math.Abs(right) >= Math.Abs(up))
            {
                tags.Add(right >= 0 ? "facing_right" : "facing_left");
            }
            else
            {
                tags.Add(up >= 0 ? "facing_up" : "facing_down");
            }
        }

        private int RenameBlocks(List<MechanicalComponent> components)
        {
            var renamed = 0;

            foreach (var component in components)
            {
                var gridId = GetGridName(component.Root);
                if (string.IsNullOrWhiteSpace(gridId))
                {
                    Echo("Skipped unnamed root grid: " + component.Root.CustomName);
                    continue;
                }

                var subgridNames = GetSubgridNames(component);
                foreach (var block in component.Blocks)
                {
                    string subgridName;
                    if (!subgridNames.TryGetValue(block.CubeGrid, out subgridName))
                    {
                        continue;
                    }

                    var descriptiveName = GetBlockName(block);
                    if (!string.IsNullOrWhiteSpace(subgridName))
                    {
                        descriptiveName = subgridName + " " + descriptiveName;
                    }

                    var newName = gridId + "." + descriptiveName;
                    if (block.CustomName != newName)
                    {
                        block.CustomName = newName;
                        renamed++;
                    }
                }
            }

            return renamed;
        }

        private List<MechanicalLink> GetMechanicalLinks(List<IMyTerminalBlock> blocks)
        {
            var links = new List<MechanicalLink>();
            foreach (var block in blocks)
            {
                var joint = block as IMyMechanicalConnectionBlock;
                if (joint == null || joint.TopGrid == null)
                {
                    continue;
                }

                links.Add(new MechanicalLink
                {
                    Parent = block.CubeGrid,
                    Child = joint.TopGrid,
                    Joint = block
                });
            }
            return links;
        }

        private Dictionary<IMyCubeGrid, string> GetSubgridNames(MechanicalComponent component)
        {
            var names = new Dictionary<IMyCubeGrid, string>();
            var pending = new Queue<IMyCubeGrid>();
            names[component.Root] = "";
            pending.Enqueue(component.Root);

            while (pending.Count > 0)
            {
                var parent = pending.Dequeue();
                foreach (var link in component.Links.Where(link => link.Parent == parent))
                {
                    if (names.ContainsKey(link.Child))
                    {
                        continue;
                    }

                    var parentName = names[parent];
                    var jointName = GetBlockName(link.Joint);
                    names[link.Child] = string.IsNullOrWhiteSpace(parentName)
                        ? jointName
                        : parentName + " " + jointName;
                    pending.Enqueue(link.Child);
                }
            }

            return names;
        }

        private string GetGridName(IMyCubeGrid grid)
        {
            var name = grid.CustomName == null ? "" : grid.CustomName.Trim();
            if (name.StartsWith("Small Grid ") || name.StartsWith("Large Grid ") || name.StartsWith("Static Grid "))
            {
                return "";
            }
            return name;
        }

        private string GetBlockName(IMyTerminalBlock block)
        {
            var standardName = GetStandardBlockName(block.DefinitionDisplayNameText);
            var overrideName = "";
            var ini = new MyIni();
            MyIniParseResult result;
            if (ini.TryParse(block.CustomData, out result))
            {
                overrideName = ini.Get(ConfigSection, "name_override").ToString().Trim();
            }

            var suffix = GetBlockNameSuffix(block, standardName, overrideName);
            var name = string.IsNullOrWhiteSpace(overrideName) ? standardName : overrideName;
            return string.IsNullOrWhiteSpace(suffix) ? name : name + " " + suffix;
        }

        private string GetStandardBlockName(string definitionName)
        {
            var name = RemoveTrailingNumber(definitionName.Trim());
            switch (name)
            {
                case "Programmable Block":
                case "Automaton Programmable Block":
                    return "PB";

                case "Timer Block":
                case "Automaton Timer Block":
                    return "Timer";

                case "Event Controller":
                    return "EC";

                default:
                    return name;
            }
        }

        private string GetBlockNameSuffix(IMyTerminalBlock block, string standardName, string overrideName)
        {
            var aliases = new[]
            {
                block.DefinitionDisplayNameText.Trim(),
                standardName,
                overrideName
            }
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var currentName = block.CustomName == null ? "" : block.CustomName.Trim();
            var bestIndex = -1;
            var bestLength = 0;

            foreach (var alias in aliases)
            {
                var index = currentName.LastIndexOf(alias, StringComparison.OrdinalIgnoreCase);
                if (index < 0 || !IsNameBoundary(currentName, index, alias.Length))
                {
                    continue;
                }

                if (index > bestIndex || (index == bestIndex && alias.Length > bestLength))
                {
                    bestIndex = index;
                    bestLength = alias.Length;
                }
            }

            if (bestIndex < 0)
            {
                return "";
            }

            var suffix = currentName.Substring(bestIndex + bestLength).TrimStart();
            var digitCount = 0;
            while (digitCount < suffix.Length && char.IsDigit(suffix[digitCount]))
            {
                digitCount++;
            }

            if (digitCount > 0 && (digitCount == suffix.Length || char.IsWhiteSpace(suffix[digitCount])))
            {
                suffix = suffix.Substring(digitCount).TrimStart();
            }

            return suffix.Trim();
        }

        private bool IsNameBoundary(string value, int index, int length)
        {
            var hasValidStart = index == 0 || value[index - 1] == '.' || char.IsWhiteSpace(value[index - 1]);
            var end = index + length;
            var hasValidEnd = end == value.Length || char.IsWhiteSpace(value[end]);
            return hasValidStart && hasValidEnd;
        }

        private string RemoveTrailingNumber(string value)
        {
            var end = value.Length - 1;
            while (end >= 0 && char.IsDigit(value[end]))
            {
                end--;
            }

            if (end == value.Length - 1 || end < 0 || !char.IsWhiteSpace(value[end]))
            {
                return value;
            }

            return value.Substring(0, end).TrimEnd();
        }
    }
}
