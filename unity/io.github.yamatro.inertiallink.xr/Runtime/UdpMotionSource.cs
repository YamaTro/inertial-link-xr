using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;
using YamaTro.InertialLink.Core;

namespace YamaTro.InertialLink
{
    [DisallowMultipleComponent]
    public sealed class UdpMotionSource : MonoBehaviour, IMotionSource
    {
        [SerializeField, Range(1024, 65535)] private int listenPort = ProtocolConstants.DefaultPort;
        [SerializeField, Min(16)] private int maximumQueuedFrames = 256;
        [SerializeField, Min(0.5f)] private float syncIntervalSeconds = 2f;
        [SerializeField, Range(5f, 120f)] private float syncLeaseSeconds = 15f;

        private readonly ConcurrentQueue<MotionSourceFrame> frames = new ConcurrentQueue<MotionSourceFrame>();
        private readonly TimeSyncEstimator timeSync = new TimeSyncEstimator();
        private readonly ReplayProtector replay = new ReplayProtector();
        private readonly SenderPin<IPEndPoint> senderPin = new SenderPin<IPEndPoint>();
        private readonly NonWrappingSequenceCounter outboundSequences = new NonWrappingSequenceCounter();
        private readonly object receiverStateGate = new object();
        private Thread receiverThread;
        private UdpClient client;
        private byte[] pairingKey;
        private volatile bool running;
        private volatile string status = "Pairing key required";
        private int queueCount;
        private long acceptedPackets;
        private long rejectedPackets;
        private long droppedFrames;
        private long lastPacketAt;
        private IPEndPoint verifiedSender;
        private ulong verifiedSenderSession;
        private long outstandingT0;
        private ulong outstandingNonce;
        private long nextSyncAt;
        private long lastSuccessfulSyncAt;
        private int receiverGeneration;

        public bool IsReady
        {
            get
            {
                return running && Interlocked.Read(ref acceptedPackets) > 0 &&
                       HasTrustedTimeSync(MonotonicClock.NowNanoseconds);
            }
        }
        public string Status { get { return status; } }
        public int ListenPort { get { return listenPort; } }
        public long AcceptedPackets { get { return Interlocked.Read(ref acceptedPackets); } }
        public long RejectedPackets { get { return Interlocked.Read(ref rejectedPackets); } }
        public long DroppedFrames { get { return Interlocked.Read(ref droppedFrames); } }
        public long LastPacketAtNanoseconds { get { return Interlocked.Read(ref lastPacketAt); } }
        public bool IsTimeSynchronized { get { return HasTrustedTimeSync(MonotonicClock.NowNanoseconds); } }
        public long BestRoundTripNanoseconds
        {
            get
            {
                lock (receiverStateGate) return timeSync.BestRoundTripNanoseconds;
            }
        }

        public bool ConfigurePairingKey(string pairingKeyHex)
        {
            return Configure(pairingKeyHex, listenPort);
        }

        public void ClearPairingKey()
        {
            StopReceiver();
            ClearKey();
            status = "Pairing key required";
        }

        public bool Configure(string pairingKeyHex, int port = ProtocolConstants.DefaultPort)
        {
            StopReceiver();
            ClearKey();
            if (port < 1024 || port > 65535)
            {
                status = "Invalid listen port";
                return false;
            }
            byte[] parsed;
            if (!PairingKey.TryParseHex(pairingKeyHex, out parsed))
            {
                status = "Invalid pairing key";
                return false;
            }
            listenPort = port;
            pairingKey = parsed;
            if (isActiveAndEnabled)
            {
                StartReceiver();
                if (!running)
                {
                    ClearKey();
                    return false;
                }
            }
            return true;
        }

        public bool TryDequeue(out MotionSourceFrame frame)
        {
            if (frames.TryDequeue(out frame))
            {
                Interlocked.Decrement(ref queueCount);
                return true;
            }
            return false;
        }

        private void OnEnable()
        {
            if (pairingKey != null) StartReceiver();
        }

        private void OnDisable() { StopReceiver(); }
        private void OnDestroy() { StopReceiver(); ClearKey(); }

        private void StartReceiver()
        {
            if (running || pairingKey == null) return;
            UdpClient boundClient = null;
            byte[] unownedKeySnapshot = null;
            try
            {
                boundClient = CreateBoundClient(listenPort);
                boundClient.Client.ReceiveTimeout = 200;
                Interlocked.Exchange(ref acceptedPackets, 0);
                Interlocked.Exchange(ref rejectedPackets, 0);
                Interlocked.Exchange(ref droppedFrames, 0);
                Interlocked.Exchange(ref lastPacketAt, 0);
                client = boundClient;
                var generation = Interlocked.Increment(ref receiverGeneration);
                var keySnapshot = (byte[])pairingKey.Clone();
                unownedKeySnapshot = keySnapshot;
                running = true;
                status = "Listening (authenticated)";
                receiverThread = new Thread(() => ReceiveLoop(boundClient, keySnapshot, generation))
                    { IsBackground = true, Name = "InertialLink UDP" };
                receiverThread.Start();
                unownedKeySnapshot = null;
            }
            catch (Exception exception)
            {
                if (unownedKeySnapshot != null)
                    Array.Clear(unownedKeySnapshot, 0, unownedKeySnapshot.Length);
                status = "UDP start failed: " + exception.GetType().Name;
                running = false;
                Interlocked.Increment(ref receiverGeneration);
                if (boundClient != null) boundClient.Close();
                client = null;
            }
        }

        private void StopReceiver()
        {
            running = false;
            Interlocked.Increment(ref receiverGeneration);
            var localClient = client;
            client = null;
            if (localClient != null) localClient.Close();
            var thread = receiverThread;
            receiverThread = null;
            if (thread != null && thread.IsAlive) thread.Join(500);
            lock (receiverStateGate)
            {
                verifiedSender = null;
                verifiedSenderSession = 0;
                timeSync.Reset();
                Interlocked.Exchange(ref lastSuccessfulSyncAt, 0);
                replay.Reset();
                senderPin.Reset();
                outstandingT0 = 0;
                outstandingNonce = 0;
                nextSyncAt = 0;
                MotionSourceFrame discarded;
                while (frames.TryDequeue(out discarded)) { }
                Interlocked.Exchange(ref queueCount, 0);
                Interlocked.Exchange(ref acceptedPackets, 0);
                Interlocked.Exchange(ref lastPacketAt, 0);
            }
            status = pairingKey == null ? "Pairing key required" : "Stopped";
        }

        private void ReceiveLoop(UdpClient boundClient, byte[] keySnapshot, int generation)
        {
            try
            {
                using (var decoder = new PacketDecoder(keySnapshot))
                {
                    var receiveBuffer = new byte[BoundedDatagramReceiver.DetectionBufferLength];
                    while (IsCurrentGeneration(generation))
                    {
                        try
                        {
                            EndPoint remote = new IPEndPoint(
                                boundClient.Client.AddressFamily == AddressFamily.InterNetworkV6
                                    ? IPAddress.IPv6Any : IPAddress.Any, 0);
                            int received;
                            if (!BoundedDatagramReceiver.TryReceive(boundClient.Client, receiveBuffer, ref remote,
                                    out received))
                            {
                                Interlocked.Increment(ref rejectedPackets);
                                continue;
                            }
                            if (!IsCurrentGeneration(generation)) break;

                            var endpoint = remote as IPEndPoint;
                            if (endpoint == null)
                            {
                                Interlocked.Increment(ref rejectedPackets);
                                continue;
                            }

                            // Copy only after the fixed 513-byte receive boundary proves the datagram
                            // is within the protocol cap. Allocation is therefore attacker-bounded.
                            var datagram = new byte[received];
                            Buffer.BlockCopy(receiveBuffer, 0, datagram, 0, received);
                            var arrival = MonotonicClock.NowNanoseconds;
                            Interlocked.Exchange(ref lastPacketAt, arrival);
                            var decoded = decoder.Decode(datagram);
                            if (!decoded.Success)
                            {
                                Interlocked.Increment(ref rejectedPackets);
                                continue;
                            }
                            ProcessPacket(decoded.Packet, endpoint, arrival, generation);
                        }
                        catch (SocketException exception)
                        {
                            if (exception.SocketErrorCode == SocketError.MessageSize)
                            {
                                // Oversize UDP datagrams are intentionally dropped without a reply or log.
                                Interlocked.Increment(ref rejectedPackets);
                            }
                            else if (exception.SocketErrorCode != SocketError.TimedOut &&
                                     IsCurrentGeneration(generation))
                            {
                                status = "UDP receive error: " + exception.SocketErrorCode;
                            }
                        }
                        catch (ObjectDisposedException) { }
                        catch (Exception exception)
                        {
                            if (IsCurrentGeneration(generation))
                                status = "Receiver error: " + exception.GetType().Name;
                            Interlocked.Increment(ref rejectedPackets);
                        }

                        if (IsCurrentGeneration(generation)) MaybeSendSyncRequest(boundClient, keySnapshot, generation);
                    }
                }
            }
            finally
            {
                Array.Clear(keySnapshot, 0, keySnapshot.Length);
            }
        }

        private void ProcessPacket(DecodedPacket packet, IPEndPoint endpoint, long arrival, int generation)
        {
            lock (receiverStateGate)
            {
                if (!IsCurrentGeneration(generation)) return;
                var pinDecision = senderPin.Check(endpoint, packet.Header.SessionId, packet.Header.Type == PacketType.Imu);
                if (pinDecision == SenderPinDecision.NotEstablished || pinDecision == SenderPinDecision.EndpointMismatch ||
                    pinDecision == SenderPinDecision.SessionMismatch)
                {
                    Interlocked.Increment(ref rejectedPackets);
                    return;
                }
                if (replay.TryAccept(packet.Header.SessionId, packet.Header.Sequence) != ReplayDecision.Accepted)
                {
                    Interlocked.Increment(ref rejectedPackets);
                    return;
                }

                if (packet.Header.Type == PacketType.Imu)
                {
                    if (verifiedSender == null)
                    {
                        verifiedSender = endpoint;
                        verifiedSenderSession = packet.Header.SessionId;
                    }
                    var synchronized = HasTrustedTimeSync(arrival);
                    var localEvent = synchronized ? timeSync.SenderToLocal(packet.Header.EventTimeNanoseconds) : arrival;
                    if (synchronized && PacketFreshness.Evaluate(arrival, localEvent) != FreshnessDecision.Accepted)
                    {
                        Interlocked.Increment(ref rejectedPackets);
                        return;
                    }

                    if (!IsCurrentGeneration(generation)) return;
                    EnqueueBounded(new MotionSourceFrame(packet.Header.SessionId, packet.Header.Sequence, arrival,
                        localEvent, synchronized, packet.Imu));
                    Interlocked.Increment(ref acceptedPackets);
                    status = synchronized ? "Streaming (time synchronized)" : "Streaming (sync pending)";
                    return;
                }

                if (packet.Header.Type == PacketType.SyncResponse && verifiedSender != null && endpoint.Equals(verifiedSender) &&
                    packet.Header.SessionId == verifiedSenderSession && outstandingNonce != 0 &&
                    packet.SyncResponse.Nonce == outstandingNonce && packet.SyncResponse.T0 == outstandingT0)
                {
                    if (timeSync.AddExchange(packet.SyncResponse.T0, packet.SyncResponse.T1,
                            packet.SyncResponse.T2, arrival))
                        Interlocked.Exchange(ref lastSuccessfulSyncAt, arrival);
                    outstandingNonce = 0;
                    outstandingT0 = 0;
                }
            }
        }

        private void MaybeSendSyncRequest(UdpClient boundClient, byte[] generationKey, int generation)
        {
            lock (receiverStateGate)
            {
                if (!IsCurrentGeneration(generation) || generationKey == null || boundClient == null) return;
                var endpoint = verifiedSender;
                if (endpoint == null) return;
                var now = MonotonicClock.NowNanoseconds;
                if (now < nextSyncAt) return;
                nextSyncAt = now + (long)(Math.Max(0.5f, syncIntervalSeconds) * 1000000000.0);
                uint sequence;
                if (!outboundSequences.TryTake(out sequence))
                {
                    outstandingT0 = 0;
                    outstandingNonce = 0;
                    nextSyncAt = long.MaxValue;
                    status = "Clock sync stopped (sequence exhausted)";
                    return;
                }
                if (verifiedSenderSession == 0) return;
                outstandingT0 = now;
                outstandingNonce = RandomUInt64NonZero();
                var request = PacketEncoder.EncodeSyncRequest(sequence, verifiedSenderSession, now,
                    outstandingT0, outstandingNonce, generationKey);
                if (!IsCurrentGeneration(generation)) return;
                try { boundClient.Send(request, request.Length, endpoint); }
                catch (SocketException)
                {
                    outstandingT0 = 0;
                    outstandingNonce = 0;
                }
            }
        }

        private bool HasTrustedTimeSync(long localNowNanoseconds)
        {
            lock (receiverStateGate)
            {
                var leaseNanoseconds = (long)(Math.Max(5f, syncLeaseSeconds) * 1000000000.0);
                return timeSync.IsSynchronized && TimeSyncLease.IsValid(localNowNanoseconds,
                    Interlocked.Read(ref lastSuccessfulSyncAt), leaseNanoseconds);
            }
        }

        private bool IsCurrentGeneration(int generation)
        {
            return running && Volatile.Read(ref receiverGeneration) == generation;
        }

        private static UdpClient CreateBoundClient(int port)
        {
            UdpClient dualStack = null;
            try
            {
                dualStack = new UdpClient(AddressFamily.InterNetworkV6);
                dualStack.Client.DualMode = true;
                dualStack.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                return dualStack;
            }
            catch (SocketException)
            {
                if (dualStack != null) dualStack.Close();
            }
            catch (NotSupportedException)
            {
                if (dualStack != null) dualStack.Close();
            }
            catch (ArgumentException)
            {
                if (dualStack != null) dualStack.Close();
            }

            // Platforms without an IPv6 dual-stack socket remain usable on IPv4.
            var ipv4 = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                ipv4.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                return ipv4;
            }
            catch
            {
                ipv4.Close();
                throw;
            }
        }

        private void EnqueueBounded(MotionSourceFrame frame)
        {
            var queueLimit = Math.Min(4096, Math.Max(16, maximumQueuedFrames));
            while (Volatile.Read(ref queueCount) >= queueLimit)
            {
                MotionSourceFrame discarded;
                if (!frames.TryDequeue(out discarded)) break;
                Interlocked.Decrement(ref queueCount);
                Interlocked.Increment(ref droppedFrames);
            }
            frames.Enqueue(frame);
            Interlocked.Increment(ref queueCount);
        }

        private void ClearKey()
        {
            if (pairingKey == null) return;
            Array.Clear(pairingKey, 0, pairingKey.Length);
            pairingKey = null;
        }

        private static ulong RandomUInt64NonZero()
        {
            var bytes = new byte[8];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var value = BitConverter.ToUInt64(bytes, 0);
            Array.Clear(bytes, 0, bytes.Length);
            return value == 0 ? 1UL : value;
        }
    }
}
