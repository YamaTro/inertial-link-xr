using System;

namespace YamaTro.InertialLink.Core
{
    public enum MotionSafetyState
    {
        Waiting,
        WarmingUp,
        Active,
        Degraded,
        FadedOut
    }

    public sealed class SafetyGate
    {
        private readonly long dropoutAfterNanoseconds;
        private readonly long fadeDurationNanoseconds;
        private readonly int warmupPackets;
        private long lastAcceptedNanoseconds;
        private int consecutiveAccepted;

        public SafetyGate(long dropoutAfterNanoseconds, long fadeDurationNanoseconds, int warmupPackets)
        {
            if (dropoutAfterNanoseconds <= 0) throw new ArgumentOutOfRangeException("dropoutAfterNanoseconds");
            if (fadeDurationNanoseconds <= 0) throw new ArgumentOutOfRangeException("fadeDurationNanoseconds");
            this.dropoutAfterNanoseconds = dropoutAfterNanoseconds;
            this.fadeDurationNanoseconds = fadeDurationNanoseconds;
            this.warmupPackets = Math.Max(1, warmupPackets);
            State = MotionSafetyState.Waiting;
        }

        public SafetyGate() : this(250000000L, 250000000L, 3) { }

        public MotionSafetyState State { get; private set; }
        public float Weight { get; private set; }

        public void BeginWarmup()
        {
            if (State == MotionSafetyState.Active || State == MotionSafetyState.Degraded) return;
            State = MotionSafetyState.WarmingUp;
            Weight = 0f;
        }

        public void RecordAccepted(long localNowNanoseconds)
        {
            if (localNowNanoseconds <= 0) return;
            if (lastAcceptedNanoseconds != 0)
            {
                var recoveryBoundary = dropoutAfterNanoseconds > long.MaxValue - fadeDurationNanoseconds
                    ? long.MaxValue : dropoutAfterNanoseconds + fadeDurationNanoseconds;
                if (localNowNanoseconds < lastAcceptedNanoseconds ||
                    localNowNanoseconds - lastAcceptedNanoseconds >= recoveryBoundary)
                    consecutiveAccepted = 0;
            }
            lastAcceptedNanoseconds = localNowNanoseconds;
            consecutiveAccepted++;
            State = consecutiveAccepted >= warmupPackets ? MotionSafetyState.Active : MotionSafetyState.WarmingUp;
            Weight = State == MotionSafetyState.Active ? 1f : 0f;
        }

        public void Tick(long localNowNanoseconds)
        {
            if (lastAcceptedNanoseconds == 0 || localNowNanoseconds <= 0)
            {
                if (State != MotionSafetyState.WarmingUp) State = MotionSafetyState.Waiting;
                Weight = 0f;
                return;
            }

            var elapsed = Math.Max(0, localNowNanoseconds - lastAcceptedNanoseconds);
            if (consecutiveAccepted < warmupPackets)
            {
                // Warm-up samples are never eligible for a degraded/fade weight. In
                // particular, one or two samples followed by silence must stay neutral.
                Weight = 0f;
                if (elapsed >= dropoutAfterNanoseconds + fadeDurationNanoseconds)
                {
                    State = MotionSafetyState.FadedOut;
                    consecutiveAccepted = 0;
                }
                else
                {
                    State = MotionSafetyState.WarmingUp;
                }
                return;
            }

            if (elapsed <= dropoutAfterNanoseconds)
            {
                State = MotionSafetyState.Active;
                Weight = 1f;
                return;
            }

            var fadeElapsed = elapsed - dropoutAfterNanoseconds;
            if (fadeElapsed >= fadeDurationNanoseconds)
            {
                State = MotionSafetyState.FadedOut;
                Weight = 0f;
                consecutiveAccepted = 0;
                return;
            }

            State = MotionSafetyState.Degraded;
            Weight = 1f - (float)((double)fadeElapsed / fadeDurationNanoseconds);
        }

        public void RejectContinuity()
        {
            // During warm-up, accepted packets must truly be consecutive. Once active, a bad packet
            // behaves like a dropout: it does not refresh the timer, and Tick performs a smooth fade.
            if (State != MotionSafetyState.WarmingUp) return;
            consecutiveAccepted = 0;
            Weight = 0f;
        }

        public void Reset()
        {
            lastAcceptedNanoseconds = 0;
            consecutiveAccepted = 0;
            State = MotionSafetyState.Waiting;
            Weight = 0f;
        }
    }
}
