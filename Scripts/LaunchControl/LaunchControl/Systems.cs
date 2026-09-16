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

        readonly List<ManagedBlock> managed = new List<ManagedBlock>();
        readonly List<IMyShipConnector> ports = new List<IMyShipConnector>();
        readonly List<IMyLightingBlock> statusLights = new List<IMyLightingBlock>();
        readonly List<IMyTextSurface> displaySurfaces = new List<IMyTextSurface>();
        readonly List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
        readonly List<IMyGasTank> hydrogenTanks = new List<IMyGasTank>();
        readonly List<IMyThrust> thrusters = new List<IMyThrust>();
        readonly List<IMyGyro> gyros = new List<IMyGyro>();
        readonly List<string> scanWarnings = new List<string>();
        readonly List<IMyTerminalBlock> scanBuffer = new List<IMyTerminalBlock>();
        readonly List<IMyShipConnector> allConnectors = new List<IMyShipConnector>();
        readonly List<IMyPowerProducer> hostPowerBuffer = new List<IMyPowerProducer>();
        readonly List<IMyTextSurface> pbSurfaces = new List<IMyTextSurface>();
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
            displaySurfaces.Clear();
            batteries.Clear();
            hydrogenTanks.Clear();
            thrusters.Clear();
            gyros.Clear();
            scanWarnings.Clear();
            allConnectors.Clear();
            blockConfigs.Clear();
            blockConfigOwners.Clear();
            blockConfigNames.Clear();
            displaySurfaces.AddRange(pbSurfaces);

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
                else if (block is IMyGasTank && IsHydrogenTank(block)) hydrogenTanks.Add((IMyGasTank)block);
                else if (block is IMyThrust) thrusters.Add((IMyThrust)block);
                else if (block is IMyGyro) gyros.Add((IMyGyro)block);

                SystemAction docked;
                SystemAction flight;
                bool hasRule = DefaultRule(functional, out docked, out flight);
                string dockedValue = ini == null ? "" : ini.String(Section, "docked", "").Trim();
                string flightValue = ini == null ? "" : ini.String(Section, "flight", "").Trim();
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

            dashboard.SetSurfaces(displaySurfaces);
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

        // One display_<n> key per surface; "status" shows the dashboard, blank leaves
        // the surface alone. A tagged block shows it on display_0 by default.
        void ReadDisplays(IMyTerminalBlock block, IniDocument ini, bool tagged)
        {
            IMyTextPanel panel = block as IMyTextPanel;
            IMyTextSurfaceProvider provider = block as IMyTextSurfaceProvider;
            int count = panel != null ? 1 : provider != null ? provider.SurfaceCount : 0;
            bool self = block.EntityId == Me.EntityId;
            if (self) pbSurfaces.Clear();
            for (int index = 0; index < count; index++)
            {
                string value = ini.String(Section, "display_" + index, tagged && index == 0 ? "status" : "",
                    text => text.Trim().Length == 0 || string.Equals(text.Trim(), "status", StringComparison.OrdinalIgnoreCase),
                    "status or blank").Trim();
                if (value.Length == 0) continue;
                IMyTextSurface surface = panel != null ? (IMyTextSurface)panel : provider.GetSurface(index);
                (self ? pbSurfaces : displaySurfaces).Add(surface);
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

        static bool IsHydrogenTank(IMyTerminalBlock block)
        {
            return block.BlockDefinition.SubtypeId.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsHydrogenEngine(IMyTerminalBlock block)
        {
            return block.BlockDefinition.TypeIdString.EndsWith("HydrogenEngine");
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
                if (!IsHydrogenTank(block)) return false;
                docked = SystemAction.Stockpile;
                flight = SystemAction.Auto;
                return true;
            }
            // Antennas are left alone so IGC scripts such as FleetTelemetry keep
            // talking while docked.
            if (block is IMyThrust || block is IMyGyro || block is IMyReactor || block is IMyGasGenerator
                || block is IMyBeacon || block is IMyOreDetector || block is IMyLightingBlock || IsHydrogenEngine(block))
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

        // Returns -1 when the grid has none of that system.
        double HydrogenPercent()
        {
            double stored = 0;
            double capacity = 0;
            foreach (IMyGasTank tank in hydrogenTanks)
            {
                stored += tank.FilledRatio * tank.Capacity;
                capacity += tank.Capacity;
            }
            return capacity > 0 ? stored / capacity * 100 : -1;
        }

        double BatteryPercent()
        {
            double stored = 0;
            double capacity = 0;
            foreach (IMyBatteryBlock battery in batteries)
            {
                stored += battery.CurrentStoredPower;
                capacity += battery.MaxStoredPower;
            }
            return capacity > 0 ? stored / capacity * 100 : -1;
        }

        static int Working<T>(List<T> blocks) where T : class, IMyTerminalBlock
        {
            int count = 0;
            foreach (T block in blocks)
                if (block.IsFunctional) count++;
            return count;
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
