namespace YamaTro.InertialLink.Core
{
    public static class TimeSyncLease
    {
        public const long DefaultDurationNanoseconds = 15000000000L;

        public static bool IsValid(long localNowNanoseconds, long refreshedAtNanoseconds,
            long durationNanoseconds)
        {
            if (localNowNanoseconds <= 0 || refreshedAtNanoseconds <= 0 || durationNanoseconds <= 0)
                return false;
            if (localNowNanoseconds < refreshedAtNanoseconds) return false;
            return localNowNanoseconds - refreshedAtNanoseconds <= durationNanoseconds;
        }
    }
}
