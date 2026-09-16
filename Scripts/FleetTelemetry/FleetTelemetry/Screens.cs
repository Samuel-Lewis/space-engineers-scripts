using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        const string Title = "FleetTelemetry";
        // "North" in space: a fixed world axis, so north-up agrees between grids.
        static readonly Vector3D WorldNorth = -Vector3D.UnitZ;
        static readonly Vector3D WorldUp = Vector3D.UnitY;
        static readonly string[] FleetHeaders = { "Name", "State", "Health", "Power", "H2", "O2", "Cargo", "Speed", "Seen" };

        readonly SurfaceDashboard dashboard = new SurfaceDashboard();
        readonly List<SurfaceDashboard.Gauge> gauges = new List<SurfaceDashboard.Gauge>();
        readonly List<SurfaceDashboard.Readout> readouts = new List<SurfaceDashboard.Readout>();
        readonly List<SurfaceDashboard.Row> rows = new List<SurfaceDashboard.Row>();
        readonly List<SurfaceDashboard.Contact> plotted = new List<SurfaceDashboard.Contact>();
        readonly List<string> issues = new List<string>();
        readonly Color dockedColour = new Color(40, 200, 90);
        readonly Color flightColour = new Color(50, 130, 255);
        readonly Color stationColour = new Color(180, 120, 255);
        readonly Color staleColour = new Color(120, 138, 160);

        void Render()
        {
            foreach (Screen screen in screens)
            {
                switch (screen.Role)
                {
                    case "fleet": DrawFleet(screen); break;
                    case "map": DrawMap(screen); break;
                    case "track": DrawTrack(screen); break;
                    default: DrawOnboard(screen); break;
                }
            }
        }

        void DrawOnboard(Screen screen)
        {
            issues.Clear();
            if (antennaWarning != null) issues.Add(antennaWarning);
            issues.AddRange(configWarnings);
            dashboard.DrawStatus(screen.Surface, Title, local.State, StateColour(local), 0,
                Subtitle(local), Readouts(local), Tape(local), Gauges(local), issues, totalSeconds);
        }

        void DrawTrack(Screen screen)
        {
            if (string.IsNullOrEmpty(screen.TrackTarget))
            {
                dashboard.DrawMessage(screen.Surface, Title + "  |  TRACK", "No target",
                    "Use display_<n>=track <grid name> in this block's Custom Data.");
                return;
            }
            Contact contact = Find(screen.TrackTarget);
            if (contact == null)
            {
                dashboard.DrawMessage(screen.Surface, Title + "  |  TRACK", screen.TrackTarget,
                    "Never heard from. Check the name matches the grid's Info tab exactly and that it is in antenna range.");
                return;
            }
            Telemetry data = contact.Data;
            bool stale = contact.Stale(totalSeconds, config.StaleSeconds);
            issues.Clear();
            if (stale) issues.Add("Stale: last update " + AgeText(contact.Age(totalSeconds)) + " ago.");
            string subtitle = "Seen " + AgeText(contact.Age(totalSeconds)) + " ago  |  " + Subtitle(data);
            dashboard.DrawStatus(screen.Surface, Title + "  |  " + data.Name, data.State + (stale ? " ?" : ""),
                stale ? staleColour : StateColour(data), 0, subtitle, Readouts(data), Tape(data), Gauges(data), issues, totalSeconds);
        }

        static string Subtitle(Telemetry data)
        {
            string where = data.Docked && data.DockedTo.Length > 0 ? "docked to " + data.DockedTo : Gps(data.Position);
            return data.Name + "  |  " + where;
        }

        Color StateColour(Telemetry data)
        {
            return data.Station ? stationColour : data.Docked ? dockedColour : flightColour;
        }

        // Motion and crew as one instrument line; heading also drives the compass tape.
        List<SurfaceDashboard.Readout> Readouts(Telemetry data)
        {
            readouts.Clear();
            readouts.Add(new SurfaceDashboard.Readout("SPD", SpeedText(data.Speed)));
            readouts.Add(new SurfaceDashboard.Readout("HDG", HeadingText(data.Heading)));
            if (!double.IsNaN(data.Altitude))
                readouts.Add(new SurfaceDashboard.Readout("ALT", Math.Round(data.Altitude).ToString("#,0") + " m"));
            readouts.Add(new SurfaceDashboard.Readout("CREW", data.Crew.ToString()));
            return readouts;
        }

        static float Tape(Telemetry data)
        {
            return data.Heading < 0 ? float.NaN : (float)data.Heading;
        }

        List<SurfaceDashboard.Gauge> Gauges(Telemetry data)
        {
            gauges.Clear();
            if (data.Health >= 0)
                gauges.Add(new SurfaceDashboard.Gauge("Health", "MyObjectBuilder_Component/SteelPlate", data.Health / 100, 0.9, Math.Floor(data.Health) + "%"));
            if (data.Power >= 0)
                gauges.Add(new SurfaceDashboard.Gauge("Power", "IconEnergy", data.Power / 100, 0.2, Math.Floor(data.Power) + "%"));
            if (data.Hydrogen >= 0)
                gauges.Add(new SurfaceDashboard.Gauge("Hydrogen", "IconHydrogen", data.Hydrogen / 100, 0.2, Math.Floor(data.Hydrogen) + "%"));
            if (data.Oxygen >= 0)
                gauges.Add(new SurfaceDashboard.Gauge("Oxygen", "IconOxygen", data.Oxygen / 100, 0.2, Math.Floor(data.Oxygen) + "%"));
            if (data.Cargo >= 0)
                gauges.Add(new SurfaceDashboard.Gauge("Cargo", "MyObjectBuilder_Ore/Iron", data.Cargo / 100, -1, Math.Floor(data.Cargo) + "%"));
            return gauges;
        }

        void DrawFleet(Screen screen)
        {
            rows.Clear();
            int stale = 0;
            foreach (Contact contact in ordered)
            {
                Telemetry data = contact.Data;
                bool old = contact.Stale(totalSeconds, config.StaleSeconds);
                if (old) stale++;
                rows.Add(new SurfaceDashboard.Row(new[]
                {
                    data.Name,
                    data.State,
                    PercentText(data.Health),
                    PercentText(data.Power),
                    PercentText(data.Hydrogen),
                    PercentText(data.Oxygen),
                    PercentText(data.Cargo),
                    SpeedText(data.Speed),
                    AgeText(contact.Age(totalSeconds))
                }, old ? staleColour : (Color?)null));
            }
            string subtitle = ordered.Count + " contact" + (ordered.Count == 1 ? "" : "s")
                + (stale > 0 ? "  |  " + stale + " stale" : "") + "  |  " + local.Name;
            dashboard.DrawTable(screen.Surface, Title + "  |  FLEET", subtitle, FleetHeaders, rows, totalSeconds);
        }

        static string PercentText(double value)
        {
            return value < 0 ? "-" : Math.Floor(value) + "%";
        }

        static string SpeedText(double value)
        {
            return value < 0 ? "-" : value.ToString("0.0") + " m/s";
        }

        static string HeadingText(double value)
        {
            return value < 0 ? "-" : ((int)Math.Round(value) % 360).ToString("000") + "°";
        }

        // Plane basis for the map: n is the plane normal (towards the viewer), f is
        // screen-up, r is screen-right.
        void DrawMap(Screen screen)
        {
            Vector3D n, north, east, f;
            bool inGravity = PlaneBasis(out n, out north, out east);
            bool headingUp = screen.HeadingUp && cockpit != null;
            if (headingUp && !inGravity)
            {
                // In space the ship's own deck is the natural plane for a heading-up view.
                n = cockpit.WorldMatrix.Up;
                north = Project(WorldNorth, n);
                if (north.LengthSquared() < 1e-6) north = Project(Vector3D.UnitX, n);
                north = Vector3D.Normalize(north);
            }
            if (headingUp)
            {
                f = Project(cockpit.WorldMatrix.Forward, n);
                // Nose straight up or down: fall back to north so the map stays readable.
                f = f.LengthSquared() < 1e-6 ? north : Vector3D.Normalize(f);
            }
            else f = north;
            Vector3D r = Vector3D.Cross(f, n);

            plotted.Clear();
            foreach (Contact contact in ordered)
            {
                Vector3D d = contact.Data.Position - local.Position;
                bool old = contact.Stale(totalSeconds, config.StaleSeconds);
                plotted.Add(new SurfaceDashboard.Contact(Vector3D.Dot(d, r), Vector3D.Dot(d, f),
                    contact.Data.Callsign.Length > 0 ? contact.Data.Callsign : contact.Data.Name,
                    old ? staleColour : StateColour(contact.Data)));
            }

            // Own heading marker, clockwise from screen-up. NaN draws a dot when there
            // is no cockpit to take a heading from.
            float heading = float.NaN;
            if (cockpit != null)
            {
                Vector3D forward = Project(cockpit.WorldMatrix.Forward, n);
                if (forward.LengthSquared() > 1e-6)
                    heading = (float)Math.Atan2(Vector3D.Dot(forward, r), Vector3D.Dot(forward, f));
            }

            string mode = headingUp ? "Heading-up" : screen.HeadingUp ? "North-up (no cockpit)" : "North-up";
            string subtitle = mode + "  |  " + HeadingText(local.Heading) + "  |  " + SpeedText(local.Speed)
                + "  |  " + ordered.Count + " contact" + (ordered.Count == 1 ? "" : "s");
            dashboard.DrawRadar(screen.Surface, Title + "  |  MAP", subtitle, plotted, heading);
        }

        static string Gps(Vector3D p)
        {
            return "GPS " + Math.Round(p.X) + ", " + Math.Round(p.Y) + ", " + Math.Round(p.Z);
        }
    }
}
