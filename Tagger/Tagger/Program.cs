using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;

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
            {"Light", new [] {"light"}},
            {"Battery", new [] {"battery"}},
            {"Piston", new [] {"piston"}},
            {"Rotor", new [] {"rotor"}},
            {"Hinge", new [] {"hinge"}},
            {"Connector", new [] {"connector"}},
            {"Collector", new [] {"collector"}},
            {"Ejector", new [] {"ejector"}},
            {"Drill", new [] {"drill", "tool"}},
            {"Welder", new [] {"welder", "tool"}},
            {"Grinder", new [] {"grinder", "tool"}},
            {"Camera", new [] {"camera"}},
            {"Sensor", new [] {"sensor"}},
            {"RadioAntenna", new [] {"antenna"}},
            {"LaserAntenna", new [] {"laser_antenna"}},
            {"Beacon", new [] {"beacon"}},
            {"Thrust", new [] {"thruster"}},
            {"Gyro", new [] {"gyro"}},
            {"Reactor", new [] {"reactor", "generator"}},
            {"HydrogenEngine", new [] {"engine", "generator"}},
            {"Parachute", new [] {"parachute"}},
            {"Door", new [] {"door"}},
            {"LandingGear", new [] {"landing_gear"}},
            {"TextPanel", new [] {"lcd"}},
            {"LCD", new [] {"lcd"}},
            {"ButtonPanel", new [] {"button_panel"}},
            {"TimerBlock", new [] {"timer"}},
            {"ProgrammableBlock", new [] {"programmable_block"}},
            {"Assembler", new [] {"assembler", "production"}},
            {"Refinery", new [] {"refinery", "production"}},
            {"OxygenGenerator", new [] {"o2h2_generator", "generator"}},
            {"OxygenTank", new [] {"oxygen_tank", "tank"}},
            {"HydrogenTank", new [] {"hydrogen_tank", "tank"}},
            {"CargoContainer", new [] {"cargo"}},
            {"Cockpit", new [] {"cockpit"}},
            {"ControlStation", new [] {"cockpit"}},
            {"RemoteControl", new [] {"remote_control"}},
            {"UpgradeModule", new [] {"upgrade_module"}},
            {"GatlingTurret", new [] {"turret", "weapon"}},
            {"InteriorTurret", new [] {"turret", "weapon"}},
            {"MissileTurret", new [] {"turret", "weapon"}},
            {"GatlingGun", new [] {"weapon_fixed", "weapon"}},
            {"MissileLauncher", new [] {"weapon_fixed", "weapon"}}
        };

        //
        // SCRIPT
        // Don't change anything below this line unless you *really* know what you're doing
        // Go to https://github.com/samuel-Lewis/space-engineers-scripts if you want to source or to contribute
        // 

        #endregion mdk preserve

        private CLI cli;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once;

            cli = new CLI("Tagger", "1.0", Echo);
            cli.add("tag", "Tag blocks with YAML tags", DoBlockTagging);
            cli.add("nuke", "Deletes custom data on all blocks", DoClearCustomData);
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

        public void DoBlockTagging()
        {
            Echo("Starting block tagging...");

            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks, b => b.IsSameConstructAs(Me));

            if (blocks.Count == 0)
            {
                Echo("No blocks found on the grid.");
                return;
            }

            Echo($"Found {blocks.Count} blocks to process.");
            int blocksModified = 0;
            int blocksSkipped = 0;

            foreach (var block in blocks)
            {
                try
                {
                    string originalCustomData = block.CustomData;

                    if (!string.IsNullOrWhiteSpace(originalCustomData) && !originalCustomData.Contains("[general]"))
                    {
                        blocksSkipped++;
                        continue;
                    }

                    List<string> blockTags = GetBlockTags(block);
                    if (blockTags.Count == 0)
                    {
                        continue;
                    }

                    string currentCustomData = originalCustomData;
                    foreach (var tag in blockTags)
                    {
                        currentCustomData = SimpleYAML.SetOrUpdateTag(currentCustomData, "general", "tags", tag);
                    }

                    if (originalCustomData != currentCustomData)
                    {
                        block.CustomData = currentCustomData;
                        blocksModified++;
                    }
                }
                catch (Exception e)
                {
                    Echo($"Error processing block: {block.CustomName}\nError: {e.Message}");
                }
            }

            Echo("Block tagging process complete.");
            Echo($"Modified {blocksModified} blocks.");
            if (blocksSkipped > 0)
            {
                Echo($"Skipped {blocksSkipped} blocks with non-standard CustomData.");
            }
        }

        public void DoClearCustomData()
        {
            Echo("Starting custom data clearing...");
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks, b => b.IsSameConstructAs(Me));
            if (blocks.Count == 0)
            {
                Echo("No blocks found on the grid.");
                return;
            }
            Echo($"Found {blocks.Count} blocks to process.");
            int blocksCleared = 0;
            foreach (var block in blocks)
            {
                try
                {
                    block.CustomData = string.Empty;
                    blocksCleared++;
                }
                catch (Exception e)
                {
                    Echo($"Error clearing custom data for block: {block.CustomName}\nError: {e.Message}");
                }
            }
            Echo($"Cleared CustomData for {blocksCleared} blocks.");
        }

        public List<string> GetBlockTags(IMyTerminalBlock block)
        {
            var tags = new HashSet<string>(); // Use a HashSet to automatically prevent duplicate tags
            string typeId = block.BlockDefinition.TypeIdString;
            string subtypeId = block.BlockDefinition.SubtypeId;

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

            // Fallback for any unmapped but valid block types
            if (tags.Count == 0)
            {
                tags.Add(typeId.Replace("MyObjectBuilder_", "").ToLower());
            }

            return tags.ToList();
        }
    }
}