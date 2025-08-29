using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class SystemStatus
        {
            public string name = "";
            public string code = "";
            public string verb = "";
            public Color color = Color.White;

            public string ShortString(bool use_color = false)
            {
                return colorize(this.code, use_color);
            }

            public string VerbString(bool use_color = false)
            {
                return colorize(this.verb, use_color);
            }

            public string NameString(bool use_color = false)
            {
                return colorize(this.name, use_color);
            }

            string colorize(string text, bool use_color = true)
            {
                if (!use_color) { return text; }
                return $"[Color={ColorToHexString(this.color)}]{text}[Color={ColorToHexString(Color.White)}]";
            }

            public static string ColorToHexString(Color color)
            {
                // TODO: This seems to be the wrong colours
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
            }
        }
    }
}
