using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IngameScript
{
    partial class Program
    {
        /* Listens for when the main cockpit is under control */
        public class ConnectorEvent : EventListener<IMyShipConnector>
        {
            public ConnectorEvent(Program prog, Action<List<IMyShipConnector>> scb = null, Action encb = null) : base(prog, scb, encb)
            {
            }

            public override List<IMyShipConnector> Condition()
            {
                return blocks.Where(c => c.IsConnected).ToList();
            }
        }
    }
}
