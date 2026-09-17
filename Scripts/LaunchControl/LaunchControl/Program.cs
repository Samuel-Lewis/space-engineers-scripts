using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        #region mdk preserve
        #region mdk macros

// This script was last deployed at $MDK_DATETIME$

        #endregion mdk macros
        

        const string Section = "launchcontrol";
        const string NameTag = "[launchcontrol]";
        #endregion mdk preserve
        const double RescanSeconds = 10;
        const double ReleaseTimeoutSeconds = 2;

        // Docked and Flight are the states. Launching, Releasing, and Docking are the
        // transitions in progress. Manual suspends every automatic action.
        enum Phase { Docked, Launching, Releasing, Flight, Docking, Manual }

        CLI cli;
        IniDocument configuration;
        readonly List<string> configWarnings = new List<string>();
        double launchDelay;
        double dockDelay;
        double minHydrogen;
        double minBattery;
        bool autoDock;
        bool autoRecover;
        // The PB's own show_title, for the config echo. Every block with screens has its
        // own; ReadDisplays reads each one where it finds the screens.
        bool showTitle;

        Phase phase = Phase.Flight;
        bool blocked;
        bool wasConnected;
        double countdown;
        double sinceScan = RescanSeconds;
        string lastAction = "Started";
        readonly List<string> blockers = new List<string>();

        public Program()
        {
            cli = new CLI(this, "LaunchControl");
            cli.Add("launch", "Check systems, power up, release the dock port (--force skips checks)", DoLaunch);
            cli.Add("dock", "Shut down into docked systems (--force even if not connected)", DoDock);
            cli.Add("restore", "Re-apply the systems for the current state", DoRestore);
            cli.Add("manual", "Stop managing systems until auto", DoManual);
            cli.Add("auto", "Resume managing systems", DoAuto);
            cli.Add("status", "Print the current state", DoStatus);
            cli.Add("config", "Print the active [launchcontrol] settings", DoConfig);
            cli.SetDefault("launch");

            LoadConfiguration();
            ScanBlocks();
            wasConnected = PortConnected();
            phase = wasConnected ? Phase.Docked : Phase.Flight;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            double dt = Runtime.TimeSinceLastRun.TotalSeconds;
            bool command = (updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0;
            sinceScan += dt;
            bool reload = command || configuration.Changed || BlockConfigurationChanged();
            if (reload) LoadConfiguration();
            if (reload || sinceScan >= RescanSeconds)
            {
                sinceScan = 0;
                ScanBlocks();
            }

            if (command) cli.Run(argument);
            Tick(dt);
            Render(dt);
        }

        void LoadConfiguration()
        {
            configWarnings.Clear();
            configuration = new IniDocument(Me, warning => configWarnings.Add(warning));
            launchDelay = configuration.Double(Section, "launch_delay_seconds", 0.5, 0, 60);
            dockDelay = configuration.Double(Section, "dock_delay_seconds", 0.5, 0, 60);
            minHydrogen = configuration.Double(Section, "min_hydrogen_percent", 40, 0, 100);
            minBattery = configuration.Double(Section, "min_battery_percent", 40, 0, 100);
            autoDock = configuration.Bool(Section, "auto_dock", true);
            autoRecover = configuration.Bool(Section, "auto_recover", true);
            showTitle = configuration.Bool(Section, "show_title", true);
            ReadDisplays(Me, configuration, false);
            configuration.CheckKeys(Section);
            configuration.Save();
        }

        void DoConfig(string argument)
        {
            Echo("LaunchControl | [" + Section + "]");
            Echo("launch_delay_seconds=" + launchDelay);
            Echo("dock_delay_seconds=" + dockDelay);
            Echo("min_hydrogen_percent=" + minHydrogen);
            Echo("min_battery_percent=" + minBattery);
            Echo("auto_dock=" + (autoDock ? "true" : "false"));
            Echo("auto_recover=" + (autoRecover ? "true" : "false"));
            Echo("show_title=" + (showTitle ? "true" : "false"));
            Echo("Dock port: " + PortSummary() + " | Displays: " + screens.Count + " | Status lights: " + statusLights.Count);
            // Blank display_<n> keys are no longer written, so list what is available.
            foreach (string block in surfaceCounts) Echo("Screens on " + block);
            foreach (string warning in configWarnings) Echo("! " + warning);
        }

        // Connector edges and pending countdowns. Nothing here runs in Manual.
        void Tick(double dt)
        {
            bool connected = PortConnected();
            if (phase != Phase.Manual)
            {
                if (connected && !wasConnected) OnConnected();
                else if (!connected && wasConnected) OnDisconnected();

                if (countdown > 0)
                {
                    countdown -= dt;
                    if (countdown <= 0)
                    {
                        countdown = 0;
                        OnCountdownComplete();
                    }
                }
            }
            wasConnected = connected;
        }

        void OnConnected()
        {
            blocked = false;
            blockers.Clear();
            if (phase == Phase.Docked || phase == Phase.Docking) return;
            if (!autoDock)
            {
                phase = Phase.Docked;
                Note("Connected. auto_dock is off; run 'dock' to shut down.");
                return;
            }
            phase = Phase.Docking;
            Note("Connected. Docking...");
            StartCountdown(dockDelay);
        }

        void OnDisconnected()
        {
            countdown = 0;
            blocked = false;
            blockers.Clear();
            switch (phase)
            {
                case Phase.Launching:
                case Phase.Releasing:
                    phase = Phase.Flight;
                    Note("Launched.");
                    break;
                case Phase.Docking:
                    phase = Phase.Flight;
                    Note("Disconnected before docking finished. Docking cancelled.");
                    break;
                case Phase.Docked:
                    phase = Phase.Flight;
                    if (autoRecover)
                    {
                        ApplyState(false);
                        Note("Disconnected without launching. Flight systems restored.");
                    }
                    else Note("Disconnected without launching. auto_recover is off; run 'restore'.");
                    break;
            }
        }

        void OnCountdownComplete()
        {
            switch (phase)
            {
                case Phase.Launching:
                    ReleasePorts();
                    phase = Phase.Releasing;
                    StartCountdown(ReleaseTimeoutSeconds);
                    break;
                case Phase.Releasing:
                    // Still connected after the timeout. Flight systems stay on so the
                    // ship is safe either way; the pilot decides what to do next.
                    phase = Phase.Docked;
                    blocked = true;
                    blockers.Clear();
                    blockers.Add("Dock port did not release.");
                    Note("Launch failed.");
                    break;
                case Phase.Docking:
                    ApplyState(true);
                    phase = Phase.Docked;
                    Note("Docked.");
                    break;
            }
        }

        void StartCountdown(double seconds)
        {
            if (seconds > 0) countdown = seconds;
            else OnCountdownComplete();
        }

        void DoLaunch(string argument)
        {
            bool force = cli.GetSwitch("force");
            if (phase == Phase.Manual) { Note("Manual mode. Run 'auto' first."); return; }
            if (phase == Phase.Launching || phase == Phase.Releasing) { Note("Launch already in progress."); return; }
            if (phase == Phase.Docking) { Note("Docking in progress."); return; }

            blocked = false;
            blockers.Clear();
            if (!PortConnected())
            {
                if (!force) { Note("Not docked. Run 'restore' to re-apply flight systems."); return; }
                ApplyState(false);
                phase = Phase.Flight;
                Note("Flight systems forced on.");
                return;
            }

            if (!force && !RunChecks())
            {
                blocked = true;
                Note("Launch blocked.");
                return;
            }

            ApplyState(false);
            phase = Phase.Launching;
            Note(force ? "Launching (checks skipped)..." : "Launching...");
            StartCountdown(launchDelay);
        }

        void DoDock(string argument)
        {
            bool force = cli.GetSwitch("force");
            if (phase == Phase.Manual) { Note("Manual mode. Run 'auto' first."); return; }
            blocked = false;
            blockers.Clear();
            countdown = 0;
            if (!PortConnected() && !force)
            {
                Note("Dock port is not connected. Use 'dock --force' to shut down anyway.");
                return;
            }
            ApplyState(true);
            phase = Phase.Docked;
            Note(force && !PortConnected() ? "Docked systems forced on." : "Docked.");
        }

        void DoRestore(string argument)
        {
            if (phase == Phase.Manual) { Note("Manual mode. Run 'auto' first."); return; }
            if (phase == Phase.Launching || phase == Phase.Releasing || phase == Phase.Docking)
            {
                Note("Transition in progress.");
                return;
            }
            blocked = false;
            blockers.Clear();
            bool docked = PortConnected();
            ApplyState(docked);
            phase = docked ? Phase.Docked : Phase.Flight;
            Note(docked ? "Docked systems restored." : "Flight systems restored.");
        }

        void DoManual(string argument)
        {
            phase = Phase.Manual;
            countdown = 0;
            blocked = false;
            blockers.Clear();
            Note("Manual mode. Systems are not managed until 'auto'.");
        }

        void DoAuto(string argument)
        {
            if (phase != Phase.Manual) { Note("Already automatic."); return; }
            wasConnected = PortConnected();
            phase = wasConnected ? Phase.Docked : Phase.Flight;
            Note("Automatic mode.");
        }

        void DoStatus(string argument)
        {
            // Rendering already reports everything; the command exists so a toolbar
            // slot can refresh the display without changing anything.
        }

        void Note(string message)
        {
            lastAction = message;
        }
    }
}
