using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program
    {
        // How full a grid is. Blocks are counted in one at a time so each script keeps
        // its own idea of which blocks count: FleetTelemetry measures the whole grid,
        // LaunchControl only the blocks it manages. Percentages are 0-100, or -1 where
        // the grid has none of that equipment.
        public sealed class GridMetrics
        {
            double powerStored, powerCapacity;
            double hydrogenStored, hydrogenCapacity;
            double oxygenStored, oxygenCapacity;
            double cargoUsed, cargoCapacity;

            public int Blocks { get; private set; }
            public int Functional { get; private set; }
            public int Batteries { get; private set; }
            public int HydrogenTanks { get; private set; }
            public int OxygenTanks { get; private set; }
            public int Antennas { get; private set; }
            public int WorkingAntennas { get; private set; }
            public int Crew { get; private set; }

            // Functional terminal blocks as a share of all terminal blocks.
            public double Health { get { return Blocks == 0 ? -1 : Functional * 100.0 / Blocks; } }
            public double Power { get { return Share(Batteries, powerStored, powerCapacity); } }
            public double Hydrogen { get { return Share(HydrogenTanks, hydrogenStored, hydrogenCapacity); } }
            public double Oxygen { get { return Share(OxygenTanks, oxygenStored, oxygenCapacity); } }
            public double Cargo { get { return cargoCapacity <= 0 ? -1 : cargoUsed * 100 / cargoCapacity; } }

            public void Reset()
            {
                powerStored = powerCapacity = 0;
                hydrogenStored = hydrogenCapacity = 0;
                oxygenStored = oxygenCapacity = 0;
                cargoUsed = cargoCapacity = 0;
                Blocks = Functional = 0;
                Batteries = HydrogenTanks = OxygenTanks = 0;
                Antennas = WorkingAntennas = 0;
                Crew = 0;
            }

            // Counts one block towards every measure it contributes to. Closed blocks are
            // skipped, so a caller may hold its lists between scans.
            public void Add(IMyTerminalBlock block)
            {
                if (block == null || block.Closed) return;
                Blocks++;
                if (block.IsFunctional) Functional++;

                if (block is IMyRadioAntenna || block is IMyLaserAntenna)
                {
                    Antennas++;
                    if (block.IsWorking) WorkingAntennas++;
                }

                var controller = block as IMyShipController;
                if (controller != null && controller.IsUnderControl) Crew++;

                var battery = block as IMyBatteryBlock;
                if (battery != null)
                {
                    Batteries++;
                    powerStored += battery.CurrentStoredPower;
                    powerCapacity += battery.MaxStoredPower;
                }

                var tank = block as IMyGasTank;
                if (tank != null)
                {
                    // Tanks carry bottles in their inventory; they report as gas, not cargo.
                    if (IsHydrogenTank(tank))
                    {
                        HydrogenTanks++;
                        hydrogenStored += tank.Capacity * tank.FilledRatio;
                        hydrogenCapacity += tank.Capacity;
                    }
                    else
                    {
                        OxygenTanks++;
                        oxygenStored += tank.Capacity * tank.FilledRatio;
                        oxygenCapacity += tank.Capacity;
                    }
                    return;
                }

                for (int i = 0; i < block.InventoryCount; i++)
                {
                    IMyInventory inventory = block.GetInventory(i);
                    if (inventory == null) continue;
                    cargoUsed += (double)inventory.CurrentVolume;
                    cargoCapacity += (double)inventory.MaxVolume;
                }
            }

            public static bool IsHydrogenTank(IMyTerminalBlock block)
            {
                return block.BlockDefinition.SubtypeId.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public static bool IsHydrogenEngine(IMyTerminalBlock block)
            {
                return block.BlockDefinition.TypeIdString.EndsWith("HydrogenEngine");
            }

            public static int Working<T>(List<T> blocks) where T : class, IMyTerminalBlock
            {
                int count = 0;
                for (int i = 0; i < blocks.Count; i++)
                    if (blocks[i].IsFunctional) count++;
                return count;
            }

            static double Share(int count, double stored, double capacity)
            {
                return count == 0 || capacity <= 0 ? -1 : stored * 100 / capacity;
            }
        }
    }
}
