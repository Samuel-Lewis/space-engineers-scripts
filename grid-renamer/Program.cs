using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region mdk macros
        // This script was deployed at $MDK_DATETIME$
        #endregion

        #region mdk preserve
        /**
        * SCRIPT
        * Don't change anything below this line unless you know what you're doing
        */
        #endregion

        const string MetaScriptName = "Veeq's Grid Renamer";
        const string MetaScriptVersion = "v1.0.0";

        MyCommandLine _commandLine = new MyCommandLine();
        Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);

        public Program()
        {
            _commands["help"] = Help;
            _commands["prefix"] = Prefix;
            _commands["test"] = Test;
            _commands["antenna"] = Antenna;
            _commands["reset"] = Reset;
        }

        public void Help()
        {
            // TODO, improve this. Explain wild card. Explain that gridName is a filter
            Echo("Usage: <command> [gridName]");
            Echo("command (optional):");
            Echo("  prefix [gridName] - Prefix block names with grid");
            Echo("  antenna [gridName] - Show ship name on antennas");
            Echo("  reset [gridName] - Reset block names to default");
            Echo("  test [gridName] - List grid names");
            Echo("  help - Display this help message");
        }

        void PrintMeta()
        {
            Echo(MetaScriptName);
            Echo(MetaScriptVersion);
        }

        string WildcardToRegex(string pattern)
        {
            return "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        }

        List<IMyTerminalBlock> GetBlocks()
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(blocks);

            string gridFilterArg = _commandLine.Argument(1) ?? "*";
            System.Text.RegularExpressions.Regex gridFilter = new System.Text.RegularExpressions.Regex(WildcardToRegex(gridFilterArg));
            blocks = blocks.Where(block => gridFilter.IsMatch(block.CubeGrid.CustomName)).ToList();

            Echo($"Found {blocks.Count} blocks on grids {gridFilterArg}.");

            return blocks;
        }

        string GetPrefixName(IMyTerminalBlock block)
        {
            string gridName = block.CubeGrid.CustomName;
            return $"[{block.CubeGrid.CustomName}]";
        }


        void Prefix()
        {
            List<IMyTerminalBlock> blocks = GetBlocks();

            // Filter blocks
            blocks.RemoveAll(block =>
            {
                string gridName = block.CubeGrid.CustomName;
                string suggestedPrefix = GetPrefixName(block);

                return gridName.StartsWith("Small Grid") || gridName.StartsWith("Large Grid") || block.CustomName.StartsWith(suggestedPrefix);
            });

            foreach (var block in blocks)
            {
                string prefixName = GetPrefixName(block);
                block.CustomName = $"{prefixName} {block.CustomName}";
            }

            Echo($"Renamed {blocks.Count} blocks");
        }

        void Antenna()
        {
            List<IMyTerminalBlock> blocks = GetBlocks();
            List<IMyRadioAntenna> antennas = new List<IMyRadioAntenna>();
            antennas = blocks.OfType<IMyRadioAntenna>().ToList();

            foreach (var block in antennas)
            {
                block.ShowShipName = true;
            }

            Echo($"Configured {antennas.Count} antennas.");
        }

        void Reset()
        {
            List<IMyTerminalBlock> blocks = GetBlocks();

            foreach (var block in blocks)
            {
                block.CustomName = block.DefinitionDisplayNameText;
            }

            Echo($"Reset {blocks.Count} block names");
        }

        void Test()
        {
            List<IMyTerminalBlock> blocks = GetBlocks();
            HashSet<string> gridNames = new HashSet<string>();

            foreach (var block in blocks)
            {
                gridNames.Add(block.CubeGrid.CustomName);
            }

            Echo(string.Join(Environment.NewLine, gridNames));
        }

        public void Main(string argument)
        {
            PrintMeta();
            if (_commandLine.TryParse(argument))
            {
                Action commandAction;
                string command = _commandLine.Argument(0);
                if (command == null)
                {
                    Echo("No command specified");
                }
                else if (_commands.TryGetValue(_commandLine.Argument(0), out commandAction))
                {
                    commandAction();
                }
                else
                {
                    Echo($"Unknown command '{command}'. Use 'help' for a list of commands.");
                }
            }
            else
            {
                Prefix();
                Antenna();
            }
        }
    }
}
