using System.Diagnostics;

namespace YamaTro.InertialLink.Core
{
    public static class MonotonicClock
    {
        public static long NowNanoseconds
        {
            get
            {
                return (long)(Stopwatch.GetTimestamp() * (1000000000.0 / Stopwatch.Frequency));
            }
        }
    }
}
