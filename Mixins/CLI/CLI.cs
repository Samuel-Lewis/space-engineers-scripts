using System;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public class CLI
    {
        private class Command
        {
            public string Description;
            public Action<string> Action;
        }

        private static readonly string[] TruthyNames = { "on", "enable", "true", "yes", "active", "1" };
        private static readonly string[] FalsyNames = { "off", "disable", "false", "no", "inactive", "0" };

        private readonly MyCommandLine commandLine = new MyCommandLine();
        private readonly Dictionary<string, Command> commands =
            new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string> echo;
        private readonly string name;

        private string defaultCommand = "help";

        public CLI(Program program, string name)
        {
            echo = program.Echo;
            this.name = name;
            Add("help", "List commands", Help);
        }

        public void Add(string command, string description, Action<string> action)
        {
            if (commands.ContainsKey(command))
                throw new ArgumentException("CLI: '" + command + "' already exists.");

            commands[command] = new Command { Description = description, Action = action };
        }

        public void SetDefault(string command)
        {
            if (!commands.ContainsKey(command))
                throw new ArgumentException("CLI: '" + command + "' does not exist.");

            defaultCommand = command;
        }

        public void Help(string argument = null)
        {
            echo(name + " | commands");
            foreach (var command in commands)
                echo("  " + command.Key + ": " + command.Value.Description);
        }

        public void Run(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                commands[defaultCommand].Action(null);
                return;
            }

            if (!commandLine.TryParse(input))
            {
                echo("! Could not parse '" + input + "'. Use help for a list of commands.");
                return;
            }

            var name = commandLine.Argument(0);
            if (name == null)
            {
                // Switches only, e.g. "--force": run the default command and keep the switches.
                commands[defaultCommand].Action(null);
                return;
            }

            Command command;
            if (!commands.TryGetValue(name, out command))
            {
                echo("! Unknown command '" + name + "'. Use help for a list of commands.");
                return;
            }

            command.Action(Arg(1));
        }

        public string Arg(int index)
        {
            return index < 0 || index >= commandLine.ArgumentCount ? null : commandLine.Argument(index);
        }

        public bool TruthySwitch() { return GetSwitch(TruthyNames); }

        public bool FalsySwitch() { return GetSwitch(FalsyNames); }

        public bool GetSwitch(string name) { return commandLine.Switch(name); }

        private bool GetSwitch(string[] names)
        {
            foreach (var name in names)
                if (commandLine.Switch(name)) return true;
            return false;
        }
    }
}
