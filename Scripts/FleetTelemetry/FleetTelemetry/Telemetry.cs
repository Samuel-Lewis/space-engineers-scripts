using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        // One grid's telemetry: either this grid's, measured directly from its blocks,
        // or another grid's, decoded from its last broadcast. Percentages are 0-100,
        // or -1 where the grid has none of that equipment or no way to measure it.
        sealed class Telemetry
        {
            public string Name = "";
            public string Callsign = "";
            // A static grid reports STATION and never DOCKED, even with ships connected.
            public bool Station;
            public bool Docked;
            public string DockedTo = "";
            // Functional terminal blocks as a share of all terminal blocks.
            public double Health = -1;
            public double Power = -1;
            public double Hydrogen = -1;
            public double Oxygen = -1;
            public double Cargo = -1;
            public double Speed = -1;
            // Degrees clockwise from north on the local horizontal plane; -1 when unknown.
            public double Heading = -1;
            // Metres above the ground directly below; NaN outside natural gravity.
            public double Altitude = double.NaN;
            // Metres above the planet's sea level; NaN outside natural gravity. Useful
            // high up, where height above ground stops meaning much.
            public double SeaLevel = double.NaN;
            // Ship controllers with someone in them.
            public int Crew;
            public Vector3D Position;

            const char Separator = '|';
            const int Fields = 17;
            // Fully qualified on purpose: the in-game editor compiles the packaged script
            // against a fixed set of usings that does not include System.Globalization,
            // and the packager cannot add one.
            static readonly System.Globalization.CultureInfo Invariant =
                System.Globalization.CultureInfo.InvariantCulture;

            public string State { get { return Station ? "STATION" : Docked ? "DOCKED" : "FLIGHT"; } }

            // name|callsign|S/D/F|health|power|hydrogen|oxygen|cargo|speed|heading|
            // altitude|sealevel|crew|x|y|z|dockedTo
            // Compact on purpose: one broadcast per grid per Update100 cycle.
            public string Encode(StringBuilder buffer)
            {
                buffer.Clear();
                buffer.Append(Clean(Name)).Append(Separator)
                    .Append(Clean(Callsign)).Append(Separator)
                    .Append(Station ? "S" : Docked ? "D" : "F").Append(Separator)
                    .Append(Number(Health)).Append(Separator)
                    .Append(Number(Power)).Append(Separator)
                    .Append(Number(Hydrogen)).Append(Separator)
                    .Append(Number(Oxygen)).Append(Separator)
                    .Append(Number(Cargo)).Append(Separator)
                    .Append(Number(Speed)).Append(Separator)
                    .Append(Number(Heading)).Append(Separator)
                    .Append(Height(Altitude)).Append(Separator)
                    .Append(Height(SeaLevel)).Append(Separator)
                    .Append(Crew).Append(Separator)
                    .Append(Math.Round(Position.X).ToString(Invariant)).Append(Separator)
                    .Append(Math.Round(Position.Y).ToString(Invariant)).Append(Separator)
                    .Append(Math.Round(Position.Z).ToString(Invariant)).Append(Separator)
                    .Append(Clean(DockedTo));
                return buffer.ToString();
            }

            public static bool TryDecode(string data, Telemetry into)
            {
                if (string.IsNullOrEmpty(data)) return false;
                string[] parts = data.Split(Separator);
                if (parts.Length != Fields || parts[0].Length == 0) return false;
                // Seeded rather than left to definite-assignment analysis across the
                // short-circuiting chain below: the in-game compiler is stricter about
                // out parameters reached through || than the one MDK2 builds with.
                double health = 0, power = 0, hydrogen = 0, oxygen = 0, cargo = 0, speed = 0;
                double heading = 0, altitude = double.NaN, sealevel = double.NaN, x = 0, y = 0, z = 0;
                int crew = 0;
                if (!ParseDouble(parts[3], out health) || !ParseDouble(parts[4], out power) || !ParseDouble(parts[5], out hydrogen)
                    || !ParseDouble(parts[6], out oxygen) || !ParseDouble(parts[7], out cargo) || !ParseDouble(parts[8], out speed)
                    || !ParseDouble(parts[9], out heading) || !ParseHeight(parts[10], out altitude) || !ParseHeight(parts[11], out sealevel)
                    || !int.TryParse(parts[12], System.Globalization.NumberStyles.Integer, Invariant, out crew)
                    || !ParseDouble(parts[13], out x) || !ParseDouble(parts[14], out y) || !ParseDouble(parts[15], out z))
                    return false;
                into.Name = parts[0];
                into.Callsign = parts[1];
                into.Station = parts[2] == "S";
                into.Docked = parts[2] == "D";
                into.Health = health;
                into.Power = power;
                into.Hydrogen = hydrogen;
                into.Oxygen = oxygen;
                into.Cargo = cargo;
                into.Speed = speed;
                into.Heading = heading;
                into.Altitude = altitude;
                into.SeaLevel = sealevel;
                into.Crew = crew;
                into.Position = new Vector3D(x, y, z);
                into.DockedTo = parts[16];
                return true;
            }

            static string Clean(string text)
            {
                return (text ?? "").Replace(Separator, '/').Replace('\n', ' ').Replace('\r', ' ');
            }

            static string Number(double value)
            {
                return value < 0 ? "-1" : value.ToString("0.0", Invariant);
            }

            static string Height(double value)
            {
                return double.IsNaN(value) ? "" : Math.Round(value).ToString(Invariant);
            }

            static bool ParseDouble(string text, out double value)
            {
                return double.TryParse(text, System.Globalization.NumberStyles.Float, Invariant, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
            }

            static bool ParseHeight(string text, out double value)
            {
                value = double.NaN;
                return text.Length == 0 || ParseDouble(text, out value);
            }
        }

        readonly GridMetrics metrics = new GridMetrics();
        string antennaWarning;

        // Measures this grid directly. Nothing here reads another script's state.
        void Collect(Telemetry into)
        {
            into.Name = Me.CubeGrid.CustomName ?? "";
            string callsign = config.Callsign.Length > 0 ? config.Callsign
                : into.Name.Length > 4 ? into.Name.Substring(0, 4) : into.Name;
            // A callsign is a code, not a name; upper case keeps map labels even.
            into.Callsign = callsign.ToUpperInvariant();
            into.Position = cockpit != null ? cockpit.GetPosition() : Me.GetPosition();
            into.Station = Me.CubeGrid.IsStatic;
            into.Docked = false;
            into.DockedTo = "";

            metrics.Reset();
            foreach (IMyTerminalBlock block in blocks) metrics.Add(block);
            into.Health = metrics.Health;
            into.Power = metrics.Power;
            into.Hydrogen = metrics.Hydrogen;
            into.Oxygen = metrics.Oxygen;
            into.Cargo = metrics.Cargo;
            into.Crew = metrics.Crew;

            // Docked means the dock port (see DiscoverBlocks) is locked on another grid.
            foreach (IMyShipConnector port in ports)
            {
                if (into.Station || port.Closed || port.Status != MyShipConnectorStatus.Connected) continue;
                into.Docked = true;
                if (into.DockedTo.Length == 0 && port.OtherConnector != null)
                    into.DockedTo = port.OtherConnector.CubeGrid.CustomName ?? "";
            }

            // Motion needs a ship controller; without one these stay unknown. A static
            // grid cannot move and has no meaningful facing, so it reports none of them
            // rather than a row of zeroes.
            into.Speed = -1;
            into.Heading = -1;
            into.Altitude = double.NaN;
            into.SeaLevel = double.NaN;
            if (cockpit != null && !into.Station)
            {
                into.Speed = cockpit.GetShipSpeed();
                Vector3D up, north, east;
                if (PlaneBasis(out up, out north, out east))
                {
                    // Surface is the height above the ground below, the figure the HUD
                    // shows. Sealevel measures to the planet's sea-level sphere, which on
                    // a plateau or in a canyon is hundreds of metres from the ground.
                    double value;
                    if (cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out value)) into.Altitude = value;
                    if (cockpit.TryGetPlanetElevation(MyPlanetElevation.Sealevel, out value)) into.SeaLevel = value;
                }
                Vector3D forward = Project(cockpit.WorldMatrix.Forward, up);
                if (forward.LengthSquared() > 1e-6)
                {
                    double degrees = Math.Atan2(Vector3D.Dot(forward, east), Vector3D.Dot(forward, north)) * 180 / Math.PI;
                    into.Heading = (degrees + 360) % 360;
                }
            }

            // Local only, never broadcast.
            antennaWarning = metrics.Antennas == 0 ? "No antenna on this grid: nothing is sent or received."
                : metrics.WorkingAntennas == 0 ? "No working antenna: nothing is sent or received."
                : null;
        }

        // Horizontal plane for headings and north-up maps: up is against natural
        // gravity, or world +Y in space. North is world -Z projected onto that plane
        // (world +X if -Z is straight up), east is to its right seen from above.
        // Returns true inside natural gravity.
        bool PlaneBasis(out Vector3D up, out Vector3D north, out Vector3D east)
        {
            Vector3D gravity = cockpit != null ? cockpit.GetNaturalGravity() : Vector3D.Zero;
            bool inGravity = gravity.LengthSquared() > 0.01;
            up = inGravity ? Vector3D.Normalize(-gravity) : WorldUp;
            north = Project(WorldNorth, up);
            if (north.LengthSquared() < 1e-6) north = Project(Vector3D.UnitX, up);
            north = Vector3D.Normalize(north);
            east = Vector3D.Cross(north, up);
            return inGravity;
        }

        static Vector3D Project(Vector3D v, Vector3D unitNormal)
        {
            return v - unitNormal * Vector3D.Dot(v, unitNormal);
        }
    }
}
