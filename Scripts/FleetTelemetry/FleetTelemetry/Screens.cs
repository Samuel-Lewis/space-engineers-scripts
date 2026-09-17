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
        static readonly string[] FleetHeaders = { "Name", "State", "Health", "Power", "H2", "O2", "Cargo", "Speed", "Link" };
        // Quarter marks around the map, clockwise from the top of the plot.
        static readonly string[] NorthCompass = { "N", "E", "S", "W" };
        static readonly string[] RelativeCompass = { "FWD", "STBD", "AFT", "PORT" };

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
        readonly Color liveColour = new Color(90, 190, 230);
        readonly Color warningColour = new Color(255, 160, 40);
        readonly Color lostColour = new Color(255, 90, 70);

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
                Subtitle(local), Readouts(local), Tape(local), Gauges(local), issues, totalSeconds, screen.ShowTitle);
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
            string subtitle = "Link " + LinkText(contact) + "  |  " + Subtitle(data);
            dashboard.DrawStatus(screen.Surface, Title + "  |  " + data.Name, data.State + (stale ? " ?" : ""),
                stale ? staleColour : StateColour(data), 0, subtitle, Readouts(data), Tape(data), Gauges(data),
                issues, totalSeconds, screen.ShowTitle);
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
        // A station has no speed, facing or height worth reporting, so it gets neither
        // the motion readouts nor the compass.
        List<SurfaceDashboard.Readout> Readouts(Telemetry data)
        {
            readouts.Clear();
            if (!data.Station)
            {
                readouts.Add(new SurfaceDashboard.Readout("SPD", SpeedText(data.Speed)));
                readouts.Add(new SurfaceDashboard.Readout("HDG", HeadingText(data.Heading)));
                AddAltitude(data);
            }
            readouts.Add(new SurfaceDashboard.Readout("CREW", data.Crew.ToString()));
            return readouts;
        }

        // Height above the ground below, which is what matters while flying. Past
        // high_altitude_metres the ground stops being the useful reference and the
        // figure switches to height above sea level, labelled so it is not mistaken.
        void AddAltitude(Telemetry data)
        {
            if (double.IsNaN(data.Altitude)) return;
            bool high = data.Altitude >= config.HighAltitude && !double.IsNaN(data.SeaLevel);
            double value = high ? data.SeaLevel : data.Altitude;
            readouts.Add(new SurfaceDashboard.Readout(high ? "ASL" : "AGL", Math.Round(value).ToString("#,0") + " m"));
        }

        static float Tape(Telemetry data)
        {
            return data.Station || data.Heading < 0 ? float.NaN : (float)data.Heading;
        }

        List<SurfaceDashboard.Gauge> Gauges(Telemetry data)
        {
            gauges.Clear();
            AddGauge(data.Health, config.HealthWarning, "Health", "MyObjectBuilder_Component/SteelPlate");
            AddGauge(data.Power, config.PowerWarning, "Power", "IconEnergy");
            AddGauge(data.Hydrogen, config.HydrogenWarning, "Hydrogen", "IconHydrogen");
            AddGauge(data.Oxygen, config.OxygenWarning, "Oxygen", "IconOxygen");
            // Cargo has no threshold: a full hold is a result, not a fault.
            AddGauge(data.Cargo, -1, "Cargo", "MyObjectBuilder_Ore/Iron");
            return gauges;
        }

        void AddGauge(double percent, double warningPercent, string label, string icon)
        {
            if (percent < 0) return;
            gauges.Add(new SurfaceDashboard.Gauge(label, icon, percent / 100,
                warningPercent < 0 ? -1 : warningPercent / 100, Math.Floor(percent) + "%"));
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
                var cells = new[]
                {
                    data.Name,
                    data.State,
                    PercentText(data.Health),
                    PercentText(data.Power),
                    PercentText(data.Hydrogen),
                    PercentText(data.Oxygen),
                    PercentText(data.Cargo),
                    SpeedText(data.Speed),
                    LinkText(contact)
                };
                // A stale row greys out as a whole, but its numbers still grade, so a
                // ship that went quiet low on fuel still reads as a ship in trouble.
                var colours = new Color?[]
                {
                    null, null,
                    Severity(data.Health, config.HealthWarning),
                    Severity(data.Power, config.PowerWarning),
                    Severity(data.Hydrogen, config.HydrogenWarning),
                    Severity(data.Oxygen, config.OxygenWarning),
                    null, null,
                    LinkColour(contact)
                };
                rows.Add(new SurfaceDashboard.Row(cells, old ? staleColour : (Color?)null, colours));
            }
            string subtitle = ordered.Count + " contact" + (ordered.Count == 1 ? "" : "s")
                + (stale > 0 ? "  |  " + stale + " stale" : "") + "  |  " + local.Name;
            dashboard.DrawTable(screen.Surface, Title + "  |  FLEET", subtitle, FleetHeaders, rows, totalSeconds, screen.ShowTitle);
        }

        // Null leaves the cell on the row's own colour, so only readings that need
        // attention are picked out.
        Color? Severity(double percent, double warningPercent)
        {
            if (percent < 0) return null;
            int severity = SurfaceDashboard.Severity(percent / 100, warningPercent / 100);
            return severity == 2 ? lostColour : severity == 1 ? warningColour : (Color?)null;
        }

        // How the link stands, rather than a stopwatch that reads 0s almost always.
        string LinkText(Contact contact)
        {
            double age = contact.Age(totalSeconds);
            return age <= config.StaleSeconds ? "LIVE" : AgeText(age);
        }

        // Amber once broadcasts start being missed, red once the contact is close
        // enough to drop_seconds that it is about to fall off the roster.
        Color? LinkColour(Contact contact)
        {
            double age = contact.Age(totalSeconds);
            if (age <= config.StaleSeconds) return liveColour;
            return age >= config.DropSeconds * 0.75 ? lostColour : warningColour;
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
            // The plane bearings are measured on, kept even when the map itself switches
            // to the ship's deck plane below, because a contact's heading was measured
            // against this one and not against our deck.
            Vector3D bearingNorth = north, bearingEast = east;
            bool headingUp = screen.HeadingUp && cockpit != null;
            if (headingUp && !inGravity)
            {
                // In space the ship's own deck is the natural plane for a heading-up view.
                n = cockpit.WorldMatrix.Up;
                north = Project(WorldNorth, n);
                if (north.LengthSquared() < 1e-6) north = Project(Vector3D.UnitX, n);
                north = Vector3D.Normalize(north);
                east = Vector3D.Cross(north, n);
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
                Telemetry data = contact.Data;
                Vector3D d = data.Position - local.Position;
                bool old = contact.Stale(totalSeconds, config.StaleSeconds);
                plotted.Add(new SurfaceDashboard.Contact(Vector3D.Dot(d, r), Vector3D.Dot(d, f),
                    data.Callsign.Length > 0 ? data.Callsign : data.Name,
                    old ? staleColour : StateColour(data),
                    ContactHeading(data, bearingNorth, bearingEast, f, r)));
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
            dashboard.DrawRadar(screen.Surface, Title + "  |  MAP", subtitle, plotted, heading,
                headingUp ? RelativeCompass : NorthCompass, screen.ShowTitle);
        }

        // A contact broadcasts its facing as a bearing on its own horizontal plane, so
        // the arrow is rebuilt from that bearing using this grid's bearing plane, then
        // projected onto whatever plane the map is drawn on. Exact in space, where every
        // grid shares one reference plane, and close enough on a planet for grids near
        // enough to hear each other. A contact with no heading to report, such as a
        // station, plots as a plain dot.
        static float ContactHeading(Telemetry data, Vector3D north, Vector3D east, Vector3D f, Vector3D r)
        {
            if (data.Heading < 0) return float.NaN;
            double radians = data.Heading * Math.PI / 180;
            Vector3D facing = north * Math.Cos(radians) + east * Math.Sin(radians);
            return (float)Math.Atan2(Vector3D.Dot(facing, r), Vector3D.Dot(facing, f));
        }

        static string Gps(Vector3D p)
        {
            return "GPS " + Math.Round(p.X) + ", " + Math.Round(p.Y) + ", " + Math.Round(p.Z);
        }
    }
}
