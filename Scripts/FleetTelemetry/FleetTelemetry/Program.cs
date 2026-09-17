using Sandbox.ModAPI.Ingame;
using System;
using System.Text;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        #region mdk preserve
        #region mdk macros

// This script was last deployed at $MDK_DATETIME$

        #endregion mdk macros

        const string Section = "fleet";
        const string NameTag = "[fleet]";
        #endregion mdk preserve
        // Rescan blocks and Custom Data this often unless a change is detected sooner.
        const double RescanSeconds = 10;

        readonly Telemetry local = new Telemetry();
        readonly StringBuilder encodeBuffer = new StringBuilder();
        readonly CLI cli;
        double totalSeconds;
        double scanSeconds;

        public Program()
        {
            cli = new CLI(this, "FleetTelemetry");
            cli.Add("status", "Print own telemetry, contacts and warnings", arg => Report());
            cli.Add("config", "Print the active [fleet] settings", arg => DoConfig());
            cli.Add("reload", "Reload configuration and rediscover blocks and screens", arg => Reload());
            cli.Add("clear", "Forget every contact; the roster refills from the next broadcasts", arg => roster.Clear());
            cli.SetDefault("status");
            Reload();
            Collect(local);
            EnsureListener();
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            double delta = Math.Max(0, Runtime.TimeSinceLastRun.TotalSeconds);
            totalSeconds += delta;
            scanSeconds += delta;
            bool command = (updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0;
            if (command || scanSeconds >= RescanSeconds || ConfigurationChanged()) Reload();
            if (command) cli.Run(argument);
            Collect(local);
            Receive();
            Prune();
            Broadcast();
            Render();
            if (!command) Report();
        }

        void Report()
        {
            Echo("FleetTelemetry | " + local.State + " | " + local.Name + " (" + local.Callsign + ")");
            Echo("Health " + PercentText(local.Health) + "  Power " + PercentText(local.Power) + "  H2 " + PercentText(local.Hydrogen)
                + "  O2 " + PercentText(local.Oxygen) + "  Cargo " + PercentText(local.Cargo));
            if (local.Station) Echo("Crew " + local.Crew);
            else Echo("Speed " + SpeedText(local.Speed) + "  Heading " + HeadingText(local.Heading)
                + AltitudeEcho() + "  Crew " + local.Crew);
            Echo("Contacts: " + ordered.Count + " | Received: " + received + (rejected > 0 ? " | Rejected: " + rejected : ""));
            for (int i = 0; i < ordered.Count && i < 8; i++)
            {
                Contact contact = ordered[i];
                Echo((contact.Stale(totalSeconds, config.StaleSeconds) ? "? " : "  ") + contact.Data.Name
                    + " " + contact.Data.State + " " + LinkText(contact));
            }
            if (ordered.Count > 8) Echo("  +" + (ordered.Count - 8) + " more");
            if (antennaWarning != null) Echo("! " + antennaWarning);
            foreach (string warning in configWarnings) Echo("! " + warning);
        }

        string AltitudeEcho()
        {
            if (double.IsNaN(local.Altitude)) return "";
            bool high = local.Altitude >= config.HighAltitude && !double.IsNaN(local.SeaLevel);
            return "  " + (high ? "ASL " + Math.Round(local.SeaLevel) : "AGL " + Math.Round(local.Altitude)) + " m";
        }

        void DoConfig()
        {
            Echo("FleetTelemetry | [" + Section + "]");
            Echo("callsign=" + config.Callsign);
            Echo("channel=" + config.Channel);
            Echo("stale_seconds=" + config.StaleSeconds);
            Echo("drop_seconds=" + config.DropSeconds);
            Echo("health_warning_percent=" + config.HealthWarning);
            Echo("power_warning_percent=" + config.PowerWarning);
            Echo("hydrogen_warning_percent=" + config.HydrogenWarning);
            Echo("oxygen_warning_percent=" + config.OxygenWarning);
            Echo("high_altitude_metres=" + config.HighAltitude);
            Echo("show_title=" + (config.ShowTitle ? "true" : "false"));
            Echo("cockpit=" + config.Cockpit);
            Echo("Cockpit: " + (cockpit == null ? "none" : cockpit.CustomName)
                + " | Dock port: " + (ports.Count == 0 ? "none" : ports.Count == 1 ? ports[0].CustomName : ports.Count + " connectors")
                + " | Screens: " + screens.Count);
            // Blank display_<n> keys are no longer written, so list what is available.
            foreach (string block in surfaceCounts) Echo("Screens on " + block);
            foreach (string warning in configWarnings) Echo("! " + warning);
        }
    }
}
