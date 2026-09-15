using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        #region mdk preserve
        #region mdk macros

// This script was last deployed at $MDK_DATETIME$

        #endregion mdk macros
//
// CONFIGURATION
// Defaults are overridden by the programmable block's [tagger] Custom Data section.
//

        private const string ConfigSection = "tagger";
        private const string GeneralSection = "general";

        // Every block is tagged with its type name in snake_case, with a leading "My" and a
        // trailing "_block" removed: BatteryBlock becomes battery, AirVent becomes air_vent,
        // LCDPanelsBlock becomes lcd_panels. Modded blocks follow the same rule.
        //
        // This table adds category tags on top of that. A type may appear in any number of
        // categories, and a category with one member is just an alias.
        private static readonly Dictionary<string, string> tagCategories = new Dictionary<string, string>
        {
            {"ai",         "BasicMissionBlock DefensiveCombatBlock EmotionControllerBlock EventControllerBlock FlightMovementBlock OffensiveCombatBlock PathRecorderBlock RemoteControl TimerBlock"},
            {"cargo",      "CargoContainer"},
            {"combat",     "DefensiveCombatBlock OffensiveCombatBlock"},
            {"connector",  "ShipConnector"},
            {"conveyor",   "Collector ConveyorSorter MotorAdvancedStator ShipConnector"},
            {"door",       "AirtightHangarDoor AirtightSlideDoor Door"},
            {"explosive",  "Warhead"},
            {"flight",     "Cockpit FlightMovementBlock Gyro JumpDrive PathRecorderBlock RemoteControl Thrust"},
            {"gatling",    "LargeGatlingTurret SmallGatlingGun"},
            {"gravity",    "GravityGenerator GravityGeneratorSphere VirtualMass"},
            {"missile",    "LargeMissileTurret SmallMissileLauncher SmallMissileLauncherReload"},
            {"lcd",        "LCDPanelsBlock TextPanel"},
            {"light",      "InteriorLight ReflectorLight Searchlight"},
            {"medical",    "CryoChamber MedicalRoom SurvivalKit"},
            {"panel",      "ButtonPanel"},
            {"piston",     "ExtendedPistonBase PistonBase"},
            {"power",      "BatteryBlock HydrogenEngine Reactor SolarPanel WindTurbine"},
            {"production", "Assembler OxygenGenerator Refinery SurvivalKit"},
            {"rotor",      "MotorAdvancedStator MotorStator"},
            {"signal",     "Beacon Decoy LaserAntenna RadioAntenna TargetDummyBlock TransponderBlock"},
            {"sound",      "Jukebox SoundBlock"},
            {"store",      "StoreBlock VendingMachine"},
            {"tank",       "OxygenTank"},
            {"tool",       "Drill ShipGrinder ShipWelder"},
            {"turret",     "InteriorTurret LargeGatlingTurret LargeMissileTurret TurretControlBlock"},
            {"vent",       "AirVent HeatVentBlock"},
            {"weapon",     "InteriorTurret LargeGatlingTurret LargeMissileTurret SmallGatlingGun SmallMissileLauncher SmallMissileLauncherReload TurretControlBlock Warhead"},
        };

        // Shorter names for block types whose full name is unwieldy in a terminal list.
        private static readonly Dictionary<string, string> nameShortcuts = new Dictionary<string, string>
        {
            {"Programmable Block", "PB"},
            {"Automaton Programmable Block", "PB"},
            {"Timer Block", "Timer"},
            {"Automaton Timer Block", "Timer"},
            {"Event Controller", "EC"},
        };


//
// SCRIPT
// Don't change anything below this line unless you *really* know what you're doing
//

        #endregion mdk preserve

        private const string TagsKey = "tags";
        private const string NameOverrideKey = "name_override";
        private const string FacingTagPrefix = "facing_";
        private const string PositionTagPrefix = "position_";

        // Axis order used by reference-space vectors: 0 right, 1 up, 2 forward.
        private static readonly string[] lowPositionTags = { "position_port", "position_lower", "position_stern" };
        private static readonly string[] highPositionTags = { "position_starboard", "position_upper", "position_bow" };
        private static readonly string[] lowFacingTags = { "facing_left", "facing_down", "facing_backward" };
        private static readonly string[] highFacingTags = { "facing_right", "facing_up", "facing_forward" };

        private static readonly char[] TagSeparators = { ',', ' ' };

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
            public MatrixD Reference;
            public Vector3D Minimum;
            public Vector3D Maximum;
        }

        private CLI cli;
        private IniDocument configuration;
        private readonly List<string> configWarnings = new List<string>();
        private bool includeConnectedGrids;
        private bool watchConfig;
        private string gridIdOverride = "";
        private double runEveryMinutes;
        private double elapsedSeconds;

        // Reused across every block so tagging and naming allocate one parser, not one per block.
        private readonly MyIni blockIni = new MyIni();
        private readonly HashSet<string> blockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> sortedTags = new List<string>();
        private readonly Dictionary<string, string[]> typeTagCache = new Dictionary<string, string[]>();
        private readonly Dictionary<string, List<string>> categoriesByType = new Dictionary<string, List<string>>();

        public Program()
        {
            cli = new CLI(this, "Tag and Name", "3.0");
            cli.Add("run", "Tag and rename blocks", DoRun);
            cli.Add("tag", "Add INI tags to blocks", DoTag);
            cli.Add("name", "Rename blocks from their grid and subgrid path", DoName);
            cli.Add("clear", "Clear [general] tags from blocks", DoClearTags);
            cli.Add("dump", "Write a block list to this block's Custom Data", DoDump);
            cli.Add("config", "Show the active configuration", DoConfig);
            cli.SetDefault("run");

            BuildCategoryLookup();
            LoadConfiguration();
        }

        private void BuildCategoryLookup()
        {
            foreach (var category in tagCategories)
            {
                foreach (var typeId in category.Value.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries))
                {
                    List<string> categories;
                    if (!categoriesByType.TryGetValue(typeId, out categories))
                        categoriesByType[typeId] = categories = new List<string>();
                    categories.Add(category.Key);
                }
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            bool command = (updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0;
            if (command || configuration.Changed) LoadConfiguration();

            if (runEveryMinutes > 0 && (updateSource & UpdateType.Update100) != 0)
            {
                elapsedSeconds += Runtime.TimeSinceLastRun.TotalSeconds;
                if (elapsedSeconds >= runEveryMinutes * 60)
                {
                    elapsedSeconds = 0;
                    DoRun();
                }
            }

            if (command) cli.Run(argument);
        }

        private void LoadConfiguration()
        {
            configWarnings.Clear();
            configuration = new IniDocument(Me, warning => configWarnings.Add(warning));
            includeConnectedGrids = configuration.Bool(ConfigSection, "include_connected_grids", false);
            watchConfig = configuration.Bool(ConfigSection, "watch_config", true);
            gridIdOverride = configuration.String(ConfigSection, "grid_id").Trim();

            double interval = configuration.Double(ConfigSection, "run_every_minutes", 0, 0);
            if (interval != runEveryMinutes) elapsedSeconds = 0;
            runEveryMinutes = interval;

            // Recognised so it never warns as unknown, but never written: overrides are opt-in.
            configuration.Has(ConfigSection, NameOverrideKey);
            configuration.CheckKeys(ConfigSection);
            configuration.Save();

            Runtime.UpdateFrequency = runEveryMinutes > 0 || watchConfig
                ? UpdateFrequency.Update100
                : UpdateFrequency.None;

            foreach (string warning in configWarnings) Echo(warning);
        }

        public void DoConfig(string argument = null)
        {
            Echo("[" + ConfigSection + "]");
            Echo("include_connected_grids=" + (includeConnectedGrids ? "true" : "false"));
            Echo("run_every_minutes=" + runEveryMinutes);
            Echo("watch_config=" + (watchConfig ? "true" : "false"));
            Echo("grid_id=" + gridIdOverride);
            foreach (string warning in configWarnings) Echo(warning);
        }

        public void DoRun(string argument = null) { Process(true, true); }
        public void DoName(string argument = null) { Process(true, false); }
        public void DoTag(string argument = null) { Process(false, true); }

        // One pass over the grid. Each block's Custom Data is parsed once and written back
        // only when the result differs from what is already there.
        private void Process(bool rename, bool tag)
        {
            var components = GetScopedComponents();
            var renamed = 0;
            var tagged = 0;
            var unparsable = 0;

            foreach (var component in components)
            {
                var gridId = GetComponentId(component);
                var renameThis = rename && gridId.Length > 0;
                if (rename && !renameThis)
                    Echo("Skipped unnamed root grid: " + component.Root.CustomName);

                var subgridPaths = renameThis ? GetSubgridPaths(component, gridId) : null;
                var spatial = tag && !component.Root.IsStatic
                    ? CreateSpatialContext(component)
                    : null;

                foreach (var block in component.Blocks)
                {
                    var valid = blockIni.TryParse(block.CustomData);
                    if (!valid)
                    {
                        unparsable++;
                        blockIni.Clear();
                    }

                    var overrideName = valid ? ReadOverride() : "";

                    if (renameThis)
                    {
                        string path;
                        var label = subgridPaths.TryGetValue(block.CubeGrid, out path)
                            ? GetBlockLabel(block, overrideName, gridId, path)
                            : "";
                        if (label.Length > 0)
                        {
                            var newName = gridId + "." + (path.Length == 0 ? label : path + " " + label);
                            if (block.CustomName != newName)
                            {
                                block.CustomName = newName;
                                renamed++;
                            }
                        }
                    }

                    // Tagging rewrites Custom Data, so it can only run on data it could parse.
                    if (tag && valid && WriteTags(block, spatial)) tagged++;
                }
            }

            if (unparsable > 0)
                Echo(unparsable + " blocks have invalid Custom Data; not tagged, and no override read");
            if (rename) Echo("Renamed " + renamed + " blocks");
            if (tag) Echo("Retagged " + tagged + " blocks");
        }

        public void DoClearTags(string argument = null)
        {
            var cleared = 0;
            var unparsable = 0;

            foreach (var component in GetScopedComponents())
            {
                foreach (var block in component.Blocks)
                {
                    if (!blockIni.TryParse(block.CustomData))
                    {
                        unparsable++;
                        continue;
                    }

                    if (!blockIni.ContainsKey(GeneralSection, TagsKey)) continue;
                    blockIni.Delete(GeneralSection, TagsKey);
                    block.CustomData = blockIni.ToString();
                    cleared++;
                }
            }

            if (unparsable > 0) Echo("Skipped " + unparsable + " blocks with invalid Custom Data");
            Echo("Cleared tags from " + cleared + " blocks");
        }

        public void DoDump(string argument = null)
        {
            if (!blockIni.TryParse(Me.CustomData))
            {
                Echo("Cannot write dump: this block's Custom Data is not valid INI");
                return;
            }

            var listing = new StringBuilder();
            var count = 0;
            foreach (var component in GetScopedComponents())
            {
                foreach (var block in component.Blocks)
                {
                    if (count > 0) listing.Append('\n');
                    listing.Append(block.CustomName)
                        .Append(", ").Append(block.BlockDefinition.TypeIdString)
                        .Append(", ").Append(block.BlockDefinition.SubtypeId);
                    count++;
                }
            }

            blockIni.Set(ConfigSection + "_debug", "blocks", listing.ToString());
            Me.CustomData = blockIni.ToString();
            Echo("Dumped " + count + " blocks to Custom Data");
        }

//
// TAGGING
//

        // Returns true when the block's Custom Data actually changed.
        private bool WriteTags(IMyTerminalBlock block, SpatialContext spatial)
        {
            blockTags.Clear();

            // Hand-added tags survive; spatial tags are recomputed every run.
            var existing = blockIni.Get(GeneralSection, TagsKey).ToString();
            foreach (var tag in existing.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IsSpatialTag(tag)) blockTags.Add(tag);
            }

            blockTags.Add("all");
            foreach (var tag in GetTypeTags(block)) blockTags.Add(tag);

            if (spatial != null)
            {
                AddPositionTags(block, spatial);
                AddFacingTag(block, spatial);
            }

            sortedTags.Clear();
            sortedTags.AddRange(blockTags);
            sortedTags.Sort(StringComparer.OrdinalIgnoreCase);

            blockIni.Set(GeneralSection, TagsKey, string.Join(", ", sortedTags));
            var updated = blockIni.ToString();
            if (block.CustomData == updated) return false;

            block.CustomData = updated;
            return true;
        }

        private bool IsSpatialTag(string tag)
        {
            return tag.StartsWith(FacingTagPrefix, StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith(PositionTagPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private string[] GetTypeTags(IMyTerminalBlock block)
        {
            var typeId = block.BlockDefinition.TypeIdString.Replace("MyObjectBuilder_", "");

            string[] cached;
            if (typeTagCache.TryGetValue(typeId, out cached)) return cached;

            var tags = new List<string> { ToTag(typeId) };
            List<string> categories;
            if (categoriesByType.TryGetValue(typeId, out categories)) tags.AddRange(categories);

            cached = tags.ToArray();
            typeTagCache[typeId] = cached;
            return cached;
        }

        // BatteryBlock -> battery, AirVent -> air_vent, LCDPanelsBlock -> lcd_panels,
        // MyProgrammableBlock -> programmable.
        private static string ToTag(string typeId)
        {
            if (typeId.Length > 2 && typeId[0] == 'M' && typeId[1] == 'y' && char.IsUpper(typeId[2]))
                typeId = typeId.Substring(2);

            var builder = new StringBuilder(typeId.Length + 8);
            for (var i = 0; i < typeId.Length; i++)
            {
                var current = typeId[i];
                if (char.IsUpper(current) && i > 0)
                {
                    var startsWord = !char.IsUpper(typeId[i - 1])
                        || (i + 1 < typeId.Length && char.IsLower(typeId[i + 1]));
                    if (startsWord) builder.Append('_');
                }
                builder.Append(char.ToLowerInvariant(current));
            }

            var tag = builder.ToString();
            return tag.EndsWith("_block", StringComparison.Ordinal)
                ? tag.Substring(0, tag.Length - "_block".Length)
                : tag;
        }

        private void AddPositionTags(IMyTerminalBlock block, SpatialContext spatial)
        {
            var local = ToReferenceSpace(block.GetPosition() - spatial.Reference.Translation, spatial.Reference);
            var placed = false;

            for (var axis = 0; axis < 3; axis++)
            {
                var tag = GetThird(
                    Axis(local, axis),
                    Axis(spatial.Minimum, axis),
                    Axis(spatial.Maximum, axis),
                    lowPositionTags[axis],
                    highPositionTags[axis]);
                if (tag == null) continue;
                blockTags.Add(tag);
                placed = true;
            }

            if (!placed) blockTags.Add("position_central");
        }

        private string GetThird(double value, double minimum, double maximum, string lowTag, string highTag)
        {
            var span = maximum - minimum;
            if (span < 0.001) return null;

            var proportion = (value - minimum) / span;
            if (proportion < 1.0 / 3.0) return lowTag;
            if (proportion > 2.0 / 3.0) return highTag;
            return null;
        }

        private void AddFacingTag(IMyTerminalBlock block, SpatialContext spatial)
        {
            if (!(block is IMyThrust) && !(block is IMyLightingBlock)
                && !(block is IMyDoor) && !(block is IMyCameraBlock)) return;

            var facing = ToReferenceSpace(block.WorldMatrix.Forward, spatial.Reference);

            // Forward wins ties with both other axes, right wins ties with up.
            var dominant = 2;
            if (Math.Abs(Axis(facing, 0)) > Math.Abs(Axis(facing, dominant))) dominant = 0;
            if (Math.Abs(Axis(facing, 1)) > Math.Abs(Axis(facing, dominant))) dominant = 1;

            blockTags.Add(Axis(facing, dominant) >= 0
                ? highFacingTags[dominant]
                : lowFacingTags[dominant]);
        }

        private SpatialContext CreateSpatialContext(MechanicalComponent component)
        {
            var reference = GetReferenceBlock(component);
            if (reference == null) return null;

            var matrix = reference.WorldMatrix;
            var minimum = new Vector3D(double.PositiveInfinity);
            var maximum = new Vector3D(double.NegativeInfinity);

            foreach (var block in component.Blocks)
            {
                var local = ToReferenceSpace(block.GetPosition() - matrix.Translation, matrix);
                minimum = Vector3D.Min(minimum, local);
                maximum = Vector3D.Max(maximum, local);
            }

            return new SpatialContext { Reference = matrix, Minimum = minimum, Maximum = maximum };
        }

        // Bow/stern only mean something relative to a controller. Without one there is no
        // orientation worth guessing at, so the component gets no spatial tags.
        private IMyTerminalBlock GetReferenceBlock(MechanicalComponent component)
        {
            IMyShipController rootMainCockpit = null;
            IMyShipController anyMainCockpit = null;
            IMyShipController rootController = null;
            IMyShipController anyController = null;
            var hasLocalBlocks = false;

            foreach (var block in component.Blocks)
            {
                if (block.CubeGrid == Me.CubeGrid) hasLocalBlocks = true;

                var controller = block as IMyShipController;
                if (controller == null) continue;
                var onRoot = controller.CubeGrid == component.Root;

                var cockpit = controller as IMyCockpit;
                if (cockpit != null && cockpit.IsMainCockpit)
                {
                    if (onRoot && rootMainCockpit == null) rootMainCockpit = controller;
                    if (anyMainCockpit == null) anyMainCockpit = controller;
                }

                if (onRoot && rootController == null) rootController = controller;
                if (anyController == null) anyController = controller;
            }

            if (rootMainCockpit != null) return rootMainCockpit;
            if (anyMainCockpit != null) return anyMainCockpit;
            if (rootController != null) return rootController;
            if (anyController != null) return anyController;
            return hasLocalBlocks ? Me : null;
        }

        private static Vector3D ToReferenceSpace(Vector3D offset, MatrixD reference)
        {
            return new Vector3D(
                Vector3D.Dot(offset, reference.Right),
                Vector3D.Dot(offset, reference.Up),
                Vector3D.Dot(offset, reference.Forward));
        }

        private static double Axis(Vector3D value, int index)
        {
            return index == 0 ? value.X : index == 1 ? value.Y : value.Z;
        }

//
// NAMING
//

        private string GetComponentId(MechanicalComponent component)
        {
            if (gridIdOverride.Length > 0 && component.Grids.Contains(Me.CubeGrid)) return gridIdOverride;

            var name = component.Root.CustomName == null ? "" : component.Root.CustomName.Trim();
            return IsDefaultGridName(name) ? "" : name;
        }

        // The game names a new grid "<size> Grid <id>". Only the English defaults are
        // recognised; on any other client, set [tagger] grid_id or rename the grid.
        private bool IsDefaultGridName(string name)
        {
            var prefixLength = 0;
            if (name.StartsWith("Small Grid ", StringComparison.Ordinal)) prefixLength = 11;
            else if (name.StartsWith("Large Grid ", StringComparison.Ordinal)) prefixLength = 11;
            else if (name.StartsWith("Static Grid ", StringComparison.Ordinal)) prefixLength = 12;
            else return false;

            if (prefixLength >= name.Length) return false;
            for (var i = prefixLength; i < name.Length; i++)
                if (!char.IsDigit(name[i])) return false;
            return true;
        }

        // Path segments for each grid in the component, keyed by grid. The root maps to "".
        private Dictionary<IMyCubeGrid, string> GetSubgridPaths(MechanicalComponent component, string gridId)
        {
            var paths = new Dictionary<IMyCubeGrid, string>();
            paths[component.Root] = "";

            var pending = new Queue<IMyCubeGrid>();
            pending.Enqueue(component.Root);

            while (pending.Count > 0)
            {
                var parent = pending.Dequeue();
                var parentPath = paths[parent];

                foreach (var link in component.Links)
                {
                    if (link.Parent != parent || paths.ContainsKey(link.Child)) continue;

                    // Parses the joint's Custom Data; the main pass parses it again as a block.
                    var overrideName = blockIni.TryParse(link.Joint.CustomData) ? ReadOverride() : "";
                    var label = GetBlockLabel(link.Joint, overrideName, gridId, parentPath);
                    paths[link.Child] = label.Length == 0 ? parentPath
                        : parentPath.Length == 0 ? label
                        : parentPath + " " + label;
                    pending.Enqueue(link.Child);
                }
            }

            return paths;
        }

        private string ReadOverride()
        {
            return blockIni.Get(ConfigSection, NameOverrideKey).ToString().Trim();
        }

        // The block's part of the name: its type name (or override) plus any text the user
        // added after it. The grid ID and subgrid path are added by the caller.
        private string GetBlockLabel(IMyTerminalBlock block, string overrideName, string gridId, string subgridPath)
        {
            var standardName = GetStandardBlockName(GetDefinitionName(block));
            var name = overrideName.Length > 0 ? overrideName : standardName;
            if (name.Length == 0) return "";

            var retained = GetRetainedText(block, standardName, overrideName, gridId, subgridPath);
            return retained.Length == 0 ? name : name + " " + retained;
        }

        // Modded blocks can leave this unset, in which case there is no type name to work from.
        private static string GetDefinitionName(IMyTerminalBlock block)
        {
            return block.DefinitionDisplayNameText == null ? "" : block.DefinitionDisplayNameText.Trim();
        }

        private string GetStandardBlockName(string definitionName)
        {
            var name = RemoveTrailingNumber(definitionName.Trim());
            string shortcut;
            return nameShortcuts.TryGetValue(name, out shortcut) ? shortcut : name;
        }

        // Text the user typed after the block's type name, which naming must not eat.
        // The prefix this script generates is removed first, so a grid called "Drill Rig"
        // cannot be mistaken for the drill's own name.
        private string GetRetainedText(
            IMyTerminalBlock block,
            string standardName,
            string overrideName,
            string gridId,
            string subgridPath)
        {
            var current = block.CustomName == null ? "" : block.CustomName.Trim();
            current = RemovePrefix(current, gridId + ".");
            if (subgridPath.Length > 0) current = RemovePrefix(current, subgridPath + " ");

            var bestIndex = -1;
            var bestLength = 0;
            MatchAlias(current, GetDefinitionName(block), ref bestIndex, ref bestLength);
            MatchAlias(current, standardName, ref bestIndex, ref bestLength);
            MatchAlias(current, overrideName, ref bestIndex, ref bestLength);
            if (bestIndex < 0) return "";

            var retained = current.Substring(bestIndex + bestLength).TrimStart();

            // Drop the game's automatic number, e.g. "LCD Panel 3 Cargo" keeps only "Cargo".
            var digits = 0;
            while (digits < retained.Length && char.IsDigit(retained[digits])) digits++;
            if (digits > 0 && (digits == retained.Length || char.IsWhiteSpace(retained[digits])))
                retained = retained.Substring(digits).TrimStart();

            return retained.Trim();
        }

        // Earliest whole-word match wins; the longest alias breaks a tie.
        private void MatchAlias(string current, string alias, ref int bestIndex, ref int bestLength)
        {
            if (string.IsNullOrWhiteSpace(alias)) return;

            var index = current.IndexOf(alias, StringComparison.OrdinalIgnoreCase);
            while (index >= 0 && !IsNameBoundary(current, index, alias.Length))
                index = current.IndexOf(alias, index + 1, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return;

            if (bestIndex < 0 || index < bestIndex || (index == bestIndex && alias.Length > bestLength))
            {
                bestIndex = index;
                bestLength = alias.Length;
            }
        }

        private static string RemovePrefix(string value, string prefix)
        {
            return prefix.Length > 1 && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(prefix.Length).TrimStart()
                : value;
        }

        private bool IsNameBoundary(string value, int index, int length)
        {
            var validStart = index == 0 || value[index - 1] == '.' || char.IsWhiteSpace(value[index - 1]);
            var end = index + length;
            var validEnd = end == value.Length || char.IsWhiteSpace(value[end]);
            return validStart && validEnd;
        }

        private string RemoveTrailingNumber(string value)
        {
            var end = value.Length - 1;
            while (end >= 0 && char.IsDigit(value[end])) end--;

            if (end == value.Length - 1 || end < 0 || !char.IsWhiteSpace(value[end])) return value;
            return value.Substring(0, end).TrimEnd();
        }

//
// GRID DISCOVERY
//

        private List<MechanicalComponent> GetScopedComponents()
        {
            var allBlocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(allBlocks);

            var components = GetMechanicalComponents(allBlocks, GetMechanicalLinks(allBlocks));
            if (!includeConnectedGrids)
                components = components.FindAll(component => component.Grids.Contains(Me.CubeGrid));

            var blockCount = 0;
            foreach (var component in components) blockCount += component.Blocks.Count;

            Echo(includeConnectedGrids
                ? "Found " + blockCount + " blocks, including connected grids"
                : "Found " + blockCount + " blocks on this grid and its mechanical subgrids");
            return components;
        }

        private List<MechanicalLink> GetMechanicalLinks(List<IMyTerminalBlock> blocks)
        {
            var links = new List<MechanicalLink>();
            foreach (var block in blocks)
            {
                var joint = block as IMyMechanicalConnectionBlock;
                if (joint == null || joint.TopGrid == null) continue;

                links.Add(new MechanicalLink { Parent = block.CubeGrid, Child = joint.TopGrid, Joint = block });
            }
            return links;
        }

        private List<MechanicalComponent> GetMechanicalComponents(
            List<IMyTerminalBlock> blocks,
            List<MechanicalLink> links)
        {
            var blocksByGrid = new Dictionary<IMyCubeGrid, List<IMyTerminalBlock>>();
            foreach (var block in blocks)
            {
                List<IMyTerminalBlock> gridBlocks;
                if (!blocksByGrid.TryGetValue(block.CubeGrid, out gridBlocks))
                    blocksByGrid[block.CubeGrid] = gridBlocks = new List<IMyTerminalBlock>();
                gridBlocks.Add(block);
            }

            var linksByGrid = new Dictionary<IMyCubeGrid, List<MechanicalLink>>();
            foreach (var link in links)
            {
                AddLink(linksByGrid, link.Parent, link);
                AddLink(linksByGrid, link.Child, link);
            }

            var components = new List<MechanicalComponent>();
            var visited = new HashSet<IMyCubeGrid>();
            var pending = new Queue<IMyCubeGrid>();

            foreach (var seed in blocksByGrid.Keys)
            {
                if (!visited.Add(seed)) continue;

                var grids = new List<IMyCubeGrid> { seed };
                var componentLinks = new List<MechanicalLink>();
                var childGrids = new HashSet<IMyCubeGrid>();
                pending.Enqueue(seed);

                while (pending.Count > 0)
                {
                    var grid = pending.Dequeue();
                    List<MechanicalLink> gridLinks;
                    if (!linksByGrid.TryGetValue(grid, out gridLinks)) continue;

                    foreach (var link in gridLinks)
                    {
                        var neighbour = link.Parent == grid ? link.Child : link.Parent;
                        if (!blocksByGrid.ContainsKey(neighbour)) continue;

                        if (link.Parent == grid)
                        {
                            componentLinks.Add(link);
                            childGrids.Add(link.Child);
                        }

                        if (visited.Add(neighbour))
                        {
                            grids.Add(neighbour);
                            pending.Enqueue(neighbour);
                        }
                    }
                }

                var componentBlocks = new List<IMyTerminalBlock>();
                IMyCubeGrid root = null;
                foreach (var grid in grids)
                {
                    componentBlocks.AddRange(blocksByGrid[grid]);
                    if (root == null && !childGrids.Contains(grid)) root = grid;
                }

                components.Add(new MechanicalComponent
                {
                    Blocks = componentBlocks,
                    Grids = new HashSet<IMyCubeGrid>(grids),
                    Links = componentLinks,
                    Root = root ?? seed
                });
            }

            return components;
        }

        private static void AddLink(
            Dictionary<IMyCubeGrid, List<MechanicalLink>> map,
            IMyCubeGrid grid,
            MechanicalLink link)
        {
            List<MechanicalLink> gridLinks;
            if (!map.TryGetValue(grid, out gridLinks)) map[grid] = gridLinks = new List<MechanicalLink>();
            gridLinks.Add(link);
        }
    }
}
