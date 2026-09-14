using Sandbox.ModAPI.Ingame;
using System;

namespace IngameScript
{
    public partial class Program
    {
        void RefreshReadiness()
        {
            blockers.Clear();
            details.Clear();
            if (stage != "preflight") return;
            double batteryStored = 0, batteryCapacity = 0;
            double hydrogenStored = 0, hydrogenCapacity = 0;
            double oxygenStored = 0, oxygenCapacity = 0;
            int batteryCount = 0, hydrogenCount = 0, oxygenCount = 0, thrusterCount = 0, gyroCount = 0;
            foreach (var item in blocks)
            {
                if (!item.Managed || !item.Readiness || item.Block.Closed) continue;
                string action = ActionFor(item, "preflight");
                if (string.Equals(action, "ignore", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "off", StringComparison.OrdinalIgnoreCase)) continue;
                var battery = item.Block as IMyBatteryBlock;
                var tank = item.Block as IMyGasTank;
                if (battery != null)
                {
                    batteryCount++;
                    batteryStored += battery.CurrentStoredPower;
                    batteryCapacity += battery.MaxStoredPower;
                }
                if (tank != null)
                {
                    bool hydrogen = tank.BlockDefinition.SubtypeId.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (hydrogen)
                    {
                        hydrogenCount++;
                        hydrogenStored += tank.Capacity * tank.FilledRatio;
                        hydrogenCapacity += tank.Capacity;
                    }
                    else
                    {
                        oxygenCount++;
                        oxygenStored += tank.Capacity * tank.FilledRatio;
                        oxygenCapacity += tank.Capacity;
                    }
                }
                if (item.Category == "thruster") thrusterCount++;
                if (item.Category == "gyro") gyroCount++;
                if (!config.CheckFlightSystems) continue;
                bool flightSystem = item.Category == "battery" || item.Category == "reactor" || item.Category == "engine"
                    || item.Category == "thruster" || item.Category == "gyro" || item.Category == "controller" || item.Category == "tank";
                var functional = item.Block as IMyFunctionalBlock;
                if (!flightSystem || functional == null) continue;
                if (!functional.IsFunctional) AddUnique(blockers, item.Block.CustomName + ": damaged or incomplete.");
                else if (!functional.Enabled) AddUnique(blockers, item.Block.CustomName + ": disabled.");
                if (battery != null && battery.ChargeMode == ChargeMode.Recharge)
                    AddUnique(blockers, item.Block.CustomName + ": still in Recharge (use readiness=false for a reserve).");
                if (tank != null && tank.Stockpile)
                    AddUnique(blockers, item.Block.CustomName + ": still stockpiling (use readiness=false for a reserve).");
            }
            ResourceCheck("Batteries", config.CheckBatteries, batteryCount, batteryStored, batteryCapacity);
            ResourceCheck("Hydrogen", config.CheckHydrogen, hydrogenCount, hydrogenStored, hydrogenCapacity);
            ResourceCheck("Oxygen", config.CheckOxygen, oxygenCount, oxygenStored, oxygenCapacity);
            if (config.CheckFlightSystems)
            {
                if (thrusterCount == 0) AddUnique(blockers, "No managed flight thrusters found.");
                if (gyroCount == 0) AddUnique(blockers, "No managed flight gyros found.");
            }
        }

        void ResourceCheck(string label, bool enabled, int count, double stored, double capacity)
        {
            if (!enabled) return;
            if (count == 0) { details.Add(label + ": not fitted (skipped)"); return; }
            double percent = capacity > 0 ? stored * 100 / capacity : 0;
            bool ready = capacity > 0 && percent + 0.000001 >= config.Threshold;
            details.Add((ready ? "OK  " : "LOW  ") + label + " " + percent.ToString("0.0") + "% / minimum " + config.Threshold.ToString("0.#") + "%");
            if (!ready) AddUnique(blockers, label + " below " + config.Threshold.ToString("0.#") + "% readiness threshold.");
        }
    }
}
