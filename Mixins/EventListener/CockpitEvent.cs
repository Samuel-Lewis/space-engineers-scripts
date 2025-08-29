using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IngameScript
{
    partial class Program
    {
        /* Listens for when the main cockpit is under control */
        public class CockpitEvent : EventListener<IMyCockpit>
        {
            List<IMyCockpit> cockpits = new List<IMyCockpit>();
            public CockpitEvent(Program prog, Action<List<IMyCockpit>> scb, Action encb) : base(prog, scb, encb)
            {
            }

            public override void FindBlocks()
            {
                cockpits.Clear();
                program.GridTerminalSystem.GetBlocksOfType<IMyCockpit>(cockpits, block => block.IsSameConstructAs(program.Me) && block.IsMainCockpit);
            }

            public override List<IMyCockpit> Condition()
            {
                return cockpits.Where(c => c.IsUnderControl).ToList();
            }
        }
    }
}
