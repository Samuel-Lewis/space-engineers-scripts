using System;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program
    {
        // Typed block operations. Discovery, configuration and transition policy live elsewhere.
        public static class BlockActions
        {
            public static string Category(IMyTerminalBlock block)
            {
                if (block == null) return "other";
                if (block is IMyBatteryBlock) return "battery";
                if (block is IMyReactor) return "reactor";
                if (block.BlockDefinition.TypeIdString == "MyObjectBuilder_HydrogenEngine") return "engine";
                if (block is IMyThrust) return "thruster";
                if (block is IMyGyro) return "gyro";
                if (block is IMyShipController) return "controller";
                if (block is IMyGasTank) return "tank";
                if (block is IMyShipConnector) return "connector";
                if (block is IMyLandingGear) return "gear";
                if (block is IMyLightingBlock) return "light";
                if (block is IMyOreDetector) return "ore_detector";
                if (block is IMyShipDrill || block is IMyShipWelder || block is IMyShipGrinder) return "tool";
                if (block is IMyUserControllableGun || block is IMyTurretControlBlock) return "weapon";
                if (block is IMyDoor) return "door";
                if (block is IMyTimerBlock) return "timer";
                if (block is IMyProgrammableBlock) return "program";
                return "other";
            }

            public static string DefaultAction(string category, string stage, bool stockpile)
            {
                bool docked = stage == "docked";
                bool flightSystems = stage == "preflight" || stage == "takeoff" || stage == "flight"
                    || stage == "approach" || stage == "landing";
                if (!docked && !flightSystems) return "ignore";

                switch (category)
                {
                    case "battery": return docked ? "recharge" : "auto";
                    case "reactor":
                    case "engine":
                    case "thruster":
                    case "gyro": return docked ? "off" : "on";
                    case "controller": return docked ? "dampeners_off" : "dampeners_on";
                    case "tank": return docked && stockpile ? "stockpile" : "normal";
                    case "connector": return stage == "takeoff" ? "disconnect" : "on";
                    case "gear":
                        if (stage == "takeoff") return "unlock";
                        return stage == "approach" || stage == "landing" ? "autolock" : "ignore";
                    case "light": return docked ? "off" : stage == "preflight" ? "on" : "ignore";
                    case "ore_detector": return docked ? "off" : "ignore";
                    case "tool":
                    case "weapon":
                        return docked || stage == "preflight" || stage == "approach" || stage == "landing"
                            ? "off" : "ignore";
                    default: return "ignore";
                }
            }

            public static bool Validate(IMyTerminalBlock block, string action, out string error)
            {
                error = null;
                if (block == null || block.Closed)
                {
                    error = "Block is missing or has been removed.";
                    return false;
                }

                string operation = Operation(action);
                if (operation == "ignore") return true;
                bool supported = operation == "on" || operation == "off"
                    ? block is IMyFunctionalBlock
                    : Supports(Category(block), action);

                if (!supported)
                    error = "Action '" + action + "' is not supported by " + block.CustomName + " (" + Category(block) + ").";
                return supported;
            }

            // Validates category defaults even when that category is not currently fitted.
            public static bool Supports(string category, string action)
            {
                switch (Operation(action))
                {
                    case "ignore":
                    case "on":
                    case "off": return true;
                    case "auto":
                    case "recharge":
                    case "discharge": return category == "battery";
                    case "normal":
                    case "stockpile": return category == "tank";
                    case "dampeners_on":
                    case "dampeners_off": return category == "controller";
                    case "connect":
                    case "disconnect": return category == "connector";
                    case "lock":
                    case "unlock":
                    case "autolock": return category == "gear";
                    case "open":
                    case "close": return category == "door";
                    case "trigger":
                    case "start":
                    case "stop": return category == "timer";
                    case "run": return category == "program";
                    default: return false;
                }
            }

            public static bool Execute(IMyTerminalBlock block, string action, out string error)
            {
                if (!Validate(block, action, out error)) return false;
                string operation = Operation(action);
                if (operation == "ignore") return true;

                try
                {
                    switch (operation)
                    {
                        case "on":
                        case "off":
                            ((IMyFunctionalBlock)block).Enabled = operation == "on";
                            break;
                        case "auto":
                        case "recharge":
                        case "discharge":
                            var battery = (IMyBatteryBlock)block;
                            battery.Enabled = true;
                            battery.ChargeMode = operation == "auto" ? ChargeMode.Auto
                                : operation == "recharge" ? ChargeMode.Recharge : ChargeMode.Discharge;
                            break;
                        case "normal":
                        case "stockpile":
                            var tank = (IMyGasTank)block;
                            tank.Enabled = true;
                            tank.Stockpile = operation == "stockpile";
                            break;
                        case "dampeners_on":
                        case "dampeners_off":
                            ((IMyShipController)block).DampenersOverride = operation == "dampeners_on";
                            break;
                        case "connect":
                        case "disconnect":
                            var connector = (IMyShipConnector)block;
                            connector.Enabled = true;
                            if (operation == "connect") connector.Connect();
                            else connector.Disconnect();
                            break;
                        case "lock":
                        case "unlock":
                        case "autolock":
                            var gear = (IMyLandingGear)block;
                            gear.Enabled = true;
                            if (operation == "lock") gear.Lock();
                            else if (operation == "autolock") gear.AutoLock = true;
                            else
                            {
                                gear.AutoLock = false;
                                gear.Unlock();
                            }
                            break;
                        case "open":
                        case "close":
                            var door = (IMyDoor)block;
                            door.Enabled = true;
                            if (operation == "open") door.OpenDoor();
                            else door.CloseDoor();
                            break;
                        case "trigger":
                        case "start":
                        case "stop":
                            var timer = (IMyTimerBlock)block;
                            if (operation == "stop") timer.StopCountdown();
                            else
                            {
                                timer.Enabled = true;
                                if (operation == "start") timer.StartCountdown();
                                else timer.Trigger();
                            }
                            break;
                        case "run":
                            // Preserve argument case, spaces and punctuation for the receiving script.
                            if (!((IMyProgrammableBlock)block).TryRun(action.Substring(action.IndexOf(':') + 1)))
                            {
                                error = block.CustomName + " rejected the run command (busy, disabled, unpowered or no runnable script).";
                                return false;
                            }
                            break;
                    }
                }
                catch (Exception exception)
                {
                    error = block.CustomName + ": " + exception.Message;
                    return false;
                }
                return true;
            }

            static bool IsRun(string action)
            {
                return action != null && action.TrimStart().StartsWith("run:", StringComparison.OrdinalIgnoreCase);
            }

            static string Operation(string action)
            {
                if (IsRun(action)) return "run";
                return (action ?? "").Trim().ToLowerInvariant();
            }
        }
    }
}
