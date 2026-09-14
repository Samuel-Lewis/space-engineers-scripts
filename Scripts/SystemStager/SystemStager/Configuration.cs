using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        sealed class StagerConfig
        {
            public bool AutoTakeoff, AutoLanding, CheckBatteries, CheckHydrogen, CheckOxygen, CheckFlightSystems, Stockpile;
            public double PreflightSeconds, ApproachSeconds, Threshold, DisplaySeconds;
            public string LandingProgram, LandingArgument, DisplayTag, StatusTag, DockingConnector;
            public readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, Color> Colours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        }

        sealed class ManagedBlock
        {
            public IMyTerminalBlock Block;
            public IniDocument Ini;
            public string Name;
            public string Category;
            public bool Managed, Readiness, Docking, StatusLight;
            public readonly Dictionary<string, string> Actions = new Dictionary<string, string>();
        }

        void Reload()
        {
            scanSeconds = 0;
            configErrors.Clear();
            configWarnings.Clear();
            config = ReadConfiguration();
            DiscoverBlocks();
            displaySeconds = config.DisplaySeconds;
        }

        bool ConfigurationChanged()
        {
            foreach (var item in blocks)
                if (item.Block.Closed || item.Ini.Changed || item.Block.CustomName != item.Name) return true;
            return false;
        }

        StagerConfig ReadConfiguration()
        {
            var ini = new IniDocument(Me, warning => AddUnique(configWarnings, warning));
            pbConfig = ini;
            var next = new StagerConfig();
            next.AutoTakeoff = ini.Bool(Section, "auto_takeoff", true);
            next.AutoLanding = ini.Bool(Section, "auto_landing", true);
            next.PreflightSeconds = ini.Double(Section, "preflight_delay_seconds", 1, 0, 3600);
            next.ApproachSeconds = ini.Double(Section, "approach_delay_seconds", 1, 0, 3600);
            next.Threshold = ini.Double(Section, "readiness_threshold_percent", 10, 0, 100);
            next.CheckBatteries = ini.Bool(Section, "check_batteries", true);
            next.CheckHydrogen = ini.Bool(Section, "check_hydrogen", true);
            next.CheckOxygen = ini.Bool(Section, "check_oxygen", false);
            next.CheckFlightSystems = ini.Bool(Section, "check_flight_systems", true);
            next.Stockpile = ini.Bool(Section, "stockpile_when_docked", true);
            next.DockingConnector = ini.String(Section, "docking_connector", "").Trim();
            next.LandingProgram = ini.String(Section, "landing_programmable_block", "Spugs Auto Landing",
                value => !string.IsNullOrWhiteSpace(value), "a PB name").Trim();
            next.LandingArgument = ini.String(Section, "landing_argument", "default_home");
            next.StatusTag = ini.String(Section, "status_light_tag", "status").Trim();
            next.DisplayTag = ini.String(Section, "display_tag", "[stager]").Trim();
            next.DisplaySeconds = ini.Double(Section, "display_update_seconds", 1, 0.1, 60);
            string[] colourStages = { "unknown", "docked", "preflight", "takeoff", "flight", "approach", "landing", "error" };
            string[] colours = { "128,128,128", "0,220,70", "255,210,0", "0,220,255", "40,100,255", "255,125,0", "180,60,255", "255,35,35" };
            for (int i = 0; i < colourStages.Length; i++)
                next.Colours[colourStages[i]] = ReadColour(ini, colourStages[i] + "_colour", colours[i]);

            string[] categories = { "battery", "reactor", "engine", "thruster", "gyro", "controller", "tank", "connector", "gear", "light", "ore_detector", "tool", "weapon", "door", "timer", "program", "other" };
            foreach (string phase in Stages)
                foreach (string category in categories)
                {
                    string key = phase + "." + category;
                    string action = NormaliseAction(ini.String(Section, key));
                    if (action.Length == 0) continue;
                    if (!BlockActions.Supports(category, action))
                        ini.Fallback(Section, key, "a supported " + category + " action", "");
                    else next.Defaults[key] = action;
                }
            return next;
        }

        void DiscoverBlocks()
        {
            blocks.Clear();
            var discovered = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType(discovered, b => b.IsSameConstructAs(Me));
            var surfaces = new List<IMyTextSurface>();
            foreach (var block in discovered)
            {
                bool self = block.EntityId == Me.EntityId;
                var blockIni = self ? pbConfig : new IniDocument(block, warning => AddUnique(configWarnings, warning));
                var item = new ManagedBlock
                {
                    Block = block,
                    Ini = blockIni,
                    Name = block.CustomName,
                    Category = BlockActions.Category(block),
                    Managed = !self && blockIni.Bool(Section, "managed", true),
                    Readiness = !self && blockIni.Bool(Section, "readiness", true)
                };
                bool docking = item.Category == "connector" && blockIni.Bool(Section, "docking", true);
                item.Docking = item.Managed && docking
                    && (config.DockingConnector.Length == 0 || string.Equals(block.CustomName, config.DockingConnector, StringComparison.OrdinalIgnoreCase));
                item.StatusLight = item.Managed && item.Category == "light" && NameContains(block, config.StatusTag);
                if (!self)
                {
                    foreach (string phase in Stages)
                    {
                        string action = NormaliseAction(blockIni.String(Section, phase));
                        if (action.Length == 0) continue;
                        string error;
                        if (!BlockActions.Validate(block, action, out error))
                            blockIni.Fallback(Section, phase, "a supported " + item.Category + " action", "");
                        else if (block is IMyProgrammableBlock
                            && string.Equals(block.CustomName, config.LandingProgram, StringComparison.OrdinalIgnoreCase)
                            && action.StartsWith("run:", StringComparison.OrdinalIgnoreCase))
                            blockIni.Fallback(Section, phase, "landing_argument on SystemStager instead of a hook on the landing PB", "");
                        else item.Actions[phase] = action;
                    }
                }
                blocks.Add(item);
                foreach (string phase in Stages) ActionFor(item, phase);
                DiscoverSurfaces(block, blockIni, surfaces);
                blockIni.CheckKeys(Section);
                blockIni.Save();
            }
            dashboard.SetSurfaces(surfaces);
            if (config.DockingConnector.Length > 0 && !blocks.Exists(b => b.Docking))
                AddUnique(configErrors, "Docking connector not found on this ship: " + config.DockingConnector);
        }

        static bool NameContains(IMyTerminalBlock block, string tag)
        {
            return tag.Length > 0 && block.CustomName.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string NormaliseAction(string action)
        {
            action = action.TrimStart();
            return action.StartsWith("run:", StringComparison.OrdinalIgnoreCase) ? action : action.TrimEnd();
        }

        Color ReadColour(IniDocument ini, string key, string defaultValue)
        {
            Color colour;
            string value = ini.String(Section, key, defaultValue,
                text => TryColour(text, out colour), "R,G,B with each channel from 0 to 255");
            TryColour(value, out colour);
            return colour;
        }

        static bool TryColour(string value, out Color colour)
        {
            string[] parts = value.Split(',');
            int r, g, b;
            if (parts.Length == 3 && int.TryParse(parts[0], out r) && int.TryParse(parts[1], out g) && int.TryParse(parts[2], out b)
                && r >= 0 && r <= 255 && g >= 0 && g <= 255 && b >= 0 && b <= 255)
            {
                colour = new Color(r, g, b);
                return true;
            }
            colour = Color.Black;
            return false;
        }

        void DiscoverSurfaces(IMyTerminalBlock block, IniDocument ini, List<IMyTextSurface> surfaces)
        {
            var panel = block as IMyTextPanel;
            var provider = block as IMyTextSurfaceProvider;
            int count = panel != null ? 1 : provider != null ? provider.SurfaceCount : 0;
            if (count == 0) return;
            int index = ini.Int(Section, "display_surface", -1, -1, count - 1);
            if (!NameContains(block, config.DisplayTag)) return;
            if (index == -1) index = panel != null ? 0 : LargestSurface(provider);
            surfaces.Add(panel != null ? (IMyTextSurface)panel : provider.GetSurface(index));
        }

        static int LargestSurface(IMyTextSurfaceProvider provider)
        {
            int best = 0;
            float area = 0;
            for (int i = 0; i < provider.SurfaceCount; i++)
            {
                var size = provider.GetSurface(i).SurfaceSize;
                float candidate = size.X * size.Y;
                if (candidate > area) { best = i; area = candidate; }
            }
            return best;
        }

        string ActionFor(ManagedBlock item, string phase)
        {
            if (!item.Managed) return "ignore";
            string action;
            if (item.Actions.TryGetValue(phase, out action)) return action;
            if (item.Category == "connector" && !item.Docking) return "ignore";
            string key = phase + "." + item.Category;
            if (config.Defaults.TryGetValue(key, out action) && action.Length > 0)
            {
                string error;
                bool landingHook = item.Block is IMyProgrammableBlock
                    && string.Equals(item.Name, config.LandingProgram, StringComparison.OrdinalIgnoreCase)
                    && action.StartsWith("run:", StringComparison.OrdinalIgnoreCase);
                if (!landingHook && BlockActions.Validate(item.Block, action, out error)) return action;
                AddUnique(configWarnings, item.Name + ": " + key
                    + " cannot be used on this block; using its built-in default.");
            }
            return BlockActions.DefaultAction(item.Category, phase, config.Stockpile);
        }

        bool ValidateStage(string phase)
        {
            actionErrors.Clear();
            foreach (var item in blocks)
            {
                string error;
                if (!BlockActions.Validate(item.Block, ActionFor(item, phase), out error)) AddUnique(actionErrors, error);
            }
            return actionErrors.Count == 0;
        }

        bool ApplyStage(string phase, bool hooks)
        {
            if (configErrors.Count > 0 || !ValidateStage(phase)) return false;
            if (!string.Equals(hookPhase, phase, StringComparison.OrdinalIgnoreCase))
            {
                hookPhase = phase;
                completedHooks.Clear();
            }
            // Settings first, hooks second, physical release last. A failed preparation never releases the ship.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var item in blocks)
                {
                    string action = ActionFor(item, phase);
                    bool hook = IsHook(action);
                    bool release = string.Equals(action, "disconnect", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "unlock", StringComparison.OrdinalIgnoreCase);
                    int actionPass = release ? 2 : hook ? 1 : 0;
                    if (actionPass != pass || (hook && !hooks)) continue;
                    if (hook && completedHooks.Contains(item.Block.EntityId)) continue;
                    string error;
                    if (!BlockActions.Execute(item.Block, action, out error)) AddUnique(actionErrors, error);
                    else if (hook) completedHooks.Add(item.Block.EntityId);
                }
                if (actionErrors.Count > 0) return false;
            }
            return true;
        }

        static bool IsHook(string action)
        {
            return action.StartsWith("run:", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "trigger", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "start", StringComparison.OrdinalIgnoreCase);
        }
    }
}
