using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;

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
        // Change these values to configure the script
        //

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
            {"BroadcastControllerBlock", new [] {"broadcast_controller"}},
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
        // Go to https://github.com/samuel-Lewis/space-engineers-scripts if you want source or to contribute
        // 

        #endregion mdk preserve

        private CLI cli;
        private MyIni _ini = new MyIni();

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once;

            cli = new CLI("Tagger", "1.1", Echo);
            cli.add("tag", "Tag blocks with INI tags", DoBlockTagging);
            cli.add("clear", "Clears [general] tags for all blocks", DoClearTags);
            cli.add("dump", "Debug: Dumps all known blocks to programmable block custom data", DoDump);
            cli.set_default("tag");
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (updateSource != UpdateType.Terminal)
            {
                return;
            }
            cli.run(argument);
        }

        public void DoDump()
        {
            var blocks = GetEligibleBlocks();
            _ini = new MyIni();
            var blockNames = blocks.Select(b => $"{b.CustomName}, {b.BlockDefinition.TypeIdString}, {b.BlockDefinition.SubtypeId}").ToList();

            var str = string.Join("\n ", blockNames);
            _ini.Set("debug", "blocks", str);
            Me.CustomData = _ini.ToString();
        }

        public void DoBlockTagging()
        {
            Echo("Starting tagging...");
            var blocks = GetEligibleBlocks();

            foreach (var block in blocks)
            {
                try
                {
                    _ini = new MyIni();
                    var tags = GetBlockTags(block);
                    var tagsString = string.Join(", ", tags);
                    _ini.Set("general", "tags", tagsString);
                    block.CustomData = _ini.ToString();
                }
                catch (Exception e)
                {
                    Echo($"Error processing block: {block.CustomName}\nError: {e.Message}");
                }
            }

            Echo("Tagging complete");
        }

        public void DoClearTags()
        {
            Echo("Starting tag cleanup...");
            var blocks = GetEligibleBlocks();

            foreach (var block in blocks)
            {
                try
                {
                    _ini = new MyIni();
                    _ini.Set("general", "tags", null);
                    block.CustomData = _ini.ToString();
                }
                catch (Exception e)
                {
                    Echo($"Error processing block: {block.CustomName}\nError: {e.Message}");
                }
            }

            Echo("Tagging complete");
        }
        public List<IMyTerminalBlock> GetEligibleBlocks()
        {
            var allBlocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(allBlocks, b => b.IsSameConstructAs(Me));

            var allBlocksCount = allBlocks.Count;
            var blocks = new List<IMyTerminalBlock>();

            blocks.AddRange(allBlocks.Where(b => MyIni.HasSection(b.CustomData, "general") || string.IsNullOrWhiteSpace(b.CustomData)));
            Echo($"Found {blocks.Count} out of {allBlocksCount}");

            if (blocks.Count == 0)
            {
                Echo("No eligible blocks found on grid");
            }
            return blocks;
        }

        public List<string> GetBlockTags(IMyTerminalBlock block)
        {
            // Get existing tags
            _ini = new MyIni();
            if (!_ini.TryParse(block.CustomData))
            {
                Echo($"Failed to parse CustomData for block: {block.CustomName}");
                return new List<string>();
            }

            var tags = _ini.Get("general", "tags").ToString().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToHashSet<string>();
            string typeId = block.BlockDefinition.TypeIdString;

            tags.Add("all");

            // Add tags from the blockTypeMappings
            foreach (var mapping in blockTypeMappings)
            {
                if (typeId.Contains(mapping.Key))
                {
                    foreach (var tag in mapping.Value)
                    {
                        tags.Add(tag);
                    }
                }
            }

            // Fall back for any unmapped but valid block types
            if (tags.Count == 0)
            {
                tags.Add(typeId.Replace("MyObjectBuilder_", "").ToLower());
            }

            return tags.ToList();
        }
    }
}
