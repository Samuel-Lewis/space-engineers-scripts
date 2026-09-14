using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        const double DrawSeconds = 0.5;

        readonly SurfaceDashboard dashboard = new SurfaceDashboard();
        readonly List<SurfaceDashboard.Gauge> gauges = new List<SurfaceDashboard.Gauge>();
        readonly List<string> issues = new List<string>();
        readonly Color dockedColour = new Color(40, 200, 90);
        readonly Color flightColour = new Color(50, 130, 255);
        readonly Color transitionColour = new Color(255, 200, 0);
        readonly Color blockedColour = new Color(255, 60, 40);
        readonly Color manualColour = new Color(230, 230, 230);
        double sinceDraw = DrawSeconds;
        double elapsed;
        string lastLightKey = "";

        void Render(double dt)
        {
            elapsed += dt;
            sinceDraw += dt;
            bool transitioning = phase == Phase.Launching || phase == Phase.Releasing || phase == Phase.Docking;

            string state = phase.ToString();
            if (transitioning && countdown > 0) state += " " + countdown.ToString("0.0") + "s";
            if (blocked) state = "BLOCKED";

            Color colour = phase == Phase.Manual ? manualColour
                : blocked ? blockedColour
                : transitioning ? transitionColour
                : phase == Phase.Docked ? dockedColour : flightColour;

            float blink = blocked ? 0.5f : transitioning ? 1f : 0f;

            gauges.Clear();
            issues.Clear();
            double hydrogen = HydrogenPercent();
            double battery = BatteryPercent();
            if (hydrogen >= 0)
                gauges.Add(new SurfaceDashboard.Gauge("Hydrogen", "IconHydrogen", hydrogen / 100, minHydrogen / 100,
                    Math.Floor(hydrogen) + "%"));
            if (battery >= 0)
                gauges.Add(new SurfaceDashboard.Gauge("Battery", "IconEnergy", battery / 100, minBattery / 100,
                    Math.Floor(battery) + "%"));
            if (thrusters.Count > 0)
            {
                int working = Working(thrusters);
                gauges.Add(new SurfaceDashboard.Gauge("Thrusters", "", (double)working / thrusters.Count,
                    1.0 / thrusters.Count, working + "/" + thrusters.Count));
            }
            if (gyros.Count > 0)
            {
                int working = Working(gyros);
                gauges.Add(new SurfaceDashboard.Gauge("Gyroscopes", "", (double)working / gyros.Count,
                    1.0 / gyros.Count, working + "/" + gyros.Count));
            }
            gauges.Add(new SurfaceDashboard.Gauge("Managed blocks", "", -1, -1, managed.Count.ToString()));
            issues.AddRange(blockers);
            if (portProblem != null) issues.Add(portProblem);
            if (hostPowerWarning != null && phase == Phase.Docked) issues.Add(hostPowerWarning);
            issues.AddRange(configWarnings);
            issues.AddRange(scanWarnings);

            Echo("LaunchControl " + ScriptVersion);
            Echo("State: " + state);
            Echo("Port: " + PortSummary());
            if (hydrogen >= 0 || battery >= 0)
                Echo((hydrogen >= 0 ? "H2 " + Math.Floor(hydrogen) + "%  " : "")
                    + (battery >= 0 ? "Batt " + Math.Floor(battery) + "%" : ""));
            Echo("Last: " + lastAction);
            for (int i = 0; i < issues.Count && i < 4; i++) Echo("! " + issues[i]);
            if (issues.Count > 4) Echo("! +" + (issues.Count - 4) + " more");

            string lightKey = phase + "|" + blocked;
            if (lightKey != lastLightKey)
            {
                lastLightKey = lightKey;
                SetStatusLights(colour, blink);
            }

            // A blinking lamp needs every tick; a solid one only needs the numbers refreshed.
            if ((blink > 0 || sinceDraw >= DrawSeconds) && dashboard.SurfaceCount > 0)
            {
                sinceDraw = 0;
                dashboard.Draw("LaunchControl", state, colour, blink, PortSummary() + "  |  " + lastAction,
                    gauges, issues, elapsed);
            }
        }

        string PortSummary()
        {
            if (ports.Count == 0) return "none";
            IMyShipConnector port = ports[0];
            string name = ports.Count > 1 ? ports.Count + " ports" : port.CustomName;
            return name + (PortConnected() ? " [connected]" : " [free]");
        }

        void SetStatusLights(Color colour, float blinkInterval)
        {
            foreach (IMyLightingBlock light in statusLights)
            {
                light.Enabled = true;
                light.Color = colour;
                light.BlinkIntervalSeconds = blinkInterval;
                light.BlinkLength = 50f;
                light.BlinkOffset = 0f;
            }
        }
    }
}
