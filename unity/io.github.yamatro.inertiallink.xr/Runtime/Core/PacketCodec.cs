using System;
using System.Security.Cryptography;

namespace YamaTro.InertialLink.Core
{
    public sealed class PacketDecoder : IDisposable
    {
        private readonly byte[] pairingKey;
        private bool disposed;

        public PacketDecoder(byte[] pairingKey)
        {
            if (pairingKey == null) throw new ArgumentNullException("pairingKey");
            if (pairingKey.Length != ProtocolConstants.PairingKeyLength)
                throw new ArgumentException("Pairing key must be exactly 16 bytes.", "pairingKey");
            this.pairingKey = (byte[])pairingKey.Clone();
        }

        public PacketDecodeResult Decode(byte[] datagram)
        {
            if (disposed) throw new ObjectDisposedException("PacketDecoder");
            if (datagram == null) return Fail(PacketError.NullDatagram, "Datagram is null.");
            if (datagram.Length < ProtocolConstants.HeaderLength) return Fail(PacketError.TooShort, "Header is incomplete.");
            if (datagram.Length > ProtocolConstants.MaximumDatagramLength) return Fail(PacketError.TooLarge, "Datagram exceeds the hard limit.");
            if (datagram[0] != (byte)'I' || datagram[1] != (byte)'L' || datagram[2] != (byte)'X' || datagram[3] != (byte)'R')
                return Fail(PacketError.BadMagic, "Magic does not match ILXR.");
            if (datagram[4] != ProtocolConstants.MajorVersion || datagram[5] > ProtocolConstants.MinorVersion)
                return Fail(PacketError.UnsupportedVersion, "Protocol version is not supported.");
            if (datagram[6] < (byte)PacketType.Imu || datagram[6] > (byte)PacketType.SyncResponse)
                return Fail(PacketError.UnsupportedType, "Packet type is not supported.");
            if ((datagram[7] & ~ProtocolConstants.AuthenticationFlag) != 0)
                return Fail(PacketError.UnsupportedFlags, "Unknown flag bits are set.");
            if (datagram[7] != ProtocolConstants.AuthenticationFlag)
                return Fail(PacketError.AuthenticationRequired, "Authenticated flag is required.");

            var headerLength = BigEndian.ReadUInt16(datagram, 8);
            var payloadLength = BigEndian.ReadUInt16(datagram, 10);
            if (headerLength != ProtocolConstants.HeaderLength) return Fail(PacketError.BadHeaderLength, "Header length must be 32.");

            var type = (PacketType)datagram[6];
            var expectedPayload = type == PacketType.Imu ? ProtocolConstants.ImuPayloadLength :
                type == PacketType.SyncRequest ? ProtocolConstants.SyncRequestPayloadLength : ProtocolConstants.SyncResponsePayloadLength;
            if (payloadLength != expectedPayload) return Fail(PacketError.BadPayloadLength, "Payload length does not match packet type.");

            var authenticated = (datagram[7] & ProtocolConstants.AuthenticationFlag) != 0;
            var expectedLength = ProtocolConstants.HeaderLength + payloadLength + (authenticated ? ProtocolConstants.AuthenticationTagLength : 0);
            if (datagram.Length != expectedLength) return Fail(PacketError.LengthMismatch, "Datagram length is not exact.");
            if (!VerifyTag(datagram, ProtocolConstants.HeaderLength + payloadLength))
                return Fail(PacketError.AuthenticationFailed, "Authentication tag mismatch.");

            var sequence = BigEndian.ReadUInt32(datagram, 12);
            var sessionId = BigEndian.ReadUInt64(datagram, 16);
            var eventTime = BigEndian.ReadInt64(datagram, 24);
            if (sessionId == 0) return Fail(PacketError.InvalidSession, "Session ID must be non-zero.");
            if (eventTime <= 0) return Fail(PacketError.InvalidTimestamp, "Event timestamp must be positive.");

            var packet = new DecodedPacket
            {
                Header = new PacketHeader(datagram[4], datagram[5], type, datagram[7], payloadLength,
                    sequence, sessionId, eventTime)
            };

            var offset = ProtocolConstants.HeaderLength;
            if (type == PacketType.Imu)
            {
                var sendTime = BigEndian.ReadInt64(datagram, offset);
                var raw = ReadFloat3(datagram, offset + 8);
                var gyro = ReadFloat3(datagram, offset + 20);
                var gravity = ReadFloat3(datagram, offset + 32);
                var linear = ReadFloat3(datagram, offset + 44);
                var rotation = new Float4(BigEndian.ReadSingle(datagram, offset + 56), BigEndian.ReadSingle(datagram, offset + 60),
                    BigEndian.ReadSingle(datagram, offset + 64), BigEndian.ReadSingle(datagram, offset + 68));
                var calibrationId = BigEndian.ReadUInt32(datagram, offset + 72);
                var statusBits = BigEndian.ReadUInt32(datagram, offset + 76);
                var candidate = new ImuPayload(sendTime, raw, gyro, gravity, linear, rotation, calibrationId, statusBits);
                var validation = ValidateImu(eventTime, candidate);
                if (validation != PacketError.None) return Fail(validation, "IMU values failed timestamp/status/finite/range validation.");
                packet.Imu = new ImuPayload(sendTime, raw, gyro, gravity, linear, rotation.Normalized(), calibrationId, statusBits);
            }
            else if (type == PacketType.SyncRequest)
            {
                var t0 = BigEndian.ReadInt64(datagram, offset);
                if (t0 <= 0 || t0 != eventTime)
                    return Fail(PacketError.InvalidTimestamp, "Sync request t0 must be positive and equal header event time.");
                packet.SyncRequest = new SyncRequestPayload(t0, BigEndian.ReadUInt64(datagram, offset + 8));
            }
            else
            {
                var t0 = BigEndian.ReadInt64(datagram, offset);
                var t1 = BigEndian.ReadInt64(datagram, offset + 8);
                var t2 = BigEndian.ReadInt64(datagram, offset + 16);
                if (t0 <= 0 || t1 <= 0 || t2 < t1) return Fail(PacketError.InvalidTimestamp, "Sync response timestamps are invalid.");
                packet.SyncResponse = new SyncResponsePayload(t0, t1, t2, BigEndian.ReadUInt64(datagram, offset + 24));
            }

            return new PacketDecodeResult(PacketError.None, packet, string.Empty);
        }

        public void Dispose()
        {
            if (disposed) return;
            if (pairingKey != null) Array.Clear(pairingKey, 0, pairingKey.Length);
            disposed = true;
        }

        internal static PacketError ValidateImu(long eventTime, ImuPayload imu)
        {
            if (eventTime <= 0 || imu.SenderSendTimeNanoseconds <= 0 || imu.SenderSendTimeNanoseconds < eventTime)
                return PacketError.InvalidTimestamp;
            return MotionSampleValidator.Validate(imu);
        }

        private static Float3 ReadFloat3(byte[] data, int offset)
        {
            return new Float3(BigEndian.ReadSingle(data, offset), BigEndian.ReadSingle(data, offset + 4), BigEndian.ReadSingle(data, offset + 8));
        }

        private bool VerifyTag(byte[] packet, int signedLength)
        {
            byte[] digest;
            using (var hmac = new HMACSHA256(pairingKey)) digest = hmac.ComputeHash(packet, 0, signedLength);
            var difference = 0;
            for (var i = 0; i < ProtocolConstants.AuthenticationTagLength; i++) difference |= digest[i] ^ packet[signedLength + i];
            Array.Clear(digest, 0, digest.Length);
            return difference == 0;
        }

        private static PacketDecodeResult Fail(PacketError error, string detail) { return new PacketDecodeResult(error, null, detail); }
    }

    public static class PacketEncoder
    {
        public static byte[] EncodeSyncRequest(uint sequence, ulong sessionId, long eventTime, long t0, ulong nonce, byte[] key)
        {
            if (t0 <= 0) throw new ArgumentOutOfRangeException("t0");
            if (eventTime != t0) throw new ArgumentException("Sync request eventTime must equal t0.", "eventTime");
            var payload = new byte[ProtocolConstants.SyncRequestPayloadLength];
            BigEndian.WriteInt64(payload, 0, t0);
            BigEndian.WriteUInt64(payload, 8, nonce);
            return Encode(PacketType.SyncRequest, sequence, sessionId, eventTime, payload, key);
        }

        public static byte[] EncodeSyncResponse(uint sequence, ulong sessionId, long eventTime, long t0, long t1, long t2, ulong nonce, byte[] key)
        {
            if (t0 <= 0) throw new ArgumentOutOfRangeException("t0");
            if (t1 <= 0) throw new ArgumentOutOfRangeException("t1");
            if (t2 < t1) throw new ArgumentOutOfRangeException("t2");
            var payload = new byte[ProtocolConstants.SyncResponsePayloadLength];
            BigEndian.WriteInt64(payload, 0, t0);
            BigEndian.WriteInt64(payload, 8, t1);
            BigEndian.WriteInt64(payload, 16, t2);
            BigEndian.WriteUInt64(payload, 24, nonce);
            return Encode(PacketType.SyncResponse, sequence, sessionId, eventTime, payload, key);
        }

        public static byte[] EncodeImu(uint sequence, ulong sessionId, long eventTime, ImuPayload imu, byte[] key)
        {
            var validation = PacketDecoder.ValidateImu(eventTime, imu);
            if (validation != PacketError.None)
                throw new ArgumentException("Invalid IMU payload: " + validation, "imu");
            var payload = new byte[ProtocolConstants.ImuPayloadLength];
            BigEndian.WriteInt64(payload, 0, imu.SenderSendTimeNanoseconds);
            WriteFloat3(payload, 8, imu.RawAcceleration);
            WriteFloat3(payload, 20, imu.AngularVelocity);
            WriteFloat3(payload, 32, imu.Gravity);
            WriteFloat3(payload, 44, imu.LinearAcceleration);
            BigEndian.WriteSingle(payload, 56, imu.Rotation.X);
            BigEndian.WriteSingle(payload, 60, imu.Rotation.Y);
            BigEndian.WriteSingle(payload, 64, imu.Rotation.Z);
            BigEndian.WriteSingle(payload, 68, imu.Rotation.W);
            BigEndian.WriteUInt32(payload, 72, imu.CalibrationId);
            BigEndian.WriteUInt32(payload, 76, imu.StatusBits);
            return Encode(PacketType.Imu, sequence, sessionId, eventTime, payload, key);
        }

        private static byte[] Encode(PacketType type, uint sequence, ulong sessionId, long eventTime, byte[] payload, byte[] key)
        {
            if (sessionId == 0) throw new ArgumentOutOfRangeException("sessionId");
            if (eventTime <= 0) throw new ArgumentOutOfRangeException("eventTime");
            if (key == null) throw new ArgumentNullException("key");
            if (key.Length != ProtocolConstants.PairingKeyLength) throw new ArgumentException("Key must be 16 bytes.", "key");
            var signedLength = ProtocolConstants.HeaderLength + payload.Length;
            var result = new byte[signedLength + ProtocolConstants.AuthenticationTagLength];
            result[0] = (byte)'I'; result[1] = (byte)'L'; result[2] = (byte)'X'; result[3] = (byte)'R';
            result[4] = ProtocolConstants.MajorVersion; result[5] = ProtocolConstants.MinorVersion; result[6] = (byte)type;
            result[7] = ProtocolConstants.AuthenticationFlag;
            BigEndian.WriteUInt16(result, 8, ProtocolConstants.HeaderLength);
            BigEndian.WriteUInt16(result, 10, (ushort)payload.Length);
            BigEndian.WriteUInt32(result, 12, sequence);
            BigEndian.WriteUInt64(result, 16, sessionId);
            BigEndian.WriteInt64(result, 24, eventTime);
            Buffer.BlockCopy(payload, 0, result, ProtocolConstants.HeaderLength, payload.Length);
            byte[] digest;
            using (var hmac = new HMACSHA256(key)) digest = hmac.ComputeHash(result, 0, signedLength);
            Buffer.BlockCopy(digest, 0, result, signedLength, ProtocolConstants.AuthenticationTagLength);
            Array.Clear(digest, 0, digest.Length);
            return result;
        }

        private static void WriteFloat3(byte[] data, int offset, Float3 value)
        {
            BigEndian.WriteSingle(data, offset, value.X);
            BigEndian.WriteSingle(data, offset + 4, value.Y);
            BigEndian.WriteSingle(data, offset + 8, value.Z);
        }
    }
}
