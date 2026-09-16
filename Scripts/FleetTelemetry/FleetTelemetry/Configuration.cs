using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;

namespace IngameScript
{
    public partial class Program
    {
        sealed class FleetConfig
        {
            public string Callsign = "", Channel = "FleetTelemetry", Cockpit = "";
            public double StaleSeconds = 8, DropSeconds = 60;
        }

        // One surface of an opted-in block and the role it plays.
        sealed class Screen
        {
            public IMyTextSurface Surface;
            public string Role;
            public bool HeadingUp;
            public string TrackTarget;
        }

        static readonly string[] Roles = { "onboard", "fleet", "map", "track" };
        readonly List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
        readonly List<Screen> screens = new List<Screen>();
        readonly List<IMyShipConnector> ports = new List<IMyShipConnector>();
        readonly List<IMyShipConnector> allConnectors = new List<IMyShipConnector>();
        readonly List<string> configWarnings = new List<string>();
        // Blocks that opted in, so their Custom Data edits and renames trigger a rescan.
        readonly List<IniDocument> blockConfigs = new List<IniDocument>();
        readonly List<IMyTerminalBlock> blockConfigOwners = new List<IMyTerminalBlock>();
        readonly List<string> blockConfigNames = new List<string>();
        FleetConfig config = new FleetConfig();
        IniDocument pbConfig;
        IMyShipController cockpit;

        void Reload()
        {
            scanSeconds = 0;
            configWarnings.Clear();
            screens.Clear();
            config = ReadConfiguration();
            DiscoverBlocks();
        }

        bool ConfigurationChanged()
        {
            if (pbConfig != null && pbConfig.Changed) return true;
            for (int i = 0; i < blockConfigs.Count; i++)
                if (blockConfigOwners[i].Closed || blockConfigs[i].Changed || blockConfigOwners[i].CustomName != blockConfigNames[i])
                    return true;
            return false;
        }

        FleetConfig ReadConfiguration()
        {
            var ini = new IniDocument(Me, warning => AddUnique(configWarnings, warning));
            pbConfig = ini;
            var next = new FleetConfig();
            next.Callsign = ini.String(Section, "callsign", "").Trim();
            next.Channel = ini.String(Section, "channel", "FleetTelemetry",
                value => !string.IsNullOrWhiteSpace(value), "a broadcast tag").Trim();
            next.StaleSeconds = ini.Double(Section, "stale_seconds", 8, 1, 86400);
            next.DropSeconds = ini.Double(Section, "drop_seconds", 60, 1, 86400);
            if (next.DropSeconds < next.StaleSeconds)
            {
                ini.Fallback(Section, "drop_seconds", "a value of at least stale_seconds", next.StaleSeconds.ToString("0.#"));
                next.DropSeconds = next.StaleSeconds;
            }
            next.Cockpit = ini.String(Section, "cockpit", "").Trim();
            // The PB's own screens always have display keys, blank by default.
            ReadDisplays(Me, ini, false);
            ini.CheckKeys(Section);
            ini.Save();
            return next;
        }

        // A block opts in to per-block settings by carrying the name tag or a [fleet]
        // section. Its section is then completed with defaults, like the PB's. Other
        // blocks are read by type only and never written to.
        void DiscoverBlocks()
        {
            blocks.Clear();
            ports.Clear();
            allConnectors.Clear();
            blockConfigs.Clear();
            blockConfigOwners.Clear();
            blockConfigNames.Clear();
            GridTerminalSystem.GetBlocksOfType(blocks, b => b.IsSameConstructAs(Me));
            cockpit = null;
            IMyShipController mainCockpit = null, anyController = null;
            foreach (IMyTerminalBlock block in blocks)
            {
                var controller = block as IMyShipController;
                if (controller != null)
                {
                    if (config.Cockpit.Length > 0)
                    {
                        if (string.Equals(controller.CustomName, config.Cockpit, StringComparison.OrdinalIgnoreCase))
                            cockpit = controller;
                    }
                    else
                    {
                        if (controller.IsMainCockpit && mainCockpit == null) mainCockpit = controller;
                        if (anyController == null) anyController = controller;
                    }
                }
                var connector = block as IMyShipConnector;
                if (connector != null) allConnectors.Add(connector);

                if (block.EntityId == Me.EntityId) continue;
                bool tagged = block.CustomName.IndexOf(NameTag, StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasSection = (block.CustomData ?? "").IndexOf(NameTag, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!tagged && !hasSection) continue;
                var ini = new IniDocument(block, warning => AddUnique(configWarnings, warning));
                blockConfigs.Add(ini);
                blockConfigOwners.Add(block);
                blockConfigNames.Add(block.CustomName);
                if (connector != null)
                {
                    if (ini.Bool(Section, "dock_port", tagged)) ports.Add(connector);
                }
                else ReadDisplays(block, ini, tagged);
                ini.CheckKeys(Section);
                ini.Save();
            }

            // Dock port rule: one connector is the port; several need dock_port=true.
            if (ports.Count == 0)
            {
                if (allConnectors.Count == 1) ports.Add(allConnectors[0]);
                else if (allConnectors.Count > 1)
                    AddUnique(configWarnings, allConnectors.Count + " connectors found. Mark the dock port with dock_port=true; reporting FLIGHT until then.");
            }

            if (cockpit == null) cockpit = mainCockpit ?? anyController;
            if (config.Cockpit.Length > 0 && cockpit == null)
                AddUnique(configWarnings, "Cockpit not found: " + config.Cockpit + ". Speed, heading, altitude and heading-up maps need one.");
            else if (cockpit == null)
                AddUnique(configWarnings, "No cockpit or remote control: speed, heading and altitude are unknown.");
        }

        // One display_<n> key per surface: a role, optionally followed by its option.
        //   display_0=map heading      display_1=track Miner 1      display_2=
        // A tagged block shows onboard on display_0 by default.
        void ReadDisplays(IMyTerminalBlock block, IniDocument ini, bool tagged)
        {
            var panel = block as IMyTextPanel;
            var provider = block as IMyTextSurfaceProvider;
            int count = panel != null ? 1 : provider != null ? provider.SurfaceCount : 0;
            if (count == 0)
            {
                if (tagged) AddUnique(configWarnings, block.CustomName + ": tagged " + NameTag + " but has no screen.");
                return;
            }
            for (int index = 0; index < count; index++)
            {
                string key = "display_" + index;
                string value = ini.String(Section, key, tagged && index == 0 ? "onboard" : "",
                    text => RoleOf(text).Length > 0 || text.Trim().Length == 0,
                    "onboard, fleet, map [north|heading], track <grid name> or blank").Trim();
                string role = RoleOf(value);
                if (role.Length == 0) continue;
                string option = value.Substring(role.Length).Trim();
                var screen = new Screen
                {
                    Role = role,
                    Surface = panel != null ? (IMyTextSurface)panel : provider.GetSurface(index)
                };
                if (role == "map")
                {
                    string mode = option.ToLowerInvariant();
                    if (mode.Length > 0 && mode != "north" && mode != "heading")
                        ini.Fallback(Section, key, "map north or map heading", "map north");
                    screen.HeadingUp = mode == "heading";
                }
                else if (role == "track")
                {
                    screen.TrackTarget = option;
                    if (option.Length == 0) ini.Fallback(Section, key, "track <grid name>", "track");
                }
                else if (option.Length > 0)
                    ini.Fallback(Section, key, role + " with no option", role);
                dashboard.Prepare(screen.Surface);
                screens.Add(screen);
            }
        }

        // The leading role word of a display_<n> value, or "" when it is not one.
        static string RoleOf(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < Roles.Length; i++)
            {
                string role = Roles[i];
                if (value == role || value.StartsWith(role + " ")) return role;
            }
            return "";
        }

        static void AddUnique(List<string> target, string text)
        {
            if (!target.Contains(text)) target.Add(text);
        }
    }
}
