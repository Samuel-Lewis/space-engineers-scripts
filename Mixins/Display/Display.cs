namespace IngameScript
{
    partial class Program
    {

        public class Display
        {
            Program program;

            public Display(Program prog)
            {
                program = prog;
            }

            public void Echo(string msg)
            {
                program.Echo(msg);
            }

            public void EchoError(string msg)
            {
                Echo($"ERR: {msg}");
            }

            public void Clear()
            {
                program.Echo("");
            }

        }


    }
}
