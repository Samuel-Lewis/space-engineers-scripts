using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class Display
        {
            protected Program program;
            protected List<IMyTextPanel> surfaces = new List<IMyTextPanel>();
            protected string display_tag;

            public string GetDisplayTag(string script_name, string display_tag)
            {
                string tag = display_tag;
                if (!string.IsNullOrWhiteSpace(script_name))
                {
                    tag = script_name + "." + tag;
                }

                return "[" + tag + "]";
            }

            public List<IMyTextPanel> GetSurfaces(Program program, string display_tag)
            {
                List<IMyTextPanel> surfaces = new List<IMyTextPanel>();
                program.GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(surfaces, block =>
                {
                    return block.IsSameConstructAs(program.Me) && block.CustomName.Contains(display_tag);
                });

                // Prepare surfaces
                foreach (var surface in surfaces)
                {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    surface.FontSize = 1.0f;
                    surface.Alignment = TextAlignment.LEFT;
                    surface.TextPadding = 2.0f;
                    surface.BackgroundColor = Color.Black;
                    surface.FontColor = Color.White;
                }

                return surfaces;
            }

        }
    }
}
