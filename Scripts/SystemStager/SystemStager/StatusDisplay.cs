using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    public partial class Program
    {
        string IndicatorPhase()
        {
            return configErrors.Count > 0 || actionErrors.Count > 0 || blockers.Count > 0 ? "error" : stage;
        }

        void UpdateLights()
        {
            string phase = IndicatorPhase();
            foreach (var item in blocks)
            {
                var light = item.Block as IMyLightingBlock;
                if (!item.StatusLight || light == null || light.Closed) continue;
                string explicitAction;
                bool overridden = item.Actions.TryGetValue(stage, out explicitAction);
                if (!overridden) overridden = config.Defaults.TryGetValue(stage + ".light", out explicitAction);
                if (overridden
                    && (string.Equals(explicitAction, "ignore", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(explicitAction, "off", StringComparison.OrdinalIgnoreCase))) continue;
                light.Enabled = true;
                light.Color = config.Colours[phase];
                light.BlinkIntervalSeconds = phase == "error" || phase == "takeoff" || phase == "landing" ? 0.5f
                    : phase == "preflight" || phase == "approach" ? 1.5f : 0;
                light.BlinkLength = 50;
                light.BlinkOffset = 0;
            }
        }

        string Subtitle()
        {
            if (paused) return "PAUSED | " + notice;
            if (actionErrors.Count > 0) return "Action failed. Correct the cause, then use retry.";
            if (configErrors.Count > 0) return "Configuration error. Correct Custom Data, then reload.";
            if (stage == "preflight")
            {
                if (blockers.Count > 0) return "Takeoff blocked. Waiting for readiness.";
                if (!config.AutoTakeoff) return "Ready. Run takeoff when required.";
                return "Takeoff in " + Math.Max(0, config.PreflightSeconds - stageSeconds).ToString("0.0") + "s";
            }
            if (stage == "approach")
            {
                if (!config.AutoLanding) return "Approach prepared. Run landing when required.";
                return "Landing in " + Math.Max(0, config.ApproachSeconds - stageSeconds).ToString("0.0") + "s";
            }
            if (stage == "landing") return "Landing command accepted. Waiting for connection.";
            return notice;
        }

        void Render()
        {
            displaySeconds = 0;
            var errors = new List<string>();
            errors.AddRange(configErrors);
            errors.AddRange(actionErrors);
            errors.AddRange(blockers);
            errors.AddRange(configWarnings);
            string subtitle = Subtitle();
            dashboard.Draw("SYSTEM STAGER", stage.ToUpperInvariant(), config.Colours[IndicatorPhase()], subtitle, details, errors, totalSeconds);
            Echo("SYSTEM STAGER | " + stage.ToUpperInvariant());
            Echo(subtitle);
            foreach (string line in details) Echo(line);
            foreach (string error in errors) Echo("! " + error);
            Echo("Displays: " + dashboard.SurfaceCount + " | help for commands");
        }
    }
}
