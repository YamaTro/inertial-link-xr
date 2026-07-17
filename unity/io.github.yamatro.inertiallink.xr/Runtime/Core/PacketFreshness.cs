namespace YamaTro.InertialLink.Core
{
    public enum FreshnessDecision
    {
        Accepted,
        Stale,
        TooFarInFuture
    }

    public static class PacketFreshness
    {
        public const long DefaultMaximumAgeNanoseconds = 250000000L;
        public const long DefaultMaximumFutureNanoseconds = 100000000L;

        public static FreshnessDecision Evaluate(long localNowNanoseconds, long localEventNanoseconds,
            long maximumAgeNanoseconds, long maximumFutureNanoseconds)
        {
            if (localNowNanoseconds <= 0 || localEventNanoseconds <= 0) return FreshnessDecision.Stale;
            if (localNowNanoseconds >= localEventNanoseconds)
            {
                if (localNowNanoseconds - localEventNanoseconds > maximumAgeNanoseconds) return FreshnessDecision.Stale;
            }
            else if (localEventNanoseconds - localNowNanoseconds > maximumFutureNanoseconds)
            {
                return FreshnessDecision.TooFarInFuture;
            }
            return FreshnessDecision.Accepted;
        }

        public static FreshnessDecision Evaluate(long localNowNanoseconds, long localEventNanoseconds)
        {
            return Evaluate(localNowNanoseconds, localEventNanoseconds,
                DefaultMaximumAgeNanoseconds, DefaultMaximumFutureNanoseconds);
        }
    }
}
