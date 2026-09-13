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
        // SCRIPT
        // Don't change anything below this line unless you *really* know what you're doing
        // Go to https://github.com/samuel-Lewis/space-engineers-scripts if you want source or to contribute
        //

        #endregion mdk preserve

        private CLI cli;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once;

            cli = new CLI(this, "Veeq's Grid Renamer", "1.2");
            cli.add("standardise", "standardise [gridName] - Standardise naming and prefix with grid name", Standardise);
            cli.add("prefix", "prefix [gridName] - Prefix block names with grid", Prefix);
            cli.add("test", "test [gridName] - List grid names", Test);
            cli.add("antenna", "antenna [gridName] - Show ship name on antennas", Antenna);
            cli.add("reset", "reset [gridName] - Reset block names to default", Reset);
            cli.set_default("standardise");
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (updateSource != UpdateType.Terminal)
            {
                return;
            }
            cli.run(argument);
        }

        private string WildcardToRegex(string pattern)
        {
            return "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        }

        private List<IMyTerminalBlock> GetBlocks(string gridFilterArg)
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(blocks);

            gridFilterArg = string.IsNullOrWhiteSpace(gridFilterArg) ? "*" : gridFilterArg;
            System.Text.RegularExpressions.Regex gridFilter = new System.Text.RegularExpressions.Regex(WildcardToRegex(gridFilterArg));
            blocks = blocks.Where(block => gridFilter.IsMatch(block.CubeGrid.CustomName)).ToList();

            Echo($"Found {blocks.Count} blocks on grids {gridFilterArg}.");

            return blocks;
        }

        private string GetPrefixName(IMyTerminalBlock block)
        {
            string gridName = block.CubeGrid.CustomName;
            return $"[{block.CubeGrid.CustomName}]";
        }

        private void Prefix(string gridFilterArg = null)
        {
            List<IMyTerminalBlock> blocks = GetBlocks(gridFilterArg);

            // Filter blocks
            blocks.RemoveAll(block =>
            {
                string gridName = block.CubeGrid.CustomName;
                string suggestedPrefix = GetPrefixName(block);

                return gridName.StartsWith("Small Grid") || gridName.StartsWith("Large Grid") || gridName.StartsWith("Static Grid") || block.CustomName.StartsWith(suggestedPrefix);
            });

            foreach (var block in blocks)
            {
                string prefixName = GetPrefixName(block);
                block.CustomName = $"{prefixName} {block.CustomName}";
            }

            Echo($"Renamed {blocks.Count} blocks");
        }

        private void Antenna(string gridFilterArg = null)
        {
            List<IMyTerminalBlock> blocks = GetBlocks(gridFilterArg);
            List<IMyRadioAntenna> antennas = new List<IMyRadioAntenna>();
            antennas = blocks.OfType<IMyRadioAntenna>().ToList();

            foreach (var block in antennas)
            {
                block.ShowShipName = true;
            }

            Echo($"Configured {antennas.Count} antennas.");
        }

        private bool HasDefaultName(IMyTerminalBlock block)
        {
            if (block.CustomName == "")
            {
                return true;
            }

            string prefix = GetPrefixName(block);

            string defaultName = block.DefinitionDisplayNameText;
            string pattern = $@"^({System.Text.RegularExpressions.Regex.Escape(prefix)})?\s*{System.Text.RegularExpressions.Regex.Escape(defaultName)}\s*\d*$";
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(pattern);

            return regex.IsMatch(block.CustomName);
        }

        private void Standardise(string gridFilterArg = null)
        {
            List<IMyTerminalBlock> blocks = GetBlocks(gridFilterArg);
            foreach (var block in blocks)
            {
                if (HasDefaultName(block))
                {
                    block.CustomName = GetSpecialNaming(block.DefinitionDisplayNameText);
                }
            }

            Prefix(gridFilterArg);
            Antenna(gridFilterArg);
        }

        private string GetSpecialNaming(string blockName)
        {
            switch (blockName)
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
                    return blockName;
            }
        }

        private void Reset(string gridFilterArg = null)
        {
            List<IMyTerminalBlock> blocks = GetBlocks(gridFilterArg);
            foreach (var block in blocks)
            {
                block.CustomName = block.DefinitionDisplayNameText;
            }

            Echo($"Force Reset {blocks.Count} block names");
        }

        private void Test(string gridFilterArg = null)
        {
            List<IMyTerminalBlock> blocks = GetBlocks(gridFilterArg);
            HashSet<string> gridNames = new HashSet<string>();

            foreach (var block in blocks)
            {
                gridNames.Add(block.CubeGrid.CustomName);
            }

            Echo(string.Join(Environment.NewLine, gridNames));
        }
    }
}
