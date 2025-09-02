using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program
    {
        public abstract class Ini<T>
        {
            protected string section;
            protected string key;
            protected string comment;
            protected T default_value;
            protected IMyFunctionalBlock block;
            protected MyIni ini = new MyIni();

            protected Ini(IMyFunctionalBlock block, string section, string key, T default_value, string comment = null)
            {
                this.section = section;
                this.key = key;
                this.block = block;
                this.comment = comment;
                this.default_value = default_value;

                CreateDefault(default_value);
            }

            public void CreateDefault(T default_value)
            {
                ini = new MyIni();
                if (!ini.TryParse(block.CustomData))
                {
                    return;
                }
                if (ini.ContainsKey(section, key))
                {
                    return;
                }
                Set(default_value);
            }

            public void Set(T value)
            {
                ini = new MyIni();
                if (!ini.TryParse(block.CustomData))
                {
                    return;
                }

                if (!ini.ContainsSection(section))
                {
                    ini.AddSection(section);
                }

                SetTyped(value);

                if (!string.IsNullOrEmpty(comment))
                {
                    ini.SetComment(section, key, comment);
                }

                block.CustomData = ini.ToString();
            }

            public T Get()
            {
                ini = new MyIni();
                if (!ini.TryParse(block.CustomData))
                {
                    return default_value;
                }

                if (!ini.ContainsKey(section, key))
                {
                    return default_value;
                }

                return GetTyped();
            }

            protected abstract void SetTyped(T value);
            protected abstract T GetTyped();
        }


        public class IniString : Ini<string>
        {
            public IniString(IMyFunctionalBlock block, string section, string key, string default_value = null, string comment = null)
                : base(block, section, key, default_value, comment)
            {
            }

            public static string Get(IMyFunctionalBlock block, string section, string key, string default_value = null)
            {
                IniString temp = new IniString(block, section, key, default_value);
                return temp.Get();
            }

            protected override void SetTyped(string value)
            {
                ini.Set(section, key, value);
            }

            protected override string GetTyped()
            {
                return ini.Get(section, key).ToString();
            }
        }

        public class IniStringList : Ini<List<string>>
        {
            public IniStringList(IMyFunctionalBlock block, string section, string key, List<string> default_value, string comment = null)
                : base(block, section, key, default_value, comment)
            {
            }

            protected override void SetTyped(List<string> value)
            {
                if (value == null || value.Count == 0)
                {
                    value = new List<string>() { };
                }
                ini.Set(section, key, string.Join(", ", value));
            }

            protected override List<string> GetTyped()
            {
                string data = ini.Get(section, key).ToString();
                if (string.IsNullOrWhiteSpace(data) || data.Length == 0)
                {
                    return new List<string>() { };
                }
                List<string> parts = data.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                return parts;
            }

        }
        public class IniBool : Ini<bool>
        {
            public IniBool(IMyFunctionalBlock block, string section, string key, bool default_value = false, string comment = null)
                : base(block, section, key, default_value, comment)
            {
            }

            protected override void SetTyped(bool value)
            {
                ini.Set(section, key, value);
            }

            protected override bool GetTyped()
            {
                return ini.Get(section, key).ToBoolean();
            }
        }

    }

}
