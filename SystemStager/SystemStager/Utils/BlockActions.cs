using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace IngameScript
{
    partial class Program
    {
        public static class Actions
        // TODO: Need to check the flip flop status of all actions. What is "true" for one block may be "false" for another.
        {
            public static void DefaultAction(IMyFunctionalBlock block, bool new_state)
            {
                block.Enabled = new_state;
            }

            public static void BatteryBlock(IMyFunctionalBlock block, bool new_state)
            {
                //DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED

                IMyBatteryBlock battery = block as IMyBatteryBlock;
                if (battery == null) return;

                if (new_state)
                {
                    battery.ChargeMode = ChargeMode.Auto;
                }
                else
                {
                    battery.ChargeMode = ChargeMode.Recharge;
                }

            }

            public static void Cockpit(IMyFunctionalBlock block, bool new_state)
            {
                IMyCockpit cockpit = block as IMyCockpit;
                if (cockpit == null) return;

                cockpit.DampenersOverride = new_state;
            }

            public static void Door(IMyFunctionalBlock block, bool new_state)
            {
                IMyDoor door = block as IMyDoor;
                if (door == null) return;
                if (new_state)
                {
                    door.OpenDoor();
                }
                else
                {
                    door.CloseDoor();
                }
            }

            public static void Light(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void LandingGear(IMyFunctionalBlock block, bool new_state)
            {
                IMyLandingGear gear = block as IMyLandingGear;
                if (gear == null) return;
                if (new_state)
                {
                    gear.Lock();
                }
                else
                {
                    gear.Unlock();
                }
            }

            public static void Motor(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void Tank(IMyFunctionalBlock block, bool new_state)
            {
                IMyGasTank tank = block as IMyGasTank;
                if (tank == null) return;
                tank.Stockpile = new_state;
            }

            public static void ShipConnector(IMyFunctionalBlock block, bool new_state)
            {
                IMyShipConnector shipConnector = block as IMyShipConnector;
                if (shipConnector == null) return;
                if (new_state)
                {
                    shipConnector.Connect();
                }
                else
                {
                    shipConnector.Disconnect();
                }
            }

            public static void SoundBlock(IMyFunctionalBlock block, bool new_state)
            {
                IMySoundBlock soundBlock = block as IMySoundBlock;
                // TODO: Find some "play once" method
                if (soundBlock == null) return;
            }

        }
    }
}
