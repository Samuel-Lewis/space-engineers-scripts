// ...existing code...
namespace IngameScript
{
    partial class Program
    {
        public static class IniHandler
        {
            private static bool TryParseIni(IMyFunctionalBlock block, out MyIni ini)
            {
                ini = new MyIni();
                return ini.TryParse(block.CustomData);
            }

            // Generic Set method for different types
            public static void Set<T>(IMyFunctionalBlock block, string section, string key, T value, string comment = null)
            {
                MyIni ini = new MyIni();
                ini.TryParse(block.CustomData);
                if (!ini.ContainsSection(section))
                {
                    ini.AddSection(section);
                }

                // Use correct Set overload
                switch (value)
                {
                    case bool b:
                        ini.Set(section, key, b);
                        break;
                    case int i:
                        ini.Set(section, key, i);
                        break;
                    case float f:
                        ini.Set(section, key, f);
                        break;
                    case double d:
                        ini.Set(section, key, d);
                        break;
                    case string s:
                        ini.Set(section, key, s);
                        break;
                    default:
                        ini.Set(section, key, value?.ToString());
                        break;
                }

                if (!string.IsNullOrEmpty(comment))
                {
                    ini.SetComment(section, key, comment);
                }
                block.CustomData = ini.ToString();
            }

            // Only write default if key does not exist
            public static void PrepareDefault<T>(IMyFunctionalBlock block, string section, string key, T default_value, string comment = null)
            {
                if (TryParseIni(block, out MyIni ini))
                {
                    if (!ini.ContainsKey(section, key))
                    {
                        Set(block, section, key, default_value, comment);
                    }
                }
                else
                {
                    // If parsing fails, the custom data is likely empty or invalid.
                    // We can treat it as if the key doesn't exist and set the default.
                    Set(block, section, key, default_value, comment);
                }
            }

            public static string GetString(IMyFunctionalBlock block, string section, string key, string default_value = null)
            {
                if (TryParseIni(block, out MyIni ini))
                {
                    return ini.Get(section, key).ToString(default_value);
                }
                return default_value;
            }

            public static List<string> GetStringList(IMyFunctionalBlock block, string section, string key, List<string> default_value = null)
            {
                if (TryParseIni(block, out MyIni ini))
                {
                    string line = ini.Get(section, key).ToString();
                    if (string.IsNullOrWhiteSpace(line) || line.Length == 0)
                    {
                        return default_value;
                    }
                    List<string> parts = line.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    return parts;
                }
                return default_value;
            }

            public static bool GetBool(IMyFunctionalBlock block, string section, string key, bool default_value = false)
            {
                if (TryParseIni(block, out MyIni ini))
                {
                    return ini.Get(section, key).ToBoolean(default_value);
                }
                return default_value;
            }
        }
    }
}
