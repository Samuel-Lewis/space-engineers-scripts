using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Linq;

namespace IngameScript
{
    partial class Program
    {
        public class DisplayLog : Display
        {
            LinkedList<string> lines = new LinkedList<string>();
            int max_lines = 25;

            public DisplayLog(Program prog, string script_name = "", string display_tag = "LCDLog")
            {
                program = prog;

                lines = new LinkedList<string>();
                display_tag = GetDisplayTag(script_name, display_tag);
                surfaces = GetSurfaces(program, display_tag);
            }


            public void Echo(string msg)
            {
                program.Echo(msg);

                lines.AddFirst(msg);
                if (lines.Count > max_lines)
                {
                    lines.RemoveLast();
                }

                WriteToSurfaces();
            }

            public void EchoError(string msg)
            {
                Echo($"ERR: {msg}");
            }

            public void Clear(string next_arg)
            {
                lines.Clear();
                WriteToSurfaces();
            }

            void WriteToSurfaces()
            {
                Dictionary<int, string> cache = new Dictionary<int, string>();
                foreach (IMyTextPanel surface in surfaces)
                {
                    int max_lines = (int)(surface.SurfaceSize.Y / (surface.FontSize * 30));
                    string trimmed_text = cache.ContainsKey(max_lines) ? cache[max_lines] : null;
                    if (trimmed_text == null)
                    {
                        List<string> top_lines = lines.Take(max_lines).ToList();
                        top_lines.Reverse();
                        trimmed_text = string.Join("\n", top_lines); ;
                        cache[max_lines] = trimmed_text;
                    }


                    surface.WriteText(trimmed_text, false);
                }
            }
        }
    }
}
