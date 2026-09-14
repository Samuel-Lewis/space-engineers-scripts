using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program
    {
        // One snapshot per configuration load. Reads declare settings and add missing
        // defaults. Save commits additions together, preserving existing user values.
        public sealed class IniDocument
        {
            readonly IMyTerminalBlock block;
            readonly Action<string> reportWarning;
            readonly MyIni ini = new MyIni();
            readonly HashSet<MyIniKey> recognised = new HashSet<MyIniKey>();
            string original;
            bool dirty;

            public bool Valid { get; private set; }
            public bool HasWarnings { get; private set; }
            public bool Changed { get { return block.CustomData != original; } }

            public IniDocument(IMyTerminalBlock block, Action<string> warning = null)
            {
                this.block = block;
                reportWarning = warning;
                original = block.CustomData;
                MyIniParseResult result;
                Valid = ini.TryParse(original, out result);
                if (!Valid)
                    Warn("Malformed INI; using defaults without changing Custom Data. " + result);
            }

            public bool Has(string section, string key)
            {
                recognised.Add(new MyIniKey(section, key));
                return Valid && ini.ContainsKey(section, key);
            }

            public string String(string section, string key, string defaultValue = "",
                Func<string, bool> validate = null, string expected = "a valid value")
            {
                bool exists = Has(section, key);
                defaultValue = defaultValue ?? "";
                if (!Valid) return defaultValue;
                if (!exists)
                {
                    ini.Set(section, key, defaultValue);
                    dirty = true;
                    return defaultValue;
                }
                string value = ini.Get(section, key).ToString();
                if (validate == null || validate(value)) return value;
                Fallback(section, key, expected, defaultValue);
                return defaultValue;
            }

            public bool Bool(string section, string key, bool defaultValue = false)
            {
                string value = String(section, key, defaultValue ? "true" : "false");
                bool parsed;
                if (bool.TryParse(value.Trim(), out parsed)) return parsed;
                Fallback(section, key, "true or false", defaultValue ? "true" : "false");
                return defaultValue;
            }

            public double Double(string section, string key, double defaultValue = 0,
                double min = double.MinValue, double max = double.MaxValue)
            {
                string fallback = defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string value = String(section, key, fallback);
                double parsed;
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)
                    && !double.IsNaN(parsed) && !double.IsInfinity(parsed) && parsed >= min && parsed <= max)
                    return parsed;
                Fallback(section, key, "a number from " + min.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " to " + max.ToString(System.Globalization.CultureInfo.InvariantCulture), fallback);
                return defaultValue;
            }

            public int Int(string section, string key, int defaultValue, int min, int max)
            {
                string fallback = defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string value = String(section, key, fallback);
                int parsed;
                if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out parsed)
                    && parsed >= min && parsed <= max) return parsed;
                Fallback(section, key, "an integer from " + min + " to " + max, fallback);
                return defaultValue;
            }

            public void CheckKeys(string section)
            {
                if (!Valid) return;
                var keys = new List<MyIniKey>();
                ini.GetKeys(section, keys);
                foreach (var key in keys)
                    if (!recognised.Contains(key))
                        Warn("[" + section + "] " + key.Name + ": unknown setting; ignored.");
            }

            public void Fallback(string section, string key, string expected, string defaultValue)
            {
                Warn("[" + section + "] " + key + ": expected " + expected
                    + "; using " + (defaultValue.Length == 0 ? "the inherited default" : defaultValue) + ".");
            }

            // Never replace malformed data or edits made since this snapshot was read.
            public bool Save()
            {
                if (!Valid) return false;
                if (Changed)
                {
                    Warn("Custom Data changed during loading; additions deferred until the next reload.");
                    return false;
                }
                if (!dirty) return true;
                original = ini.ToString();
                block.CustomData = original;
                dirty = false;
                return true;
            }

            void Warn(string message)
            {
                HasWarnings = true;
                if (reportWarning != null) reportWarning(block.CustomName + ": " + message);
            }
        }
    }
}
