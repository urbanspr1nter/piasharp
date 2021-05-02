using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiaSharp.Core.DebugUtils
{
    public interface ILogger
    {
        public delegate bool BreakOnCondition(Dictionary<string, object> paramList);

        public void Log(string message);
        public void Warn(string message);
        public void Error(string message);

        public void Break();

        public void Break(BreakOnCondition handler);
    }
}
