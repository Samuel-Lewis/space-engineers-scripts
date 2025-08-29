using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program
    {
        public static class IniHandler
        {
            public static string GetString(IMyFunctionalBlock block, string section, string key, string default_value = null)
            {
                MyIni ini = new MyIni();
                if (ini.TryParse(block.CustomData))
                {
                    return ini.Get(section, key).ToString(default_value);
                }
                return default_value;
            }

            public static List<string> GetStringList(IMyFunctionalBlock block, string section, string key, List<string> default_value = null)
            {
                string line = GetString(block, section, key);

                if (default_value == null)
                {
                    default_value = new List<string>();
                }

                if (string.IsNullOrWhiteSpace(line) || line.Length == 0)
                {
                    return default_value;
                }

                List<string> parts = line.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                return parts;
            }
        }
    }
}
