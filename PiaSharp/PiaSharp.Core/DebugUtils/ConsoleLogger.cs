using System;

namespace PiaSharp.Core.DebugUtils
{
    public class ConsoleLogger : ILogger
    {
        public void Break()
        {
            System.Diagnostics.Debugger.Break();
        }

        public void Break(ILogger.BreakOnCondition handler)
        {
            throw new NotImplementedException();
        }

        public void Error(string message)
        {
            Console.WriteLine(message);
        }

        public void Log(string message)
        {
            Console.WriteLine(message);
        }

        public void Warn(string message)
        {
            Console.WriteLine(message);
        }
    }
}
