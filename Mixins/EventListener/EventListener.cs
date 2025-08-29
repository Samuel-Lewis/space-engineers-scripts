using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;

namespace IngameScript
{
    partial class Program
    {
        public abstract class EventListener<T> where T : class, IMyTerminalBlock
        {
            protected Program program;
            Action<List<T>> start_callback;
            Action end_callback;
            protected List<T> blocks = new List<T>();
            bool active = true;

            int last_count = 0;

            public EventListener(Program prog, Action<List<T>> start_cb = null, Action end_cb = null)
            {
                program = prog;
                start_callback = start_cb;
                end_callback = end_cb;
                FindBlocks();

                last_count = Condition().Count;
            }

            public abstract List<T> Condition();
            public virtual void FindBlocks()
            {
                blocks.Clear();
                program.GridTerminalSystem.GetBlocksOfType<T>(blocks, block => block.IsSameConstructAs(program.Me));
            }

            public bool IsDetected()
            {
                return last_count > 0;
            }

            public void Activate()
            {
                active = true;
            }

            public void Deactivate()
            {
                active = false;
            }

            public void Poll()
            {
                if (!active)
                {
                    return;
                }

                List<T> confirmed = Condition();
                int count = confirmed.Count;

                if (count == last_count)
                {
                    // TODO: In theory, the blocks in the condition could change without the count changing
                    return;
                }

                if (count > 0)
                {
                    last_count = count;
                    start_callback?.Invoke(confirmed);
                }
                else
                {
                    last_count = 0;
                    end_callback?.Invoke();
                }
            }

        }
    }
}
