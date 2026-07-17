using System;

namespace YamaTro.InertialLink.Core
{
    public sealed class TimeSyncEstimator
    {
        private const long MaximumRoundTripNanoseconds = 1000000000L;
        private const long CandidateSlackNanoseconds = 5000000L;
        private double offsetSenderMinusLocal;
        private long bestRoundTrip = long.MaxValue;
        private int acceptedSamples;

        public bool IsSynchronized { get { return acceptedSamples >= 2; } }
        public long BestRoundTripNanoseconds { get { return bestRoundTrip == long.MaxValue ? 0 : bestRoundTrip; } }
        public long EstimatedOffsetNanoseconds { get { return (long)Math.Round(offsetSenderMinusLocal); } }

        public bool AddExchange(long t0LocalSend, long t1SenderReceive, long t2SenderSend, long t3LocalReceive)
        {
            if (t0LocalSend <= 0 || t1SenderReceive <= 0 || t2SenderSend < t1SenderReceive || t3LocalReceive < t0LocalSend)
                return false;

            var localElapsed = t3LocalReceive - t0LocalSend;
            var senderProcessing = t2SenderSend - t1SenderReceive;
            var roundTrip = localElapsed - senderProcessing;
            if (roundTrip < 0 || roundTrip > MaximumRoundTripNanoseconds) return false;

            var candidateOffset = (((double)t1SenderReceive - t0LocalSend) + ((double)t2SenderSend - t3LocalReceive)) * 0.5;
            if (acceptedSamples == 0)
            {
                offsetSenderMinusLocal = candidateOffset;
                bestRoundTrip = roundTrip;
            }
            else if (roundTrip <= bestRoundTrip + CandidateSlackNanoseconds)
            {
                var weight = roundTrip < bestRoundTrip ? 0.5 : 0.15;
                offsetSenderMinusLocal += (candidateOffset - offsetSenderMinusLocal) * weight;
                if (roundTrip < bestRoundTrip) bestRoundTrip = roundTrip;
            }
            else
            {
                return false;
            }

            acceptedSamples++;
            return true;
        }

        public long SenderToLocal(long senderTimeNanoseconds)
        {
            var mapped = (double)senderTimeNanoseconds - EstimatedOffsetNanoseconds;
            if (mapped >= long.MaxValue) return long.MaxValue;
            if (mapped <= long.MinValue) return long.MinValue;
            return (long)mapped;
        }

        public void Reset()
        {
            offsetSenderMinusLocal = 0;
            bestRoundTrip = long.MaxValue;
            acceptedSamples = 0;
        }
    }
}
