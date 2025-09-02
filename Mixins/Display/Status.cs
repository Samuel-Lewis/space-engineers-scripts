using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;

namespace IngameScript
{
    partial class Program
    {
        public class DisplayStatus : Display
        {
            Dictionary<string, Func<string>> stat_fields = new Dictionary<string, Func<string>>();

            public DisplayStatus(Program prog, string script_name = "", string display_tag = "LCDStatus")
            {
                program = prog;

                display_tag = GetDisplayTag(script_name, display_tag);
                surfaces = GetSurfaces(program, display_tag);
            }

            public void AddField(string label, Func<string> value_provider)
            {
                stat_fields.Add(label, value_provider);
            }

            public void Update()
            {
                List<string> lines = new List<string>();
                foreach (var kv in stat_fields)
                {
                    string value = kv.Value.Invoke().ToUpper();
                    lines.Add($"{kv.Key}: {value}");
                }

                string text = string.Join("\n", lines);
                foreach (IMyTextPanel surface in surfaces)
                {
                    surface.WriteText(text, false);
                }
            }
        }
    }
}
