using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program
    {
        public static class IniHandler
        {

            public static void Set<T>(IMyFunctionalBlock block, string section, string key, T value, string comment = null)
            {
                MyIni ini = new MyIni();
                if (!ini.TryParse(block.CustomData))
                {
                    return;
                }

                if (!ini.ContainsSection(section))
                {
                    ini.AddSection(section);
                }

                string typeName = typeof(T).Name;
                switch (typeName)
                {
                    case "String":
                        ini.Set(section, key, value as string);
                        break;
                    case "Int32":
                        ini.Set(section, key, (int)(object)value);
                        break;
                    case "Boolean":
                        ini.Set(section, key, (bool)(object)value);
                        break;
                    default:
                        throw new NotSupportedException($"Type {typeName} is not supported.");
                }


                if (!string.IsNullOrEmpty(comment))
                {
                    ini.SetComment(section, key, comment);
                }
                block.CustomData = ini.ToString();
            }

            public static void CreateDefault<T>(IMyFunctionalBlock block, string section, string key, T default_value, string comment = null)
            {
                MyIni ini = new MyIni();
                if (!ini.TryParse(block.CustomData))
                {
                    return;
                }

                if (ini.ContainsKey(section, key))
                {
                    return;
                }
                Set(block, section, key, default_value, comment);
            }

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

            public static bool GetBool(IMyFunctionalBlock block, string section, string key, bool default_value = false)
            {
                MyIni ini = new MyIni();
                if (ini.TryParse(block.CustomData))
                {
                    return ini.Get(section, key).ToBoolean(default_value);
                }
                return default_value;
            }
        }
    }
}
