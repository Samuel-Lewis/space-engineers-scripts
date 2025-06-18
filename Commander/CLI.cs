using System;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;
using Sandbox.ModAPI.Ingame;

using System;
using System.Collections.Generic;

public class CLI
{
    private MyCommandLine commandLine = new MyCommandLine();
    private Dictionary<string, Action> commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private string default_command = "help";

    private string name = "";
    private string version = "";
    private Action<string> Echo;

    public CLI(string n, string v, Action<string> echo)
    {
        name = n;
        version = v;
        Echo = echo;
        add("help", "Display help info", help);
    }

    public void add(string command, string description, Action action)
    {
        if (commands.ContainsKey(command))
        {
            throw new ArgumentException($"Command '{command}' already exists.");
        }
        commands[command] = action;
        descriptions[command] = description;
    }

    public void help()
    {
        Echo(name);
        Echo($"Version: {version}");
        Echo("---");
        Echo("Available commands:");
        foreach (var cmd in commands)
        {
            Echo($"  {cmd.Key}: {descriptions[cmd.Key]}");
        }
    }

    public void set_default(string command)
    {
        if (!commands.ContainsKey(command))
        {
            throw new ArgumentException($"Command '{command}' does not exist.");
        }
        default_command = command;
    }

    public void run(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            run(default_command);
            return;
        }

        if (commandLine.TryParse(input))
        {
            Action commandAction;
            string command = commandLine.Argument(0);
            if (command == null)
            {
                run(default_command);
                return;
            }
            else if (commands.TryGetValue(commandLine.Argument(0), out commandAction))
            {
                commandAction();
            }
            else
            {
                Echo($"Unknown command '{command}'. Use 'help' for a list of commands.");
            }
        }
    }
}