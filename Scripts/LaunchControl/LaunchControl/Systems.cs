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
        readonly MyIni blockIni = new MyIni();
        string portProblem;
        string hostPowerWarning;

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
            int invalidCustomData = 0;

            scanBuffer.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(scanBuffer, block => block.IsSameConstructAs(Me));

            foreach (IMyTerminalBlock block in scanBuffer)
            {
                bool tagged = block.CustomName.IndexOf(NameTag, StringComparison.OrdinalIgnoreCase) >= 0;
                blockIni.Clear();
                string customData = block.CustomData;
                if (!string.IsNullOrWhiteSpace(customData) && !blockIni.TryParse(customData))
                {
                    if (customData.IndexOf(NameTag, StringComparison.OrdinalIgnoreCase) >= 0) invalidCustomData++;
                    blockIni.Clear();
                }

                IMyShipConnector connector = block as IMyShipConnector;
                if (connector != null)
                {
                    allConnectors.Add(connector);
                    if (tagged || IniBool("dock_port")) ports.Add(connector);
                    continue;
                }

                IMyLightingBlock light = block as IMyLightingBlock;
                if (light != null && (tagged || IniBool("status_light")))
                {
                    statusLights.Add(light);
                    continue;
                }

                int surfaceIndex = IniInt("use_display", tagged ? 0 : -1);
                if (surfaceIndex >= 0)
                {
                    IMyTextSurfaceProvider provider = block as IMyTextSurfaceProvider;
                    IMyTextSurface surface = block as IMyTextSurface;
                    if (provider != null && provider.SurfaceCount > 0)
                        displaySurfaces.Add(provider.GetSurface(Math.Min(surfaceIndex, provider.SurfaceCount - 1)));
                    else if (surface != null)
                        displaySurfaces.Add(surface);
                }

                if (IniBool("ignore")) continue;
                IMyFunctionalBlock functional = block as IMyFunctionalBlock;
                if (functional == null || NeverManaged(block)) continue;

                if (block is IMyBatteryBlock) batteries.Add((IMyBatteryBlock)block);
                else if (block is IMyGasTank && IsHydrogenTank(block)) hydrogenTanks.Add((IMyGasTank)block);
                else if (block is IMyThrust) thrusters.Add((IMyThrust)block);
                else if (block is IMyGyro) gyros.Add((IMyGyro)block);

                SystemAction docked;
                SystemAction flight;
                bool hasRule = DefaultRule(functional, out docked, out flight);
                string dockedValue = IniString("docked");
                string flightValue = IniString("flight");
                if (dockedValue != null) { docked = ParseAction(functional, "docked", dockedValue); hasRule = true; }
                if (flightValue != null) { flight = ParseAction(functional, "flight", flightValue); hasRule = true; }
                if (!hasRule) continue;

                managed.Add(new ManagedBlock { Block = functional, Docked = docked, Flight = flight });
            }

            if (invalidCustomData > 0)
                scanWarnings.Add(invalidCustomData + " block(s) have [launchcontrol] in unreadable Custom Data; ignored.");

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

        bool IniBool(string key)
        {
            bool value;
            return blockIni.ContainsKey(Section, key) && blockIni.Get(Section, key).TryGetBoolean(out value) && value;
        }

        int IniInt(string key, int fallback)
        {
            int value;
            if (blockIni.ContainsKey(Section, key) && blockIni.Get(Section, key).TryGetInt32(out value)) return value;
            return fallback;
        }

        string IniString(string key)
        {
            if (!blockIni.ContainsKey(Section, key)) return null;
            string value = blockIni.Get(Section, key).ToString().Trim();
            return value.Length == 0 ? null : value;
        }

        // The pilot, the script, and anything that sequences other blocks are never touched.
        static bool NeverManaged(IMyTerminalBlock block)
        {
            if (block is IMyShipController || block is IMyProgrammableBlock || block is IMyTimerBlock) return true;
            string type = block.BlockDefinition.TypeIdString;
            return type.EndsWith("EventControllerBlock") || type.EndsWith("ButtonPanel");
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
            if (block is IMyThrust || block is IMyGyro || block is IMyReactor || block is IMyGasGenerator
                || block is IMyRadioAntenna || block is IMyLaserAntenna || block is IMyBeacon
                || block is IMyOreDetector || block is IMyLightingBlock || IsHydrogenEngine(block))
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
