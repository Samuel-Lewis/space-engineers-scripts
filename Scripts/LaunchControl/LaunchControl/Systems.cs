using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program
    {
        enum SystemAction { Ignore, On, Off, Recharge, Auto, Discharge, Stockpile }

        class ManagedBlock
        {
            public IMyFunctionalBlock Block;
            public SystemAction Docked;
            public SystemAction Flight;
        }

        // One surface showing the dashboard, and whether it carries the script's title.
        sealed class Screen
        {
            public IMyTextSurface Surface;
            public bool ShowTitle;
        }

        readonly List<ManagedBlock> managed = new List<ManagedBlock>();
        readonly List<IMyShipConnector> ports = new List<IMyShipConnector>();
        readonly List<IMyLightingBlock> statusLights = new List<IMyLightingBlock>();
        readonly List<Screen> screens = new List<Screen>();
        readonly List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
        readonly List<IMyGasTank> hydrogenTanks = new List<IMyGasTank>();
        readonly List<IMyThrust> thrusters = new List<IMyThrust>();
        readonly List<IMyGyro> gyros = new List<IMyGyro>();
        readonly List<string> scanWarnings = new List<string>();
        readonly List<IMyTerminalBlock> scanBuffer = new List<IMyTerminalBlock>();
        readonly List<IMyShipConnector> allConnectors = new List<IMyShipConnector>();
        readonly List<IMyPowerProducer> hostPowerBuffer = new List<IMyPowerProducer>();
        readonly List<Screen> pbScreens = new List<Screen>();
        // Surfaces found per opted-in block, so 'config' can say which display_<n> keys
        // are available without writing blank ones into Custom Data.
        readonly List<string> surfaceCounts = new List<string>();
        readonly List<string> pbSurfaceCounts = new List<string>();
        readonly GridMetrics metrics = new GridMetrics();
        // Blocks that opted in, so their Custom Data edits and renames trigger a rescan.
        readonly List<IniDocument> blockConfigs = new List<IniDocument>();
        readonly List<IMyTerminalBlock> blockConfigOwners = new List<IMyTerminalBlock>();
        readonly List<string> blockConfigNames = new List<string>();
        string portProblem;
        string hostPowerWarning;

        // A block opts in to per-block settings by carrying the name tag or a
        // [launchcontrol] section. Its section is then completed with defaults, like
        // the PB's. Untagged blocks without the section are read with defaults only
        // and never written to.
        void ScanBlocks()
        {
            managed.Clear();
            ports.Clear();
            statusLights.Clear();
            screens.Clear();
            batteries.Clear();
            hydrogenTanks.Clear();
            thrusters.Clear();
            gyros.Clear();
            scanWarnings.Clear();
            allConnectors.Clear();
            blockConfigs.Clear();
            blockConfigOwners.Clear();
            blockConfigNames.Clear();
            surfaceCounts.Clear();
            // The PB's own screens are read with the configuration, before this scan.
            screens.AddRange(pbScreens);
            surfaceCounts.AddRange(pbSurfaceCounts);

            scanBuffer.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(scanBuffer, block => block.IsSameConstructAs(Me));

            foreach (IMyTerminalBlock block in scanBuffer)
            {
                if (block.EntityId == Me.EntityId) continue;
                bool tagged = block.CustomName.IndexOf(NameTag, StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasSection = (block.CustomData ?? "").IndexOf(NameTag, StringComparison.OrdinalIgnoreCase) >= 0;
                IniDocument ini = null;
                if (tagged || hasSection)
                {
                    ini = new IniDocument(block, warning => scanWarnings.Add(warning));
                    blockConfigs.Add(ini);
                    blockConfigOwners.Add(block);
                    blockConfigNames.Add(block.CustomName);
                }

                IMyShipConnector connector = block as IMyShipConnector;
                if (connector != null)
                {
                    allConnectors.Add(connector);
                    if (ini != null && ini.Bool(Section, "dock_port", tagged)) ports.Add(connector);
                    Finish(ini);
                    continue;
                }

                IMyLightingBlock light = block as IMyLightingBlock;
                if (light != null && ini != null && ini.Bool(Section, "status_light", tagged))
                {
                    statusLights.Add(light);
                    Finish(ini);
                    continue;
                }

                if (ini != null) ReadDisplays(block, ini, tagged);

                bool ignore = ini != null && ini.Bool(Section, "ignore", false);
                IMyFunctionalBlock functional = block as IMyFunctionalBlock;
                if (ignore || functional == null || NeverManaged(block))
                {
                    Finish(ini);
                    continue;
                }

                if (block is IMyBatteryBlock) batteries.Add((IMyBatteryBlock)block);
                else if (block is IMyGasTank && GridMetrics.IsHydrogenTank(block)) hydrogenTanks.Add((IMyGasTank)block);
                else if (block is IMyThrust) thrusters.Add((IMyThrust)block);
                else if (block is IMyGyro) gyros.Add((IMyGyro)block);

                SystemAction docked;
                SystemAction flight;
                bool hasRule = DefaultRule(functional, out docked, out flight);
                // Both are overrides of the default rule for the block's type, so an
                // absent key stays absent rather than being written back blank.
                string dockedValue = ini == null ? "" : ini.Optional(Section, "docked").Trim();
                string flightValue = ini == null ? "" : ini.Optional(Section, "flight").Trim();
                if (dockedValue.Length > 0) { docked = ParseAction(functional, "docked", dockedValue); hasRule = true; }
                if (flightValue.Length > 0) { flight = ParseAction(functional, "flight", flightValue); hasRule = true; }
                Finish(ini);
                if (!hasRule) continue;

                managed.Add(new ManagedBlock { Block = functional, Docked = docked, Flight = flight });
            }

            portProblem = null;
            if (ports.Count == 0)
            {
                if (allConnectors.Count == 1) ports.Add(allConnectors[0]);
                else if (allConnectors.Count == 0) portProblem = "No connector on this grid.";
                else portProblem = allConnectors.Count + " connectors found. Mark the dock port with dock_port=true.";
            }

            lastLightKey = "";
        }

        static void Finish(IniDocument ini)
        {
            if (ini == null) return;
            ini.CheckKeys(Section);
            ini.Save();
        }

        bool BlockConfigurationChanged()
        {
            for (int i = 0; i < blockConfigs.Count; i++)
                if (blockConfigOwners[i].Closed || blockConfigs[i].Changed || blockConfigOwners[i].CustomName != blockConfigNames[i])
                    return true;
            return false;
        }

        // One display_<n> key per surface; "status" shows the dashboard. A tagged block
        // shows it on display_0 by default; every other surface is optional, so its key
        // is only written once it has been given a role.
        void ReadDisplays(IMyTerminalBlock block, IniDocument ini, bool tagged)
        {
            IMyTextPanel panel = block as IMyTextPanel;
            IMyTextSurfaceProvider provider = block as IMyTextSurfaceProvider;
            int count = panel != null ? 1 : provider != null ? provider.SurfaceCount : 0;
            bool self = block.EntityId == Me.EntityId;
            if (self)
            {
                pbScreens.Clear();
                pbSurfaceCounts.Clear();
            }
            if (count == 0) return;
            // Only blocks that actually have screens get a title setting.
            bool showTitle = ini.Bool(Section, "show_title", true);
            List<Screen> target = self ? pbScreens : screens;
            (self ? pbSurfaceCounts : surfaceCounts)
                .Add(block.CustomName + " (display_0" + (count > 1 ? "-display_" + (count - 1) : "") + ")");
            Func<string, bool> valid = text => text.Trim().Length == 0
                || string.Equals(text.Trim(), "status", StringComparison.OrdinalIgnoreCase);
            for (int index = 0; index < count; index++)
            {
                string key = "display_" + index;
                string value = tagged && index == 0
                    ? ini.String(Section, key, "status", valid, "status or blank").Trim()
                    : ini.Optional(Section, key, valid, "status or blank").Trim();
                if (value.Length == 0) continue;
                IMyTextSurface surface = panel != null ? (IMyTextSurface)panel : provider.GetSurface(index);
                dashboard.Prepare(surface);
                target.Add(new Screen { Surface = surface, ShowTitle = showTitle });
            }
        }

        // The pilot, the script, and anything that sequences other blocks are never touched.
        static bool NeverManaged(IMyTerminalBlock block)
        {
            string type = block.BlockDefinition.TypeIdString;
            return block is IMyShipController || block is IMyProgrammableBlock
                || type.EndsWith("TimerBlock") || type.EndsWith("EventControllerBlock")
                || type.EndsWith("ButtonPanel");
        }

        static bool DefaultRule(IMyFunctionalBlock block, out SystemAction docked, out SystemAction flight)
        {
            docked = SystemAction.Ignore;
            flight = SystemAction.Ignore;
            if (block is IMyBatteryBlock)
            {
                docked = SystemAction.Recharge;
                flight = SystemAction.Auto;
                return true;
            }
            if (block is IMyGasTank)
            {
                if (!GridMetrics.IsHydrogenTank(block)) return false;
                docked = SystemAction.Stockpile;
                flight = SystemAction.Auto;
                return true;
            }
            // Antennas are left alone so IGC scripts such as FleetTelemetry keep
            // talking while docked.
            if (block is IMyThrust || block is IMyGyro || block is IMyReactor || block is IMyGasGenerator
                || block is IMyBeacon || block is IMyOreDetector || block is IMyLightingBlock || GridMetrics.IsHydrogenEngine(block))
            {
                docked = SystemAction.Off;
                flight = SystemAction.On;
                return true;
            }
            return false;
        }

        SystemAction ParseAction(IMyFunctionalBlock block, string key, string value)
        {
            bool battery = block is IMyBatteryBlock;
            bool tank = block is IMyGasTank;
            switch (value.ToLowerInvariant())
            {
                case "ignore": return SystemAction.Ignore;
                case "off": return SystemAction.Off;
                case "on": return battery || tank ? SystemAction.Auto : SystemAction.On;
                case "auto": if (battery || tank) return SystemAction.Auto; break;
                case "recharge": if (battery) return SystemAction.Recharge; break;
                case "discharge": if (battery) return SystemAction.Discharge; break;
                case "stockpile": if (tank) return SystemAction.Stockpile; break;
            }
            scanWarnings.Add(block.CustomName + ": " + key + "=" + value + " is not valid here; ignored.");
            return SystemAction.Ignore;
        }

        // A docked ship with every battery recharging and every reactor off is dead if
        // the host cannot supply power, and a dead grid cannot run this script to fix it.
        bool HostHasPower()
        {
            hostPowerBuffer.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyPowerProducer>(hostPowerBuffer,
                producer => !producer.IsSameConstructAs(Me) && producer.IsWorking && producer.MaxOutput > 0);
            return hostPowerBuffer.Count > 0;
        }

        void ApplyState(bool docked)
        {
            bool hostPower = !docked || HostHasPower();
            hostPowerWarning = hostPower ? null : "Dock has no working power. Batteries left on Auto.";
            foreach (ManagedBlock item in managed)
            {
                SystemAction action = docked ? item.Docked : item.Flight;
                if (action == SystemAction.Recharge && !hostPower) action = SystemAction.Auto;
                IMyFunctionalBlock block = item.Block;
                switch (action)
                {
                    case SystemAction.Ignore:
                        break;
                    case SystemAction.Off:
                        block.Enabled = false;
                        break;
                    case SystemAction.On:
                        block.Enabled = true;
                        break;
                    case SystemAction.Recharge:
                    case SystemAction.Auto:
                    case SystemAction.Discharge:
                    case SystemAction.Stockpile:
                        block.Enabled = true;
                        IMyBatteryBlock battery = block as IMyBatteryBlock;
                        if (battery != null)
                            battery.ChargeMode = action == SystemAction.Recharge ? ChargeMode.Recharge
                                : action == SystemAction.Discharge ? ChargeMode.Discharge : ChargeMode.Auto;
                        IMyGasTank tank = block as IMyGasTank;
                        if (tank != null) tank.Stockpile = action == SystemAction.Stockpile;
                        break;
                }
            }
        }

        bool PortConnected()
        {
            foreach (IMyShipConnector port in ports)
                if (port.Status == MyShipConnectorStatus.Connected) return true;
            return false;
        }

        void ReleasePorts()
        {
            foreach (IMyShipConnector port in ports)
                if (port.Status == MyShipConnectorStatus.Connected) port.Disconnect();
        }

        // Both measure only the blocks this script manages, which is the point: an
        // ignored tank is not fuel this script will burn. The arithmetic itself is
        // shared with FleetTelemetry so the two never drift. -1 means none fitted.
        double HydrogenPercent()
        {
            metrics.Reset();
            for (int i = 0; i < hydrogenTanks.Count; i++) metrics.Add(hydrogenTanks[i]);
            return metrics.Hydrogen;
        }

        double BatteryPercent()
        {
            metrics.Reset();
            for (int i = 0; i < batteries.Count; i++) metrics.Add(batteries[i]);
            return metrics.Power;
        }

        static int Working<T>(List<T> blocks) where T : class, IMyTerminalBlock
        {
            return GridMetrics.Working(blocks);
        }

        bool RunChecks()
        {
            blockers.Clear();
            double hydrogen = HydrogenPercent();
            if (hydrogen >= 0 && hydrogen < minHydrogen)
                blockers.Add("Hydrogen " + Math.Floor(hydrogen) + "% (need " + minHydrogen + "%)");
            double battery = BatteryPercent();
            if (battery >= 0 && battery < minBattery)
                blockers.Add("Battery " + Math.Floor(battery) + "% (need " + minBattery + "%)");
            if (thrusters.Count > 0 && Working(thrusters) == 0) blockers.Add("No working thrusters");
            if (gyros.Count > 0 && Working(gyros) == 0) blockers.Add("No working gyroscopes");
            return blockers.Count == 0;
        }
    }
}
