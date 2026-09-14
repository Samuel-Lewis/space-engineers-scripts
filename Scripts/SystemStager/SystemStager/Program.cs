using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        const string Section = "stager";
        static readonly string[] Stages = { "docked", "preflight", "takeoff", "flight", "approach", "landing" };
        readonly List<ManagedBlock> blocks = new List<ManagedBlock>();
        readonly List<string> configErrors = new List<string>();
        readonly List<string> configWarnings = new List<string>();
        readonly List<string> actionErrors = new List<string>();
        readonly List<string> blockers = new List<string>();
        readonly List<string> details = new List<string>();
        readonly HashSet<long> completedHooks = new HashSet<long>();
        readonly SurfaceDashboard dashboard = new SurfaceDashboard();
        CLI cli;
        StagerConfig config;
        IniDocument pbConfig;
        string stage = "unknown";
        string notice = "Choose preflight or flight to initialise.";
        double stageSeconds;
        double totalSeconds;
        double scanSeconds;
        double displaySeconds;
        bool paused;
        bool pendingDock;
        bool wasConnected;
        bool landingAccepted;
        string hookPhase = "";

        public Program()
        {
            cli = new CLI(this, "SystemStager", "2.0");
            cli.add("stage", "<name> [-force]: select a stage; force bypasses takeoff readiness only", CommandStage);
            cli.add("preflight", "Prepare the ship and start the takeoff countdown", arg => CommandStage("preflight"));
            cli.add("takeoff", "Release the ship after checking readiness", arg => CommandStage("takeoff"));
            cli.add("flight", "Select flight settings", arg => CommandStage("flight"));
            cli.add("approach", "Prepare the ship and start the landing countdown", arg => CommandStage("approach"));
            cli.add("landing", "Start Landing immediately, skipping the Approach delay", arg => CommandStage("landing"));
            cli.add("docked", "Stand down only while connected", arg => CommandStage("docked"));
            cli.add("pause", "Pause automatic transitions (does not stop another PB)", arg => SetPaused(true));
            cli.add("resume", "Resume automatic transitions", arg => SetPaused(false));
            cli.add("reload", "Reload configuration and discover blocks without replaying actions", arg => notice = "Configuration and blocks reloaded.");
            cli.add("retry", "Reload and retry the current stage, including its hooks", arg => Retry());
            cli.add("status", "Print current stage, readiness and errors", arg => Render());
            cli.set_default("status");
            Reload();
            Restore();
            CaptureConnectionState();
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            RefreshReadiness();
            UpdateLights();
            Render();
        }

        public void Save()
        {
            var saved = new MyIni();
            saved.Set(Section, "stage", stage);
            saved.Set(Section, "paused", paused);
            saved.Set(Section, "landing_accepted", landingAccepted);
            Storage = saved.ToString();
        }

        void Restore()
        {
            var saved = new MyIni();
            if (saved.TryParse(Storage))
            {
                string previous = saved.Get(Section, "stage").ToString("unknown");
                if (Array.IndexOf(Stages, previous) >= 0) stage = previous;
                paused = saved.Get(Section, "paused").ToBoolean();
                landingAccepted = saved.Get(Section, "landing_accepted").ToBoolean();
            }
            if (HasConnection())
            {
                SetDocked(false);
            }
            else if (stage == "docked" || stage == "preflight" || stage == "takeoff" || stage == "approach")
            {
                stage = "unknown";
                paused = false;
                landingAccepted = false;
                notice = "The previous transition was interrupted. Select preflight, flight or approach.";
            }
            else if (stage != "unknown")
            {
                notice = stage == "landing" && !landingAccepted
                    ? "Landing was interrupted before Spug accepted it. Use retry."
                    : "Restored " + stage + "; entry actions were not repeated.";
            }
            Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            double delta = Math.Max(0, Runtime.TimeSinceLastRun.TotalSeconds);
            totalSeconds += delta;
            if (!paused) stageSeconds += delta;
            scanSeconds += delta;
            displaySeconds += delta;
            bool command = (updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0;
            if (scanSeconds >= 5 || command || ConfigurationChanged()) Reload();
            ObserveConnections();
            if (command) cli.run(argument);
            RefreshReadiness();
            Advance();
            UpdateLights();
            if (displaySeconds >= config.DisplaySeconds || !string.IsNullOrWhiteSpace(argument)) Render();
        }

        void CommandStage(string requested)
        {
            requested = (requested ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(Stages, requested) < 0)
            {
                notice = "Use stage docked/preflight/takeoff/flight/approach/landing.";
                return;
            }
            if (requested == "landing")
            {
                if (stage == "landing")
                {
                    if (!landingAccepted) StartLanding();
                    else notice = "Landing already started. Waiting for connection.";
                    return;
                }
                if (stage != "approach" && !Enter("approach")) return;
                StartLanding();
                return;
            }
            if (requested == "takeoff")
            {
                if (stage != "preflight" && !Enter("preflight")) return;
                if (actionErrors.Count > 0)
                {
                    notice = "Preflight actions failed. Correct the cause and use retry before takeoff.";
                    return;
                }
                RefreshReadiness();
                if (!cli.get_switch("force") && blockers.Count > 0)
                {
                    notice = "Takeoff blocked. Correct readiness or use stage takeoff -force.";
                    return;
                }
            }
            Enter(requested);
        }

        void Retry()
        {
            if (stage == "landing")
            {
                if (!landingAccepted) StartLanding();
                else notice = "Landing already started. Waiting for connection.";
                return;
            }
            if (stage == "unknown") { notice = "Select preflight or flight first."; return; }
            Enter(stage);
        }

        void SetPaused(bool value)
        {
            paused = value;
            notice = value ? "Automatic transitions paused." : "Automatic transitions resumed.";
            Save();
        }

        bool Enter(string next)
        {
            if (configErrors.Count > 0) { notice = "Fix configuration errors before changing stage."; return false; }
            if (next == "docked" && !HasConnection())
            {
                notice = "Docked requires a connected docking connector.";
                return false;
            }
            if (next == "docked") return SetDocked(true);
            if ((next == "flight" || next == "approach" || next == "landing") && HasConnection())
            {
                notice = "Still connected. Use preflight and takeoff first.";
                return false;
            }
            stage = next;
            stageSeconds = 0;
            paused = false;
            if (next != "landing") landingAccepted = false;
            blockers.Clear();
            details.Clear();
            displaySeconds = config.DisplaySeconds;
            Save();
            bool success = ApplyStage(next, true);
            notice = success ? "Entered " + stage + "." : stage + " is incomplete. Correct the error and run retry.";
            CaptureConnectionState();
            return success;
        }

        bool SetDocked(bool hooks)
        {
            if (!HasConnection())
            {
                notice = "Docked requires a connected docking connector.";
                return false;
            }
            stage = "docked";
            landingAccepted = false;
            stageSeconds = 0;
            blockers.Clear();
            details.Clear();
            actionErrors.Clear();
            bool success = ApplyStage("docked", hooks);
            pendingDock = configErrors.Count > 0;
            paused = !success;
            notice = success ? "Docked. Systems standing by." : "Docked, but stand-down is incomplete. Correct the error and run retry.";
            displaySeconds = config.DisplaySeconds;
            Save();
            CaptureConnectionState();
            return success;
        }

        void Advance()
        {
            if (paused || configErrors.Count > 0 || actionErrors.Count > 0) return;
            if (stage == "preflight" && config.AutoTakeoff && stageSeconds >= config.PreflightSeconds && blockers.Count == 0)
                Enter("takeoff");
            else if (stage == "takeoff")
            {
                if (!HasConnection() && !HasLockedGear()) Enter("flight");
                else notice = "Waiting for connectors and landing gear to release.";
            }
            else if (stage == "approach" && config.AutoLanding && stageSeconds >= config.ApproachSeconds)
                StartLanding();
        }

        void StartLanding()
        {
            if ((stage != "approach" && stage != "landing") || configErrors.Count > 0) return;
            if (stage == "approach" && actionErrors.Count > 0)
            {
                notice = "Approach is incomplete. Correct the error and run retry before Landing.";
                return;
            }
            if (HasConnection()) { Enter("docked"); return; }
            if (landingAccepted) { notice = "Landing already started. Waiting for connection."; return; }
            stage = "landing";
            stageSeconds = 0;
            paused = false;
            landingAccepted = false;
            displaySeconds = config.DisplaySeconds;
            notice = "Starting landing.";
            Save();
            IMyProgrammableBlock landing = FindLandingProgram();
            if (landing == null) return;
            if (!ApplyStage("landing", true)) return;
            if (HasConnection()) { SetDocked(true); return; }
            if (!landing.IsWorking || !landing.TryRun(config.LandingArgument))
            {
                AddUnique(actionErrors, "Landing PB did not accept the command. Fix it, then use retry.");
                return;
            }
            landingAccepted = true;
            displaySeconds = config.DisplaySeconds;
            notice = "Landing command accepted. Waiting for docking connection.";
            Save();
        }

        IMyProgrammableBlock FindLandingProgram()
        {
            IMyProgrammableBlock found = null;
            foreach (var item in blocks)
            {
                var pb = item.Block as IMyProgrammableBlock;
                if (pb == null || pb.EntityId == Me.EntityId) continue;
                if (!string.Equals(pb.CustomName, config.LandingProgram, StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null)
                {
                    AddUnique(actionErrors, "Multiple PBs named '" + config.LandingProgram + "'. Use a unique name.");
                    return null;
                }
                found = pb;
            }
            if (found == null) AddUnique(actionErrors, "Landing PB not found: " + config.LandingProgram + ". Configure its exact name, then retry.");
            return found;
        }

        bool HasConnection()
        {
            foreach (var item in blocks)
            {
                var connector = item.Block as IMyShipConnector;
                if (item.Docking && connector != null && connector.Status == MyShipConnectorStatus.Connected) return true;
            }
            return false;
        }

        bool HasLockedGear()
        {
            foreach (var item in blocks)
            {
                var gear = item.Block as IMyLandingGear;
                if (item.Managed && gear != null && gear.IsLocked
                    && string.Equals(ActionFor(item, "takeoff"), "unlock", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        void CaptureConnectionState()
        {
            wasConnected = HasConnection();
        }

        void ObserveConnections()
        {
            bool any = HasConnection();
            bool newlyConnected = any && !wasConnected;
            bool lost = !any && wasConnected;
            if (newlyConnected)
            {
                SetDocked(true);
            }
            else if (pendingDock && any && configErrors.Count == 0)
            {
                SetDocked(true);
            }
            else if (lost && stage != "takeoff")
            {
                paused = true;
                landingAccepted = false;
                stageSeconds = 0;
                actionErrors.Clear();
                hookPhase = "";
                completedHooks.Clear();
                notice = "Unexpected disconnect. Automatic transitions paused; select preflight or flight.";
                stage = "unknown";
                Save();
            }
            if (!any) pendingDock = false;
            wasConnected = HasConnection();
        }

        static void AddUnique(List<string> target, string text)
        {
            if (!target.Contains(text)) target.Add(text);
        }
    }
}
