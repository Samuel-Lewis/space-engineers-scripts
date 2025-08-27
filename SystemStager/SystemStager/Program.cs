using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
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
        // You shouldn't need to change anything below this line. It should all be configurable from CustomData.
        // But it's here if you really need.
        //


        List<string> default_stages = new List<string>() { "boot", "launch", "approach", "dock" };
        List<string> default_systems = new List<string>() { "production", "powersaver", "combat", "tools" };
        List<string> default_tags = new List<string>();

        // Custom lookup for DataConfig by TypeId and substring match on SubtypeId

        // Generated from a spreadsheet
        DataStore data_store = new DataStore()
            .Add("AirtightHangarDoor", null, "-0------", Actions.Door)
            .Add("Assembler", null, "----10--")
            .Add("BatteryBlock", null, "-0-1----", Actions.BatteryBlock)
            .Add("Beacon", null, "1-------")
            .Add("Cockpit", null, "01-0----", Actions.Cockpit)
            .Add("Decoy", null, "---0--1-")
            .Add("DefensiveCombatBlock", null, "--00----")
            .Add("Door", null, "-0------", Actions.Door)
            .Add("Drill", null, "--00---1")
            .Add("GravityGenerator", null, "-----0--")
            .Add("GravityGeneratorSphere", null, "-----0--")
            .Add("Gyro", null, "-1-0----")
            .Add("HydrogenEngine", null, "----1---")
            .Add("InteriorLight", null, "1--0--1-", Actions.Light)
            .Add("JumpDrive", null, "-----0--")
            .Add("LandingGear", null, "-0------", Actions.LandingGear)
            .Add("LargeGatlingTurret", "Gatling", "--00--1-")
            .Add("LargeGatlingTurret", "AutoCannon", "--00--1-")
            .Add("LargeMissileTurret", "LargeCalibre", "--00--1-")
            .Add("LargeMissileTurret", "MediumCalibre", "--00--1-")
            .Add("MotorAdvancedRotor", null, "---0----", Actions.Motor)
            .Add("MotorAdvancedStator", null, "---0----", Actions.Motor)
            .Add("MotorRotor", null, "---0----", Actions.Motor)
            .Add("OffensiveCombatBlock", null, "---0----")
            .Add("OreDetector", null, "---0-001")
            .Add("OxygenGenerator", null, "----1---")
            .Add("OxygenTank", "Oxygen", "-0-1----", Actions.Tank)
            .Add("OxygenTank", "Hydrogen", "-0-1----", Actions.Tank)
            .Add("Refinery", null, "----10--")
            .Add("ReflectorLight", null, "1--0---1")
            .Add("Searchlight", null, "1--0---1")
            .Add("ShipConnector", null, "-0-1----", Actions.ShipConnector)
            .Add("ShipGrinder", null, "---0---1")
            .Add("SmallGatlingGun", "Gatling", "---0--1-")
            .Add("SmallGatlingGun", "Autocannon", "---0--1-")
            .Add("SmallMissileLauncher", "Missile", "---0--1-")
            .Add("SmallMissileLauncher", "LargeCalibre", "---0--1-")
            .Add("SmallMissileLauncher", "Flare", "---0--1-")
            .Add("SmallMissileLauncherReload", "Rocket", "---0--1-")
            .Add("SmallMissileLauncherReload", "Railgun", "---0--1-")
            .Add("SmallMissileLauncherReload", "MediumCalibre", "---0--1-")
            .Add("SoundBlock", null, "-1-1----", Actions.SoundBlock)
            .Add("Thrust", null, "-1-0----");

        static string ini_prefix = "stager";
        static string ini_global = $"{ini_prefix}.config";
        static string ini_global_debug = $"{ini_prefix}.debug";
        static string ini_block_stager = $"{ini_prefix}.stages";
        static string ini_block_systems = $"{ini_prefix}.systems";


        SystemStatus status_off = new SystemStatus() { name = "standby", code = "STB", verb = "Standing down", color = Color.Red };
        SystemStatus status_on = new SystemStatus() { name = "engaged", code = "ENG", verb = "Engaging", color = Color.Green };
        SystemStatus status_error = new SystemStatus() { name = "error", code = "ERR", verb = "Error", color = Color.DarkViolet };
        SystemStatus status_partial = new SystemStatus() { name = "partial", code = "PRT", verb = "Partial", color = Color.Orange };

        //
        // SCRIPT
        // Don't change anything below this line unless you *really* know what you're doing
        // Go to https://github.com/samuel-Lewis/space-engineers-scripts if you want source or to contribute
        // 

        #endregion mdk preserve

        CLI cli;
        MyIni _ini = null;

        Dictionary<string, SystemStatus> current_systems = new Dictionary<string, SystemStatus>();
        int current_stage = 0;

        Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once;

            cli = new CLI("SystemStager", "1.0", Echo);
            cli.add("diagnostics", "Print diagnostics", DoDiagnostics);
            cli.add("stage", "Begin a stage", DoStage);
            cli.add("system", "Control a system", DoSystem);
            cli.add("debug", "Saves debug to CustomData", DoDebug);
            cli.set_default("diagnostics");
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (updateSource != UpdateType.Terminal)
            {
                return;
            }

            // On first boot, parse the default config
            default_tags.AddRange(default_stages);
            default_tags.AddRange(default_systems);

            // Run the command
            cli.run(argument);
        }

        public void DoStage()
        {

            string tag = cli.arg(1);

            if (string.IsNullOrWhiteSpace(tag))
            {
                current_stage++;
                current_stage = current_stage % default_stages.Count;
                tag = default_stages[current_stage];

                if (cli.truthy_switch())
                {
                    Echo($"Early exit at stage: {tag}");
                    return;
                }
            }

            if (!default_stages.Contains(tag))
            {
                Echo($"Error: Stage '{tag}' not found");
                return;
            }

            Echo($"Entering stage {tag}");
            bool result = RunTransition(tag);
        }

        public void DoSystem()
        {
            string tag = cli.arg(1);
            SystemStatus status = GetSystemStatus(tag);

            if (status == null)
            {
                return;
            }

            bool positive_transition = true;

            if (cli.truthy_switch())
            {
                status = status_on;
            }
            else if (cli.falsy_switch())
            {
                positive_transition = false;
                status = status_off;
            }
            else if (status == status_on || status == status_error)
            {
                positive_transition = false;
                status = status_off;
            }
            else if (status == status_off || status == status_partial)
            {
                status = status_on;
            }

            Echo($"{status.VerbString()} {tag}");
            bool result = RunTransition(tag, false, positive_transition);

            if (result)
            {
                Echo($"Success");
                SetSystemStatus(tag, status);
            }
            else
            {
                Echo($"Failed");
                SetSystemStatus(tag, status_error);
            }
        }

        public void DoDiagnostics()
        {
            Echo(":: Grid ::");
            Echo($"Grid Name: {Me.CubeGrid.CustomName}");

            Echo("\n:: Stage ::");
            Echo($"Current Stage: {default_stages[current_stage]}");

            Echo("\n:: Systems ::");
            GetAllSystems().ForEach(s =>
            {
                SystemStatus status = GetSystemStatus(s);
                Echo($"{status.ShortString()}: {s}");
            });
        }

        public void DoDebug()
        {
            Echo("Running Debug");

            _ini = new MyIni();
            MyIniParseResult result;
            if (!_ini.TryParse(Me.CustomData, out result))
            {
                Echo($"Error parsing CustomData: {result}");
                return;
            }

            _ini.Set(ini_global_debug, "version", cli.version);
            _ini.Set(ini_global_debug, "stages", string.Join(", ", default_stages));
            _ini.Set(ini_global_debug, "systems", string.Join(", ", GetAllSystems()));

            Me.CustomData = _ini.ToString();
        }

        public static Dictionary<string, Type> GetAllAvailableBlockTypes()
        {
            var blockTypes = new Dictionary<string, Type>();


            return blockTypes;
        }

        string ReadIni(string section, string key, IMyTerminalBlock block = null)
        {
            if (block == null)
            {
                block = Me;
            }

            _ini = new MyIni();
            MyIniParseResult result;

            if (!_ini.TryParse(block.CustomData, section, out result))
            {
                return null;
            }

            return _ini.Get(section, key).ToString();
        }

        List<string> GetAllSystems()
        {
            List<string> all_systems = new List<string>();
            all_systems.AddRange(default_systems);

            List<string> strings = ReadIni(ini_global, "systems").Split(',').ToList();
            all_systems.AddRange(strings.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList());

            return all_systems;
        }

        List<string> GetAllTags()
        {
            List<string> all_tags = new List<string>();
            all_tags.AddRange(default_stages);
            all_tags.AddRange(GetAllSystems());
            return all_tags;
        }

        SystemStatus GetSystemStatus(string system, bool use_color = false)
        {
            if (string.IsNullOrWhiteSpace(system))
            {
                Echo("Error: No system name provided");
                return null;
            }

            List<string> all_systems = GetAllSystems();
            if (!all_systems.Contains(system))
            {
                Echo($"GetSystemStatus Error: System '{system}' not found");
                return null;
            }

            SystemStatus status = current_systems.GetValueOrDefault(system, status_off);

            return status;
        }

        void SetSystemStatus(string system, SystemStatus status)
        {
            if (string.IsNullOrWhiteSpace(system))
            {
                Echo("Error: No system name provided");
                return;
            }
            List<string> all_systems = GetAllSystems();
            if (!all_systems.Contains(system))
            {
                Echo($"SetSystemStatus Error: System '{system}' not found");
                return;
            }
            current_systems[system] = status;
        }


        public bool RunTransition(string tag, bool check_stage = true, bool positive = true)
        {
            if (!GetAllTags().Contains(tag))
            {
                Echo($"Error: '{tag}' not found");
                return false;
            }

            int tag_index = default_tags.IndexOf(tag);
            List<IMyFunctionalBlock> blocks = new List<IMyFunctionalBlock>();

            GridTerminalSystem.GetBlocksOfType<IMyFunctionalBlock>(blocks, b => b.CubeGrid == Me.CubeGrid);

            int success = 0;
            int failure = 0;

            bool custom_tag = tag_index < 0 || tag_index >= default_tags.Count;

            foreach (var b in blocks)
            {
                // Check for override or custom tags in CustomData
                string custom = "";
                if (check_stage)
                {
                    custom = ReadIni(ini_block_stager, tag, b);
                }
                else
                {
                    custom = ReadIni(ini_block_systems, tag, b);
                }

                if (!string.IsNullOrWhiteSpace(custom))
                {
                    // TODO: Need to parse and act based on the custom data tag
                    continue;
                }

                // If no tag_index, then it's not a default tag, skippies
                if (custom_tag)
                {
                    continue;
                }

                // Search the default data
                DataConfig config = data_store.Search(b);
                if (config == null || !config.HasState(tag_index))
                {
                    continue;
                }

                bool new_state = config.State(tag_index).Value;
                bool result = Act(b, new_state, config.action, positive);
                if (result)
                {
                    success += 1;
                }
                else
                {
                    failure += 1;
                }
            }

            Echo($"Transition {tag} complete.\n{success} successes\n{failure} failures");
            return success > 0 && failure == 0;
        }

        public bool Act(IMyFunctionalBlock block, bool new_state, Action<IMyFunctionalBlock, bool> action, bool positive_transition = true)
        {
            if (!positive_transition)
            {
                new_state = !new_state;
            }

            try
            {
                Echo($"Setting {block.CustomName} to {(new_state ? "ON" : "OFF")}");
                action(block, new_state);
                return true;
            }
            catch (Exception e)
            {
                Echo($"Error on {block.CustomName}: {e.Message}");
                return false;
            }
        }
    }
}
