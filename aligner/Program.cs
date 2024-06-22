using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region mdk macros
        // This script was deployed at $MDK_DATETIME$
        #endregion

        #region mdk preserve
        /**
        * ADVANCED CONFIGURATION
        * Change these values to configure the script
        */

        // Identifier used in Custom Data
        const string CustomDataIdentifier = "V Aligner";
        const string CustomDataTargetAngleField = "targetAngle";

        // Max speed at which rotors move
        // (TODO, this is in radians)
        const float RotorMoveSpeed = 0.25f;


        /**
        * SCRIPT
        * Don't change anything below this line unless you know what you're doing
        */
        #endregion

        const string MetaScriptName = "Veeq's Rotor Aligner";
        const string MetaScriptVersion = "v1.1.0";


        MyCommandLine _commandLine = new MyCommandLine();
        Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);

        List<IMyMotorStator> stators = new List<IMyMotorStator>();

        string mode = "idle";

        public Program()
        {
            _commands["help"] = Help;
            _commands["lock"] = Lock;
            _commands["release"] = Release;
            _commands["align"] = Align;
            _commands["cancel"] = Cancel;
        }


        public void Help()
        {
            Echo("Usage: <command>");
            Echo("  align - Aligns hinges and rotors to target angle");
            Echo("  lock - Locks tagged hinges and rotors");
            Echo("  release - Unlocks tagged hinges and rotors");
            Echo("  cancel - Stops the current alignment operation");
            Echo("  help - Display this help message");
        }

        void PrintMeta()
        {
            Echo(MetaScriptName);
            Echo(MetaScriptVersion);
        }

        public void Main(string argument, UpdateType updateSource)
        {
            PrintMeta();
            Echo($"Mode: {mode}");

            // Check if update from 10
            if (updateSource == UpdateType.Update10 && mode == "align")
            {
                ContinueAlign();
                return;
            }


            if (_commandLine.TryParse(argument))
            {
                Action commandAction;
                string command = _commandLine.Argument(0);
                if (command == null)
                {
                    Echo("No command specified");
                }
                else if (_commands.TryGetValue(_commandLine.Argument(0), out commandAction))
                {
                    commandAction();
                }
                else
                {
                    Echo($"Unknown command '{command}'. Use 'help' for a list of commands.");
                }
            }
        }

        List<IMyMotorStator> GetRotorsOnGrid(string targetGrid)
        {
            List<IMyMotorStator> gridStators = new List<IMyMotorStator>();
            GridTerminalSystem.GetBlocksOfType<IMyMotorStator>(gridStators, stator => MyIni.HasSection(stator.CustomData, CustomDataIdentifier) && stator.CubeGrid.CustomName == targetGrid);

            HashSet<IMyMotorStator> returnList = new HashSet<IMyMotorStator>();


            foreach (var stator in gridStators)
            {
                returnList.Add(stator);
                returnList.UnionWith(GetRotorsOnGrid(stator.TopGrid.CustomName));
            }

            return returnList.ToList();
        }

        void LoadRotors()
        {
            stators = GetRotorsOnGrid(Me.CubeGrid.CustomName);
        }

        void Lock()
        {
            LoadRotors();
            foreach (var stator in stators)
            {
                stator.RotorLock = true;
            }
            Echo("Locked " + stators.Count + " rotors");
        }

        void Release()
        {
            LoadRotors();
            foreach (var stator in stators)
            {
                stator.RotorLock = false;
            }
            Echo("Released " + stators.Count + " rotors");
        }

        void Align()
        {
            LoadRotors();
            mode = "align";
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        void Cancel()
        {
            mode = "idle";
            Runtime.UpdateFrequency = UpdateFrequency.None;

            LoadRotors();
            foreach (var stator in stators)
            {
                stator.TargetVelocityRad = 0;
            }
            Echo("Finished Alignment");
        }


        double GetDesiredAngle(IMyMotorStator rotor)
        {
            MyIni ini = new MyIni();
            MyIniParseResult result;
            if (!ini.TryParse(rotor.CustomData, out result))
            {
                Echo("Failed to parse Custom Data: " + result.ToString());
                return 0;
            }

            double targetAngleDeg = ini.Get(CustomDataIdentifier, CustomDataTargetAngleField).ToInt32();
            double targetAngleRad = targetAngleDeg * Math.PI / 180;
            return targetAngleRad;
        }

        void ContinueAlign()
        {
            bool allAligned = true;
            foreach (var stator in stators)
            {
                bool isAligned = TransitionAngle(stator);
                allAligned = allAligned && isAligned;
            }

            if (allAligned)
            {
                Echo("All Aligned!");
                Cancel();
            }
        }

        bool TransitionAngle(IMyMotorStator rotor)
        {
            if (rotor == null)
            {
                return true;
            }

            double targetAngle = GetDesiredAngle(rotor);
            double tolerance = 0.01f;
            double currentAngle = rotor.Angle;
            double angleDelta = targetAngle - currentAngle;


            if (Math.Abs(angleDelta) < tolerance)
            {
                rotor.TargetVelocityRad = 0;
                return true;
            }

            if (angleDelta > Math.PI)
            {
                angleDelta -= MathHelper.TwoPi;
            }
            else if (angleDelta < -Math.PI)
            {
                angleDelta += MathHelper.TwoPi;
            }

            double angleStep = Math.Sign(angleDelta) * Math.Min(Math.Abs(angleDelta), RotorMoveSpeed);
            rotor.TargetVelocityRad = (float)angleStep;
            return false;
        }

    }
}
