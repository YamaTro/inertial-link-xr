namespace YamaTro.InertialLink.Core
{
    public struct PacketHeader
    {
        public readonly byte Major;
        public readonly byte Minor;
        public readonly PacketType Type;
        public readonly byte Flags;
        public readonly ushort PayloadLength;
        public readonly uint Sequence;
        public readonly ulong SessionId;
        public readonly long EventTimeNanoseconds;

        public PacketHeader(byte major, byte minor, PacketType type, byte flags, ushort payloadLength,
            uint sequence, ulong sessionId, long eventTimeNanoseconds)
        {
            Major = major;
            Minor = minor;
            Type = type;
            Flags = flags;
            PayloadLength = payloadLength;
            Sequence = sequence;
            SessionId = sessionId;
            EventTimeNanoseconds = eventTimeNanoseconds;
        }

        public bool IsAuthenticated { get { return (Flags & ProtocolConstants.AuthenticationFlag) != 0; } }
    }

    public struct ImuPayload
    {
        public readonly long SenderSendTimeNanoseconds;
        public readonly Float3 RawAcceleration;
        public readonly Float3 AngularVelocity;
        public readonly Float3 Gravity;
        public readonly Float3 LinearAcceleration;
        public readonly Float4 Rotation;
        public readonly uint CalibrationId;
        public readonly uint StatusBits;

        public ImuPayload(long senderSendTimeNanoseconds, Float3 rawAcceleration, Float3 angularVelocity,
            Float3 gravity, Float3 linearAcceleration, Float4 rotation, uint calibrationId, uint statusBits)
        {
            SenderSendTimeNanoseconds = senderSendTimeNanoseconds;
            RawAcceleration = rawAcceleration;
            AngularVelocity = angularVelocity;
            Gravity = gravity;
            LinearAcceleration = linearAcceleration;
            Rotation = rotation;
            CalibrationId = calibrationId;
            StatusBits = statusBits;
        }
    }

    public struct SyncRequestPayload
    {
        public readonly long T0;
        public readonly ulong Nonce;
        public SyncRequestPayload(long t0, ulong nonce) { T0 = t0; Nonce = nonce; }
    }

    public struct SyncResponsePayload
    {
        public readonly long T0;
        public readonly long T1;
        public readonly long T2;
        public readonly ulong Nonce;
        public SyncResponsePayload(long t0, long t1, long t2, ulong nonce) { T0 = t0; T1 = t1; T2 = t2; Nonce = nonce; }
    }

    public sealed class DecodedPacket
    {
        public PacketHeader Header { get; internal set; }
        public ImuPayload Imu { get; internal set; }
        public SyncRequestPayload SyncRequest { get; internal set; }
        public SyncResponsePayload SyncResponse { get; internal set; }
    }

    public struct PacketDecodeResult
    {
        public readonly PacketError Error;
        public readonly DecodedPacket Packet;
        public readonly string Detail;

        public PacketDecodeResult(PacketError error, DecodedPacket packet, string detail)
        {
            Error = error;
            Packet = packet;
            Detail = detail;
        }

        public bool Success { get { return Error == PacketError.None && Packet != null; } }
    }
}
