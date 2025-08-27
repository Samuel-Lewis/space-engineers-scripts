using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    partial class Program
    {
        public static class Actions
        {
            public static void DefaultAction(IMyFunctionalBlock block, bool new_state)
            {
                block.Enabled = new_state;
            }

            public static void BatteryBlock(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void Cockpit(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void Door(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void Light(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void LandingGear(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void Motor(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void Tank(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void ShipConnector(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }

            public static void SoundBlock(IMyFunctionalBlock block, bool new_state)
            {
                DefaultAction(block, new_state); // TODO: NOT IMPLEMENTED
            }
        }
    }
}
