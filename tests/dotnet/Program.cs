using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using YamaTro.InertialLink.Core;

internal static class Program
{
    private const string GoldenImuHex = "494c58520100010100200050112233440102030405060708000000003b9aca00000000003ba26b203f80000040000000404000003dcccccd3e4ccccd3e99999a00000000c11ce80a000000003f8ccccd400ccccd405333330000000000000000000000003f800000aabbccdd0000041fc469443bfeaa907111df804297ea6214";
    private const string GoldenSyncRequestHex = "494c58520100020100200010000000018877665544332211000000007735940000000000773594001020304050607080a644347726832b2f8f879f74b9bf6a41";
    private const string GoldenSyncResponseHex = "494c58520100030100200020000000028877665544332211000000007736575000000000773594000000000077365750000000007736b8f81020304050607080ccdad3a1c13f90a19a5b8c1cdf2e3baf";
    private static readonly byte[] Key = Hex("000102030405060708090a0b0c0d0e0f");
    private static int failures;

    private static int Main()
    {
        Run("pairing key accepts canonical and separated forms", PairingKeyParsing);
        Run("golden IMU vector decodes exactly", GoldenImuDecodes);
        Run("encoder matches independent golden IMU vector", EncoderMatchesGolden);
        Run("golden sync request decodes and encodes", GoldenSyncRequest);
        Run("authenticated IMU establishes the sync session", SyncSessionInterop);
        Run("malformed framing is rejected", MalformedFraming);
        Run("UDP receive allocation is capped before decoding", BoundedUdpReceiveBoundary);
        Run("authentication is mandatory and tamper evident", AuthenticationBoundary);
        Run("public encoders reject invalid or unauthenticated output", EncoderBoundary);
        Run("disposed decoders cannot reuse zeroized keys", DecoderDisposalBoundary);
        Run("non-finite and out-of-range motion is rejected", NumericBoundary);
        Run("generic motion samples share full numeric validation", GenericMotionValidationBoundary);
        Run("status bits reject reserved and conflicting accuracy states", StatusBoundary);
        Run("unsynchronized motion remains diagnostic-only", UnsynchronizedMotionBoundary);
        Run("invalid IDs and timestamps are rejected", IdentityAndTimeBoundary);
        Run("replay window handles duplicate/reorder and forbids same-session wrap", ReplayWindow);
        Run("replay storage stays bounded across sessions", ReplaySessionBound);
        Run("sender pin rejects endpoint and session takeover before replay", SenderPinBoundary);
        Run("outbound sequence allocator never wraps", NonWrappingSequenceBoundary);
        Run("time synchronization rejects bad exchanges", TimeSynchronization);
        Run("time synchronization lease expires closed", TimeSyncLeaseBoundary);
        Run("freshness rejects stale and future packets", FreshnessBoundary);
        Run("low pass filter is finite, bounded and resettable", FilterBoundary);
        Run("safety gate warms up and fades on dropout", DropoutSafety);

        Console.WriteLine(failures == 0 ? "All InertialLink Core tests passed." : failures + " test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void PairingKeyParsing()
    {
        byte[] parsed;
        True(PairingKey.TryParseHex("000102030405060708090A0B0C0D0E0F", out parsed), "canonical key");
        BytesEqual(Key, parsed);
        True(PairingKey.TryParseHex("00-01-02-03-04-05-06-07\t08 09 0a 0b\r\n0c 0d 0e 0f", out parsed), "separated key");
        BytesEqual(Key, parsed);
        False(PairingKey.TryParseHex("0011", out parsed), "short key");
        False(PairingKey.TryParseHex("000102030405060708090a0b0c0d0e0g", out parsed), "non-hex key");
        False(PairingKey.TryParseHex("00:01:02:03:04:05:06:07:08:09:0a:0b:0c:0d:0e:0f", out parsed), "colon separator");
        False(PairingKey.TryParseHex("0001020304050607\u00a008090a0b0c0d0e0f", out parsed), "non-ASCII whitespace");
        False(PairingKey.TryParseHex("000102030405060708090a0b0c0d0e0f!", out parsed), "unexpected separator");
    }

    private static void GoldenImuDecodes()
    {
        var result = Decoder().Decode(Hex(GoldenImuHex));
        True(result.Success, result.Detail);
        Equal(PacketType.Imu, result.Packet.Header.Type);
        Equal(0x11223344U, result.Packet.Header.Sequence);
        Equal(0x0102030405060708UL, result.Packet.Header.SessionId);
        Equal(1000000000L, result.Packet.Header.EventTimeNanoseconds);
        Equal(1000500000L, result.Packet.Imu.SenderSendTimeNanoseconds);
        Near(1f, result.Packet.Imu.RawAcceleration.X);
        Near(2f, result.Packet.Imu.RawAcceleration.Y);
        Near(3f, result.Packet.Imu.RawAcceleration.Z);
        Near(0.1f, result.Packet.Imu.AngularVelocity.X);
        Near(-9.80665f, result.Packet.Imu.Gravity.Y);
        Near(3.3f, result.Packet.Imu.LinearAcceleration.Z);
        Near(1f, result.Packet.Imu.Rotation.W);
        Equal(0xAABBCCDDU, result.Packet.Imu.CalibrationId);
        Equal(0x0000041FU, result.Packet.Imu.StatusBits);
    }

    private static void EncoderMatchesGolden()
    {
        var actual = PacketEncoder.EncodeImu(0x11223344, 0x0102030405060708UL, 1000000000L,
            ValidImu(), Key);
        BytesEqual(Hex(GoldenImuHex), actual);
    }

    private static void GoldenSyncRequest()
    {
        var golden = Hex(GoldenSyncRequestHex);
        var decoded = Decoder().Decode(golden);
        True(decoded.Success, decoded.Detail);
        Equal(PacketType.SyncRequest, decoded.Packet.Header.Type);
        Equal(2000000000L, decoded.Packet.SyncRequest.T0);
        Equal(0x1020304050607080UL, decoded.Packet.SyncRequest.Nonce);
        var encoded = PacketEncoder.EncodeSyncRequest(1, 0x8877665544332211UL, 2000000000L,
            2000000000L, 0x1020304050607080UL, Key);
        BytesEqual(golden, encoded);

        var goldenResponse = Hex(GoldenSyncResponseHex);
        var encodedGoldenResponse = PacketEncoder.EncodeSyncResponse(2, 0x8877665544332211UL, 2000050000L,
            2000000000L, 2000050000L, 2000075000L, 0x1020304050607080UL, Key);
        BytesEqual(goldenResponse, encodedGoldenResponse);
        True(Decoder().Decode(goldenResponse).Success, "golden sync response");

        var response = PacketEncoder.EncodeSyncResponse(2, 7, 3000, 1000, 2100, 2200, 99, Key);
        var responseDecoded = Decoder().Decode(response);
        True(responseDecoded.Success, responseDecoded.Detail);
        Equal(2200L, responseDecoded.Packet.SyncResponse.T2);
        Equal(99UL, responseDecoded.Packet.SyncResponse.Nonce);
    }

    private static void SyncSessionInterop()
    {
        var imu = Decoder().Decode(Golden()).Packet;
        const long firstT0 = 10000000000L;
        var requestBytes = PacketEncoder.EncodeSyncRequest(1, imu.Header.SessionId, firstT0,
            firstT0, 0xCAFEBABEUL, Key);
        var request = Decoder().Decode(requestBytes);
        True(request.Success, request.Detail);
        Equal(imu.Header.SessionId, request.Packet.Header.SessionId);

        var responseBytes = PacketEncoder.EncodeSyncResponse(0x11223345, imu.Header.SessionId,
            15011000000L, request.Packet.SyncRequest.T0, 15010000000L, 15011000000L,
            request.Packet.SyncRequest.Nonce, Key);
        var response = Decoder().Decode(responseBytes);
        True(response.Success, response.Detail);
        Equal(imu.Header.SessionId, response.Packet.Header.SessionId);
        var estimator = new TimeSyncEstimator();
        True(estimator.AddExchange(response.Packet.SyncResponse.T0, response.Packet.SyncResponse.T1,
            response.Packet.SyncResponse.T2, 10021000000L), "first response");

        True(estimator.AddExchange(11000000000L, 16010000000L, 16011000000L, 11021000000L),
            "second response");
        True(estimator.IsSynchronized, "two authenticated exchanges synchronize clocks");
        Equal(5000000000L, estimator.EstimatedOffsetNanoseconds);
    }

    private static void MalformedFraming()
    {
        ExpectError(null, PacketError.NullDatagram);
        ExpectError(new byte[31], PacketError.TooShort);
        ExpectError(new byte[513], PacketError.TooLarge);

        var packet = Golden();
        packet[0] = (byte)'X';
        ExpectError(packet, PacketError.BadMagic);
        packet = Golden(); packet[4] = 2;
        ExpectError(packet, PacketError.UnsupportedVersion);
        packet = Golden(); packet[5] = 1;
        ExpectError(packet, PacketError.UnsupportedVersion);
        packet = Golden(); packet[6] = 99;
        ExpectError(packet, PacketError.UnsupportedType);
        packet = Golden(); packet[7] = 0x81;
        ExpectError(packet, PacketError.UnsupportedFlags);
        packet = Golden(); WriteUInt16(packet, 8, 31);
        ExpectError(packet, PacketError.BadHeaderLength);
        packet = Golden(); WriteUInt16(packet, 10, 79);
        ExpectError(packet, PacketError.BadPayloadLength);
        packet = Resize(Golden(), 127);
        ExpectError(packet, PacketError.LengthMismatch);
        packet = Resize(Golden(), 129);
        ExpectError(packet, PacketError.LengthMismatch);
    }

    private static void BoundedUdpReceiveBoundary()
    {
        using (var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            receiver.ReceiveTimeout = 2000;
            var destination = (IPEndPoint)receiver.LocalEndPoint;
            var oversized = new byte[1024];
            sender.SendTo(oversized, destination);
            var buffer = new byte[BoundedDatagramReceiver.DetectionBufferLength];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int received;
            False(BoundedDatagramReceiver.TryReceive(receiver, buffer, ref remote, out received),
                "oversize datagram");
            True(received > ProtocolConstants.MaximumDatagramLength, "oversize detected with fixed buffer");

            var exact = new byte[ProtocolConstants.MaximumDatagramLength];
            exact[0] = 0x5a;
            sender.SendTo(exact, destination);
            remote = new IPEndPoint(IPAddress.Any, 0);
            True(BoundedDatagramReceiver.TryReceive(receiver, buffer, ref remote, out received),
                "exact maximum datagram");
            Equal(ProtocolConstants.MaximumDatagramLength, received);
            Equal((byte)0x5a, buffer[0]);
        }
        Throws<ArgumentException>(() =>
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int received;
                BoundedDatagramReceiver.TryReceive(socket, new byte[512], ref remote, out received);
            }
        });
    }

    private static void AuthenticationBoundary()
    {
        var tampered = Golden();
        tampered[40] ^= 1;
        ExpectError(tampered, PacketError.AuthenticationFailed);
        tampered = Golden();
        tampered[tampered.Length - 1] ^= 1;
        ExpectError(tampered, PacketError.AuthenticationFailed);

        var unsigned = Resize(Golden(), 112);
        unsigned[7] = 0;
        ExpectError(unsigned, PacketError.AuthenticationRequired);
        Throws<ArgumentNullException>(() => new PacketDecoder(null));
        Equal(PacketError.AuthenticationFailed,
            new PacketDecoder(Hex("ffffffffffffffffffffffffffffffff")).Decode(Golden()).Error);
    }

    private static void EncoderBoundary()
    {
        Throws<ArgumentNullException>(() => PacketEncoder.EncodeImu(1, 1, 1000000000L, ValidImu(), null));
        Throws<ArgumentException>(() => PacketEncoder.EncodeImu(1, 1, 1000000000L, ValidImu(), new byte[15]));
        var invalidTime = new ImuPayload(999999999L, new Float3(0, 0, 0), new Float3(0, 0, 0),
            new Float3(0, 9.8f, 0), new Float3(0, 0, 0), new Float4(0, 0, 0, 1), 1, 0x41F);
        Throws<ArgumentException>(() => PacketEncoder.EncodeImu(1, 1, 1000000000L, invalidTime, Key));
        var invalidFloat = new ImuPayload(1000500000L, new Float3(float.NaN, 0, 0), new Float3(0, 0, 0),
            new Float3(0, 9.8f, 0), new Float3(0, 0, 0), new Float4(0, 0, 0, 1), 1, 0x41F);
        Throws<ArgumentException>(() => PacketEncoder.EncodeImu(1, 1, 1000000000L, invalidFloat, Key));
        var invalidStatus = new ImuPayload(1000500000L, new Float3(0, 0, 0), new Float3(0, 0, 0),
            new Float3(0, 9.8f, 0), new Float3(0, 0, 0), new Float4(0, 0, 0, 1), 1, 0x31F);
        Throws<ArgumentException>(() => PacketEncoder.EncodeImu(1, 1, 1000000000L, invalidStatus, Key));
        Throws<ArgumentOutOfRangeException>(() => PacketEncoder.EncodeSyncRequest(1, 1, 1, 0, 0, Key));
        Throws<ArgumentOutOfRangeException>(() => PacketEncoder.EncodeSyncResponse(1, 1, 1, 1, 0, 0, 0, Key));
        Throws<ArgumentOutOfRangeException>(() => PacketEncoder.EncodeSyncResponse(1, 1, 1, 1, 3, 2, 0, Key));
        Throws<ArgumentNullException>(() => PacketEncoder.EncodeSyncRequest(1, 1, 1, 1, 0, null));
        Throws<ArgumentException>(() => PacketEncoder.EncodeSyncRequest(1, 1, 1, 1, 0, new byte[17]));
        Throws<ArgumentOutOfRangeException>(() => PacketEncoder.EncodeSyncRequest(1, 0, 1, 1, 0, Key));
        Throws<ArgumentOutOfRangeException>(() => PacketEncoder.EncodeSyncRequest(1, 1, 0, 0, 0, Key));
        Throws<ArgumentException>(() => PacketEncoder.EncodeSyncRequest(1, 1, 2, 1, 0, Key));

        var mismatchedSync = PacketEncoder.EncodeSyncRequest(1, 1, 1, 1, 9, Key);
        WriteInt64(mismatchedSync, 24, 2);
        Retag(mismatchedSync);
        ExpectError(mismatchedSync, PacketError.InvalidTimestamp);
    }

    private static void DecoderDisposalBoundary()
    {
        var zeroKey = new byte[ProtocolConstants.PairingKeyLength];
        var packet = PacketEncoder.EncodeImu(1, 1, 1000000000L, ValidImu(), zeroKey);
        var decoder = new PacketDecoder(zeroKey);
        True(decoder.Decode(packet).Success, "zero key is valid while decoder is live");
        decoder.Dispose();
        decoder.Dispose();
        Throws<ObjectDisposedException>(() => decoder.Decode(packet));
    }

    private static void NumericBoundary()
    {
        var packet = Golden(); WriteUInt32(packet, 40, 0x7FC00000U); Retag(packet);
        ExpectError(packet, PacketError.NonFiniteValue);
        packet = Golden(); WriteUInt32(packet, 52, 0x7F800000U); Retag(packet);
        ExpectError(packet, PacketError.NonFiniteValue);
        packet = Golden(); WriteSingle(packet, 40, 200.01f); Retag(packet);
        ExpectError(packet, PacketError.ValueOutOfRange);
        packet = Golden(); WriteSingle(packet, 52, 50.01f); Retag(packet);
        ExpectError(packet, PacketError.ValueOutOfRange);
        packet = Golden(); WriteSingle(packet, 68, 30.01f); Retag(packet);
        ExpectError(packet, PacketError.ValueOutOfRange);
        packet = Golden(); WriteSingle(packet, 88, 1.51f); Retag(packet);
        ExpectError(packet, PacketError.ValueOutOfRange);
        packet = Golden();
        WriteSingle(packet, 88, 0f); WriteSingle(packet, 92, 0f); WriteSingle(packet, 96, 0f); WriteSingle(packet, 100, 0f); Retag(packet);
        ExpectError(packet, PacketError.InvalidQuaternion);
        packet = Golden(); WriteSingle(packet, 100, 1.51f); Retag(packet);
        ExpectError(packet, PacketError.ValueOutOfRange);

        packet = Golden();
        WriteSingle(packet, 40, 200f); WriteSingle(packet, 52, 50f); WriteSingle(packet, 68, -30f);
        WriteSingle(packet, 84, 200f); WriteSingle(packet, 100, 1.5f); Retag(packet);
        True(Decoder().Decode(packet).Success, "exact numeric bounds must be accepted");
    }

    private static void GenericMotionValidationBoundary()
    {
        Equal(PacketError.None, MotionSampleValidator.Validate(ValidImu()));
        var invalidOptionalRaw = new ImuPayload(1, new Float3(float.NaN, 0, 0), new Float3(0, 0, 0),
            new Float3(0, 0, 0), new Float3(0, 0, 0), new Float4(0, 0, 0, 1), 0,
            (uint)(SensorStatusBits.GyroscopeValid | SensorStatusBits.LinearAccelerationValid));
        Equal(PacketError.NonFiniteValue, MotionSampleValidator.Validate(invalidOptionalRaw));
        var invalidRotation = new ImuPayload(1, Float3.Zero, Float3.Zero, Float3.Zero, Float3.Zero,
            new Float4(0, 0, 0, 0), 0,
            (uint)(SensorStatusBits.GyroscopeValid | SensorStatusBits.LinearAccelerationValid));
        Equal(PacketError.InvalidQuaternion, MotionSampleValidator.Validate(invalidRotation));
        var optionalFieldsAbsent = new ImuPayload(1, Float3.Zero, Float3.Zero, Float3.Zero, Float3.Zero,
            new Float4(0, 0, 0, 1), 0,
            (uint)(SensorStatusBits.GyroscopeValid | SensorStatusBits.LinearAccelerationValid));
        Equal(PacketError.None, MotionSampleValidator.Validate(optionalFieldsAbsent));
    }

    private static void StatusBoundary()
    {
        var packet = Golden(); WriteUInt32(packet, 108, 0x0000081FU); Retag(packet);
        ExpectError(packet, PacketError.InvalidStatusBits);
        packet = Golden(); WriteUInt32(packet, 108, 0x0000031FU); Retag(packet);
        ExpectError(packet, PacketError.InvalidStatusBits);
        True(SensorStatus.HasRequiredMotionInputs(0x0A), "gyro + linear valid");
        False(SensorStatus.HasRequiredMotionInputs(0x08), "gyro missing");
    }

    private static void UnsynchronizedMotionBoundary()
    {
        const uint valid = 0x0000041F;
        False(MotionEligibility.CanDrive(false, valid), "unsynchronized network timestamp");
        True(MotionEligibility.CanDrive(true, valid), "trusted timestamp and required sensors");
        False(MotionEligibility.CanDrive(true, (uint)SensorStatusBits.LinearAccelerationValid), "gyro invalid");
        False(MotionEligibility.CanDrive(true, 0x0000031F), "conflicting accuracy bits");
    }

    private static void IdentityAndTimeBoundary()
    {
        var packet = Golden(); WriteUInt64(packet, 16, 0); Retag(packet);
        ExpectError(packet, PacketError.InvalidSession);
        packet = Golden(); WriteInt64(packet, 24, 0); Retag(packet);
        ExpectError(packet, PacketError.InvalidTimestamp);
        packet = Golden(); WriteInt64(packet, 32, 999999999L); Retag(packet);
        ExpectError(packet, PacketError.InvalidTimestamp);

        var response = PacketEncoder.EncodeSyncResponse(1, 1, 100, 100, 300, 300, 4, Key);
        WriteInt64(response, ProtocolConstants.HeaderLength + 16, 200);
        Retag(response);
        ExpectError(response, PacketError.InvalidTimestamp);
    }

    private static void ReplayWindow()
    {
        var replay = new ReplayProtector();
        Equal(ReplayDecision.Accepted, replay.TryAccept(9, 100));
        Equal(ReplayDecision.Accepted, replay.TryAccept(9, 102));
        Equal(ReplayDecision.Accepted, replay.TryAccept(9, 101));
        Equal(ReplayDecision.Duplicate, replay.TryAccept(9, 101));
        Equal(ReplayDecision.TooOld, replay.TryAccept(9, 30));
        Equal(ReplayDecision.InvalidSession, replay.TryAccept(0, 1));

        replay.Reset();
        Equal(ReplayDecision.Accepted, replay.TryAccept(7, 0xFFFFFFFE));
        Equal(ReplayDecision.Accepted, replay.TryAccept(7, 0xFFFFFFFF));
        Equal(ReplayDecision.TooOld, replay.TryAccept(7, 0));
        Equal(ReplayDecision.TooOld, replay.TryAccept(7, 1));
        Equal(ReplayDecision.Duplicate, replay.TryAccept(7, 0xFFFFFFFF));
        Equal(ReplayDecision.Accepted, replay.TryAccept(8, 0));
    }

    private static void ReplaySessionBound()
    {
        var replay = new ReplayProtector(2);
        Equal(ReplayDecision.Accepted, replay.TryAccept(1, 1));
        Equal(ReplayDecision.Accepted, replay.TryAccept(2, 1));
        Equal(ReplayDecision.Accepted, replay.TryAccept(1, 2));
        Equal(ReplayDecision.Accepted, replay.TryAccept(3, 1)); // evicts least-recently-used session 2
        Equal(ReplayDecision.Accepted, replay.TryAccept(2, 1));
    }

    private static void SenderPinBoundary()
    {
        var pin = new SenderPin<string>();
        var replay = new ReplayProtector();
        Equal(SenderPinDecision.NotEstablished, pin.Check("10.0.0.2:5000", 7, false));
        Equal(SenderPinDecision.Pinned, pin.Check("10.0.0.1:5000", 7, true));
        Equal(ReplayDecision.Accepted, replay.TryAccept(7, 100));

        // A captured valid packet replayed from another endpoint is rejected by the pin first;
        // replay state and the established sender remain unchanged.
        Equal(SenderPinDecision.EndpointMismatch, pin.Check("10.0.0.2:5000", 7, true));
        Equal(SenderPinDecision.SessionMismatch, pin.Check("10.0.0.1:5000", 8, true));
        Equal(SenderPinDecision.Accepted, pin.Check("10.0.0.1:5000", 7, false));
        Equal(ReplayDecision.Accepted, replay.TryAccept(7, 101));
        Equal("10.0.0.1:5000", pin.Endpoint);
        Equal(7UL, pin.SessionId);
        pin.Reset();
        False(pin.IsEstablished, "reset permits explicit re-pairing");
    }

    private static void NonWrappingSequenceBoundary()
    {
        var counter = new NonWrappingSequenceCounter(uint.MaxValue - 1);
        uint sequence;
        True(counter.TryTake(out sequence), "penultimate sequence");
        Equal(uint.MaxValue - 1, sequence);
        True(counter.TryTake(out sequence), "final sequence");
        Equal(uint.MaxValue, sequence);
        True(counter.IsExhausted, "counter is permanently exhausted");
        False(counter.TryTake(out sequence), "no wrapped sequence");
        Equal(0U, sequence);
    }

    private static void TimeSynchronization()
    {
        var sync = new TimeSyncEstimator();
        False(sync.AddExchange(0, 1, 2, 3), "zero timestamp");
        False(sync.AddExchange(1000, 2100, 2000, 1300), "sender timestamps reversed");
        True(sync.AddExchange(1000, 2100, 2200, 1300), "first exchange");
        False(sync.IsSynchronized, "requires two samples");
        True(sync.AddExchange(2000, 3100, 3200, 2300), "second exchange");
        True(sync.IsSynchronized, "synchronized");
        Equal(200L, sync.BestRoundTripNanoseconds);
        Equal(1000L, sync.EstimatedOffsetNanoseconds);
        Equal(1500L, sync.SenderToLocal(2500));
        False(sync.AddExchange(1000, 2100, 2200, 2000000001L), "excessive RTT");
    }

    private static void TimeSyncLeaseBoundary()
    {
        const long refreshed = 1000000000L;
        const long duration = 15000000000L;
        False(TimeSyncLease.IsValid(refreshed, 0, duration), "never refreshed");
        True(TimeSyncLease.IsValid(refreshed, refreshed, duration), "fresh exchange");
        True(TimeSyncLease.IsValid(refreshed + duration, refreshed, duration), "inclusive lease boundary");
        False(TimeSyncLease.IsValid(refreshed + duration + 1, refreshed, duration), "expired lease");
        False(TimeSyncLease.IsValid(refreshed - 1, refreshed, duration), "clock rewind");
        False(TimeSyncLease.IsValid(refreshed, refreshed, 0), "invalid duration");
    }

    private static void FreshnessBoundary()
    {
        Equal(FreshnessDecision.Accepted, PacketFreshness.Evaluate(1000000000L, 750000000L));
        Equal(FreshnessDecision.Stale, PacketFreshness.Evaluate(1000000001L, 750000000L));
        Equal(FreshnessDecision.Accepted, PacketFreshness.Evaluate(1000000000L, 1100000000L));
        Equal(FreshnessDecision.TooFarInFuture, PacketFreshness.Evaluate(1000000000L, 1100000001L));
        Equal(FreshnessDecision.Stale, PacketFreshness.Evaluate(0, 1));
    }

    private static void FilterBoundary()
    {
        var filter = new BoundedLowPassFilter3(2f, 10f);
        Float3 output;
        True(filter.TryUpdate(new Float3(1, 2, 3), 1000000000L, out output), "first value");
        Equal(new Float3(1, 2, 3), output);
        False(filter.TryUpdate(new Float3(float.NaN, 0, 0), 1010000000L, out output), "NaN");
        False(filter.TryUpdate(new Float3(11, 0, 0), 1010000000L, out output), "bound");
        True(filter.TryUpdate(new Float3(2, 2, 3), 1010000000L, out output), "second value");
        True(output.X > 1f && output.X < 2f, "filtered output lies between samples");
        var beforeReorder = output;
        False(filter.TryUpdate(new Float3(9, 9, 9), 1009999999L, out output), "out-of-order timestamp");
        Equal(beforeReorder, filter.Value);
        filter.Reset();
        True(filter.TryUpdate(new Float3(4, 5, 6), 500L, out output), "after reset");
        Equal(new Float3(4, 5, 6), output);
    }

    private static void DropoutSafety()
    {
        var gate = new SafetyGate();
        var t = 1000000000L;
        gate.BeginWarmup();
        gate.Tick(t - 1);
        Equal(MotionSafetyState.WarmingUp, gate.State);
        Equal(0f, gate.Weight);
        gate.RecordAccepted(t);
        Equal(MotionSafetyState.WarmingUp, gate.State);
        Equal(0f, gate.Weight);
        gate.RecordAccepted(t + 1000000L);
        gate.RecordAccepted(t + 2000000L);
        Equal(MotionSafetyState.Active, gate.State);
        Equal(1f, gate.Weight);
        gate.Tick(t + 252000000L);
        Equal(MotionSafetyState.Active, gate.State);
        gate.Tick(t + 377000000L);
        Equal(MotionSafetyState.Degraded, gate.State);
        Near(0.5f, gate.Weight);
        gate.Tick(t + 502000000L);
        Equal(MotionSafetyState.FadedOut, gate.State);
        Equal(0f, gate.Weight);
        gate.RecordAccepted(t + 600000000L);
        Equal(MotionSafetyState.WarmingUp, gate.State);
        gate.RejectContinuity();
        Equal(0f, gate.Weight);

        var incomplete = new SafetyGate();
        incomplete.RecordAccepted(t);
        incomplete.RecordAccepted(t + 1000000L);
        incomplete.Tick(t + 400000000L);
        Equal(MotionSafetyState.WarmingUp, incomplete.State);
        Equal(0f, incomplete.Weight);
        incomplete.Tick(t + 501000000L);
        Equal(MotionSafetyState.FadedOut, incomplete.State);
        Equal(0f, incomplete.Weight);

        var paused = new SafetyGate();
        paused.RecordAccepted(t);
        paused.RecordAccepted(t + 1000000L);
        paused.RecordAccepted(t + 2000000L);
        Equal(MotionSafetyState.Active, paused.State);
        paused.RecordAccepted(t + 502000000L);
        Equal(MotionSafetyState.WarmingUp, paused.State);
        Equal(0f, paused.Weight);
    }

    private static ImuPayload ValidImu()
    {
        return new ImuPayload(1000500000L, new Float3(1, 2, 3), new Float3(0.1f, 0.2f, 0.3f),
            new Float3(0, -9.80665f, 0), new Float3(1.1f, 2.2f, 3.3f),
            new Float4(0, 0, 0, 1), 0xAABBCCDD, 0x0000041F);
    }

    private static PacketDecoder Decoder() { return new PacketDecoder(Key); }
    private static byte[] Golden() { return Hex(GoldenImuHex); }

    private static void ExpectError(byte[] packet, PacketError expected)
    {
        var result = Decoder().Decode(packet);
        Equal(expected, result.Error);
        False(result.Success, "failed packet cannot report success");
    }

    private static void Retag(byte[] packet)
    {
        var signedLength = packet.Length - ProtocolConstants.AuthenticationTagLength;
        byte[] digest;
        using (var hmac = new HMACSHA256(Key)) digest = hmac.ComputeHash(packet, 0, signedLength);
        Buffer.BlockCopy(digest, 0, packet, signedLength, ProtocolConstants.AuthenticationTagLength);
    }

    private static byte[] Resize(byte[] source, int length)
    {
        var result = new byte[length];
        Buffer.BlockCopy(source, 0, result, 0, Math.Min(source.Length, length));
        return result;
    }

    private static byte[] Hex(string text)
    {
        var result = new byte[text.Length / 2];
        for (var i = 0; i < result.Length; i++) result[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        return result;
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8); data[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24); data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8); data[offset + 3] = (byte)value;
    }

    private static void WriteUInt64(byte[] data, int offset, ulong value)
    {
        WriteUInt32(data, offset, (uint)(value >> 32)); WriteUInt32(data, offset + 4, (uint)value);
    }

    private static void WriteInt64(byte[] data, int offset, long value) { WriteUInt64(data, offset, unchecked((ulong)value)); }

    private static void WriteSingle(byte[] data, int offset, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        Buffer.BlockCopy(bytes, 0, data, offset, 4);
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
    }

    private static void True(bool condition, string message) { if (!condition) throw new Exception("Expected true: " + message); }
    private static void False(bool condition, string message) { if (condition) throw new Exception("Expected false: " + message); }
    private static void Near(float expected, float actual)
    {
        if (Math.Abs(expected - actual) > 0.0001f) throw new Exception("Expected " + expected + ", got " + actual);
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Expected " + expected + ", got " + actual);
    }
    private static void BytesEqual(byte[] expected, byte[] actual)
    {
        Equal(expected.Length, actual.Length);
        var difference = 0;
        for (var i = 0; i < expected.Length; i++) difference |= expected[i] ^ actual[i];
        if (difference != 0) throw new Exception("Byte arrays differ.");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        catch (Exception exception) { throw new Exception("Expected " + typeof(TException).Name + ", got " + exception.GetType().Name); }
        throw new Exception("Expected " + typeof(TException).Name + ", but no exception was thrown.");
    }
}
