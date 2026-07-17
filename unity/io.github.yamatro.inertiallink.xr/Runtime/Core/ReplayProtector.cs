using System.Collections.Generic;

namespace YamaTro.InertialLink.Core
{
    public enum SenderPinDecision
    {
        Pinned,
        Accepted,
        NotEstablished,
        EndpointMismatch,
        SessionMismatch
    }

    public sealed class SenderPin<TEndpoint>
    {
        private readonly EqualityComparer<TEndpoint> comparer = EqualityComparer<TEndpoint>.Default;
        private TEndpoint endpoint;

        public bool IsEstablished { get; private set; }
        public ulong SessionId { get; private set; }
        public TEndpoint Endpoint { get { return endpoint; } }

        public SenderPinDecision Check(TEndpoint candidateEndpoint, ulong candidateSession, bool mayEstablish)
        {
            if (!IsEstablished)
            {
                if (!mayEstablish) return SenderPinDecision.NotEstablished;
                endpoint = candidateEndpoint;
                SessionId = candidateSession;
                IsEstablished = true;
                return SenderPinDecision.Pinned;
            }
            if (!comparer.Equals(endpoint, candidateEndpoint)) return SenderPinDecision.EndpointMismatch;
            return SessionId == candidateSession ? SenderPinDecision.Accepted : SenderPinDecision.SessionMismatch;
        }

        public void Reset()
        {
            endpoint = default(TEndpoint);
            SessionId = 0;
            IsEstablished = false;
        }
    }

    public enum ReplayDecision
    {
        Accepted,
        Duplicate,
        TooOld,
        InvalidSession
    }

    public sealed class ReplayProtector
    {
        private sealed class Window
        {
            public uint Highest;
            public ulong Seen;
            public long LastTouched;
        }

        private readonly Dictionary<ulong, Window> windows = new Dictionary<ulong, Window>();
        private readonly int maximumSessions;
        private long touchCounter;

        public ReplayProtector(int maximumSessions)
        {
            this.maximumSessions = maximumSessions < 1 ? 1 : maximumSessions;
        }

        public ReplayProtector() : this(16) { }

        public ReplayDecision TryAccept(ulong sessionId, uint sequence)
        {
            if (sessionId == 0) return ReplayDecision.InvalidSession;
            Window window;
            if (!windows.TryGetValue(sessionId, out window))
            {
                if (windows.Count >= maximumSessions) EvictLeastRecentlyUsed();
                windows[sessionId] = new Window { Highest = sequence, Seen = 1UL, LastTouched = ++touchCounter };
                return ReplayDecision.Accepted;
            }

            window.LastTouched = ++touchCounter;
            if (sequence == window.Highest) return ReplayDecision.Duplicate;

            if (sequence > window.Highest)
            {
                var forward = sequence - window.Highest;
                window.Seen = forward >= 64 ? 1UL : (window.Seen << (int)forward) | 1UL;
                window.Highest = sequence;
                return ReplayDecision.Accepted;
            }

            var behind = window.Highest - sequence;
            if (behind >= 64) return ReplayDecision.TooOld;
            var bit = 1UL << (int)behind;
            if ((window.Seen & bit) != 0) return ReplayDecision.Duplicate;
            window.Seen |= bit;
            return ReplayDecision.Accepted;
        }

        public void Reset()
        {
            windows.Clear();
            touchCounter = 0;
        }

        private void EvictLeastRecentlyUsed()
        {
            ulong oldestId = 0;
            var oldestTouch = long.MaxValue;
            foreach (var pair in windows)
            {
                if (pair.Value.LastTouched >= oldestTouch) continue;
                oldestTouch = pair.Value.LastTouched;
                oldestId = pair.Key;
            }
            windows.Remove(oldestId);
        }
    }
}
