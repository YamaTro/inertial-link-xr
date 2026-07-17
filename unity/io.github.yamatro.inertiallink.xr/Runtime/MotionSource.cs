using YamaTro.InertialLink.Core;

namespace YamaTro.InertialLink
{
    public struct MotionSourceFrame
    {
        public readonly ulong SessionId;
        public readonly uint Sequence;
        public readonly long ArrivalTimeNanoseconds;
        public readonly long LocalEventTimeNanoseconds;
        public readonly bool TimestampSynchronized;
        public readonly ImuPayload Imu;

        public MotionSourceFrame(ulong sessionId, uint sequence, long arrivalTimeNanoseconds,
            long localEventTimeNanoseconds, bool timestampSynchronized, ImuPayload imu)
        {
            SessionId = sessionId;
            Sequence = sequence;
            ArrivalTimeNanoseconds = arrivalTimeNanoseconds;
            LocalEventTimeNanoseconds = localEventTimeNanoseconds;
            TimestampSynchronized = timestampSynchronized;
            Imu = imu;
        }
    }

    public interface IMotionSource
    {
        bool IsReady { get; }
        string Status { get; }
        bool TryDequeue(out MotionSourceFrame frame);
    }
}
