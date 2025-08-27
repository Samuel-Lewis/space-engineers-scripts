using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;

namespace IngameScript
{
    partial class Program
    {
        public class DataConfig
        {
            string states;
            string typeId;
            string subtypeIdPart = null;
            public Action<IMyFunctionalBlock, bool> action;

            public DataConfig(string tid, string sidp, string data, Action<IMyFunctionalBlock, bool> act)
            {
                states = data;
                typeId = tid.ToLower();
                if (!string.IsNullOrWhiteSpace(sidp))
                {
                    subtypeIdPart = sidp.ToLower();
                }
                action = act;
            }

            public bool Equals(string tid, string sid)
            {
                if (!tid.StartsWith("MyObjectBuilder_"))
                {
                    tid = "MyObjectBuilder_" + tid;
                }

                if (tid.ToLower() != typeId)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(subtypeIdPart))
                {
                    if (!sid.ToLower().Contains(subtypeIdPart))
                    {
                        return false;
                    }
                }
                return true;
            }

            public bool Equals(IMyFunctionalBlock block)
            {
                if (block == null)
                {
                    return false;
                }

                return Equals(block.BlockDefinition.TypeIdString, block.BlockDefinition.SubtypeId);
            }

            public bool HasState(int tag_index)
            {
                char tag = states[tag_index];
                if (char.IsDigit(tag))
                {
                    return true;
                }

                return false;
            }

            public bool? State(int tag_index)
            {
                char tag = states[tag_index];
                if (char.IsDigit(tag))
                {
                    return (tag != '0');
                }
                return null;
            }
        }

        public class DataStore
        {
            public Dictionary<string, List<DataConfig>> configs = new Dictionary<string, List<DataConfig>>();

            public DataStore()
            {
            }

            public DataStore Add(string typeId, string subtypeIdPart, string data, Action<IMyFunctionalBlock, bool> action = null)
            {

                if (!typeId.StartsWith("MyObjectBuilder_"))
                {
                    typeId = "MyObjectBuilder_" + typeId;
                }

                if (!configs.ContainsKey(typeId))
                {
                    configs[typeId] = new List<DataConfig>();
                }

                if (action == null)
                {
                    action = Actions.DefaultAction;
                }

                configs[typeId].Add(new DataConfig(typeId, subtypeIdPart, data, action));

                return this;
            }

            public DataConfig Search(string typeId, string subtypeId)
            {
                if (!typeId.StartsWith("MyObjectBuilder_"))
                {
                    typeId = "MyObjectBuilder_" + typeId;
                }

                if (!configs.ContainsKey(typeId))
                {
                    return null;
                }

                foreach (var config in configs[typeId])
                {
                    if (config.Equals(typeId, subtypeId))
                    {
                        return config;
                    }
                }
                return null;
            }

            public DataConfig Search(IMyFunctionalBlock block)
            {
                if (block == null)
                {
                    return null;
                }
                return Search(block.BlockDefinition.TypeIdString, block.BlockDefinition.SubtypeId);
            }
        }
    }
}
