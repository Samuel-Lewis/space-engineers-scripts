using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
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
            .Add("BatteryBlock", null, "-1-0----", Actions.BatteryBlock)
            .Add("Beacon", null, "1--0----")
            .Add("Cockpit", null, "0100----", Actions.Cockpit)
            .Add("Decoy", null, "---0--1-")
            .Add("DefensiveCombatBlock", null, "--00----")
            .Add("Door", null, "-0------", Actions.Door)
            .Add("Drill", null, "--00---1")
            .Add("FlightMovementBlock", null, "---0----")
            .Add("GravityGenerator", null, "-----0--")
            .Add("GravityGeneratorSphere", null, "-----0--")
            .Add("Gyro", null, "-1-0----")
            .Add("HydrogenEngine", null, "----1---")
            .Add("InteriorLight", null, "1-----11", Actions.Light)
            .Add("JumpDrive", null, "-----0--")
            .Add("LandingGear", null, "-0------", Actions.LandingGear)
            .Add("LargeGatlingTurret", "Gatling", "--00--1-")
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
            .Add("Searchlight", null, "1--0---1", Actions.Searchlight)
            .Add("ShipConnector", null, "-1-0----", Actions.ShipConnector)
            .Add("ShipGrinder", null, "---0---1")
            .Add("ShipWelder", null, "---0---1")
            .Add("SmallGatlingGun", null, "---0--1-")
            .Add("SmallMissileLauncher", null, "---0--1-")
            .Add("SmallMissileLauncherReload", null, "---0--1-")
            .Add("SoundBlock", null, "-1-1----", Actions.SoundBlock)
            .Add("Thrust", null, "-1-0----");

        static string ini_prefix = "stager";
        static string ini_global = $"{ini_prefix}.config";
        static string ini_global_debug = $"{ini_prefix}.debug";
        static string ini_block_stager = $"{ini_prefix}.stages";
        static string ini_block_systems = $"{ini_prefix}.systems";


        SystemStatus status_off = new SystemStatus() { name = "Standby", code = "STB", verb = "Standing down", color = Color.Red };
        SystemStatus status_on = new SystemStatus() { name = "Engaged", code = "ENG", verb = "Engaging", color = Color.Green };
        SystemStatus status_error = new SystemStatus() { name = "Error", code = "ERR", verb = "Error", color = Color.DarkViolet };
        SystemStatus status_partial = new SystemStatus() { name = "Partial", code = "PRT", verb = "Partial", color = Color.Orange };

        //
        // SCRIPT
        // Don't change anything below this line unless you *really* know what you're doing
        // Go to https://github.com/samuel-Lewis/space-engineers-scripts if you want source or to contribute
        // 

        #endregion mdk preserve


        CLI cli;
        DisplayLog log;
        DisplayStatus display_status;
        MyIni _ini = null;

        Dictionary<string, SystemStatus> current_systems = new Dictionary<string, SystemStatus>();
        int current_stage = 0;


        CockpitEvent cockpit_event;
        ConnectorEvent connector_event;

        Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            log = new DisplayLog(this, "Stager");
            display_status = new DisplayStatus(this, "Stager");

            cli = new CLI(this, "SystemStager", "1.0");
            cli.add("stage", "[tag]: Start next stage, or start [tag].", DoStage);
            cli.add("system", "<tag>: Toggle system. Force with -on/-off", DoSystem);
            cli.add("diagnostics", "Print diagnostics", DoDiagnostics);
            cli.add("debug", "Saves debug to CustomData", DoDebug);
            cli.add("clear", "Clears the display", log.Clear);
            cli.set_default("diagnostics");

            // On first boot, parse the default config
            default_tags.AddRange(default_stages);
            default_tags.AddRange(default_systems);

            // Event Handlers
            cockpit_event = new CockpitEvent(this, OnCockpitEntered, OnCockpitExited);
            connector_event = new ConnectorEvent(this, OnConnectorConnected);

            // UI Updates
            display_status.AddStat("Stage", () => default_stages[current_stage]);
            display_status.AddStat("Cockpit", () => cockpit_event.IsDetected() ? "Occupied" : "Empty");
            display_status.AddStat("Connector", () => connector_event.IsDetected() ? "Docked" : "Free");
        }

        public void Main(string argument, UpdateType updateSource)
        {

            if ((updateSource & (UpdateType.Update100 | UpdateType.Update10)) != 0)
            {
                cockpit_event.Poll();
                connector_event.Poll();
                display_status.Update();
            }

            if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal)) != 0)
            {
                cli.run(argument);
                return;
            }
        }

        public void OnCockpitEntered(List<IMyCockpit> cockpits)
        {
            if (current_stage == 3) // Docked
            {
                DoStage("boot");
            }
        }

        public void OnCockpitExited()
        {
            if (current_stage == 0) // Booted
            {
                DoStage("dock");
            }
        }

        public void OnConnectorConnected(List<IMyShipConnector> cockpits)
        {
            DoStage("dock");
        }

        public void OnConnectorDisconnected()
        {
            // TODO: Maybe launch?
        }


        public void DoStage(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                current_stage++;
                current_stage = current_stage % default_stages.Count;
                tag = default_stages[current_stage];
            }
            log.Echo($"{status_on.VerbString()} {tag.ToUpper()}");

            if (!default_stages.Contains(tag))
            {
                log.EchoError($"'{tag}' not found");
                return;
            }

            bool result = RunTransition(tag);
        }

        public void DoSystem(string tag)
        {
            log.Echo($"Selecting {tag.ToUpper()}");
            if (!AssertSystem(tag))
            {
                return;
            }

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

            log.Echo($"{status.VerbString()} system {tag.ToUpper()}");
            bool result = RunTransition(tag, false, positive_transition);

            if (result)
            {
                log.Echo($"{status.NameString()} system {tag.ToUpper()}");
                SetSystemStatus(tag, status);
            }
            else
            {
                log.Echo($"{status_error.NameString()} system {tag.ToUpper()}");
                SetSystemStatus(tag, status_error);
            }
        }

        public void DoDiagnostics(string next_arg)
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

        public void DoDebug(string next_arg)
        {
            log.Echo("Running Debug...");

            _ini = new MyIni();
            MyIniParseResult result;
            if (!_ini.TryParse(Me.CustomData, out result))
            {
                log.EchoError($"CustomData parse: {result}");
            }

            _ini.AddSection(ini_global_debug);

            List<string> all_systems = GetAllSystems();
            if (all_systems == null || all_systems.Count == 0)
            {
                all_systems = new List<string>();
            }
            _ini.Set(ini_global_debug, "systems", string.Join(", ", all_systems));
            _ini.Set(ini_global_debug, "stages", string.Join(", ", default_stages));
            _ini.Set(ini_global_debug, "version", cli.version);


            Me.CustomData = _ini.ToString();
            log.Echo("Debug Complete! See CustomData");
        }



        List<string> GetAllSystems()
        {
            List<string> all_systems = new List<string>();
            all_systems.AddRange(default_systems);

            List<string> strings = IniHandler.GetStringList(Me, ini_global, "systems");
            Echo(string.Join(", ", strings));
            all_systems.AddRange(strings);

            return all_systems;
        }

        List<string> GetAllTags()
        {
            List<string> all_tags = new List<string>();
            all_tags.AddRange(default_stages);
            all_tags.AddRange(GetAllSystems());
            return all_tags;
        }

        bool AssertSystem(string system)
        {
            if (string.IsNullOrWhiteSpace(system))
            {
                log.EchoError("No system name provided");
                return false;
            }
            List<string> all_systems = GetAllSystems();
            if (!all_systems.Contains(system))
            {
                log.EchoError($"'{system}' not found");
                return false;
            }
            return true;
        }

        SystemStatus GetSystemStatus(string system)
        {
            SystemStatus status = current_systems.GetValueOrDefault(system, status_off);
            return status;
        }

        void SetSystemStatus(string system, SystemStatus status)
        {
            current_systems[system] = status;
        }

        public bool RunTransition(string tag, bool check_stage = true, bool positive = true)
        {
            if (!GetAllTags().Contains(tag))
            {
                log.EchoError($"'{tag}' not found");
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
                    custom = IniHandler.GetString(b, ini_block_stager, tag);
                }
                else
                {
                    custom = IniHandler.GetString(b, ini_block_systems, tag);
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

            log.Echo($"Transition {tag} complete.");
            log.Echo($"{success} successes");
            log.Echo($"{failure} failures");
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
                action(block, new_state);
                return true;
            }
            catch (Exception e)
            {
                log.EchoError($"{block.CustomName}: {e.Message}");
                return false;
            }
        }
    }
}
