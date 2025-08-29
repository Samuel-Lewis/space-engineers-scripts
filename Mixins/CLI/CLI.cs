using IngameScript;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;

public class CLI
{
    private MyCommandLine commandLine = new MyCommandLine();
    private Dictionary<string, Action> commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private string default_command = "help";

    private string name = "";
    public string version = "";
    private Action<string> Echo;

    private Program program;

    public CLI(Program prog, string n, string v)
    {
        program = prog;
        Echo = prog.Echo;
        name = n;
        version = v;
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
                Echo($"Unknown command '{command}'.\nUse 'help' for a list of commands.");
            }
        }
    }

    public string arg(int index)
    {
        if (index < 0 || index >= commandLine.ArgumentCount)
        {
            return null;
        }
        return commandLine.Argument(index);
    }



    public bool truthy_switch()
    {
        return get_switch(new List<string> { "on", "enable", "true", "yes", "active", "1" });
    }

    public bool falsy_switch()
    {
        return get_switch(new List<string> { "off", "disable", "false", "no", "inactive", "0" });
    }

    private bool get_switch(List<string> names)
    {
        List<string> switches = commandLine.Switches.ToList().Select(s => s.ToLower().Trim()).ToList();
        foreach (string name in names)
        {
            if (switches.Contains(name.ToLower().Trim()))
            {
                return true;
            }
        }
        return false;
    }
    public bool get_switch(string name)
    {
        return commandLine.Switch(name);
    }
}