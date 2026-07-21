package io.github.yamatro.inertiallink.motion

import android.os.SystemClock
import io.github.yamatro.inertiallink.protocol.ImuPayload
import io.github.yamatro.inertiallink.protocol.InertialLinkProtocol
import io.github.yamatro.inertiallink.protocol.PacketCodec
import io.github.yamatro.inertiallink.protocol.PacketPayload
import io.github.yamatro.inertiallink.protocol.PairingKey
import io.github.yamatro.inertiallink.protocol.SyncResponsePayload
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.SocketTimeoutException
import java.security.SecureRandom
import java.util.LinkedHashMap
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

public data class SenderStats(
    public val imuPacketsSent: Long,
    public val staleOrBackpressuredFramesDropped: Long,
    public val syncResponsesSent: Long,
    public val lastEventTimeNs: Long,
)

public interface SenderListener {
    public fun onSenderStarted(sessionId: Long) {}
    public fun onSenderStats(stats: SenderStats) {}
    public fun onCalibrationUpdate(update: CalibrationUpdate) {}
    public fun onSenderStopped() {}
    public fun onSenderError(message: String, cause: Throwable? = null) {}
}

public data class UdpSenderConfig(
    public val endpoint: UdpEndpoint,
    public val maximumFrameAgeNs: Long = 150_000_000L,
    public val statisticsIntervalNs: Long = 1_000_000_000L,
    /** Zero selects an ephemeral source port. A fixed port is useful for explicit local relays. */
    public val localPort: Int = 0,
) {
    init {
        require(maximumFrameAgeNs in 20_000_000L..2_000_000_000L) { "maximumFrameAgeNs is outside bounds" }
        require(statisticsIntervalNs in 250_000_000L..10_000_000_000L) { "statisticsIntervalNs is outside bounds" }
        require(localPort == 0 || localPort in 1_024..65_535) { "localPort must be zero or between 1024 and 65535" }
    }
}

/**
 * Bounded, authenticated UDP transport for [MotionSource].
 *
 * The queue holds at most two frames and stale frames are dropped; delayed motion cues are never
 * replayed in a burst. The connected socket accepts time-sync requests only from the configured
 * peer. Each instance is a single sender session and may be started only once; create a new instance
 * with a newly displayed pairing key after stop or failure.
 */
public class AuthenticatedUdpMotionSender(
    private val motionSource: MotionSource,
    key: PairingKey,
    private val config: UdpSenderConfig,
    private val listener: SenderListener = object : SenderListener {},
) : AutoCloseable, MotionSource.Listener {
    private val ownedKey = cloneKey(key)
    private val running = AtomicBoolean(false)
    private val closed = AtomicBoolean(false)
    private val singleStart = SingleStartGuard()
    private val sequence = SessionSequence()
    private val frames = ArrayBlockingQueue<MotionFrame>(2)
    private val sendLock = Any()
    private val packetsSent = AtomicLong(0)
    private val framesDropped = AtomicLong(0)
    private val syncResponses = AtomicLong(0)
    private val lastEventTimeNs = AtomicLong(0)
    // DatagramSocket.connect fixes the source endpoint, and sessionId is checked before this
    // per-(endpoint, session) guard is consulted.
    private val syncRequests = SyncRequestGuard(MAX_RECENT_NONCES)

    @Volatile private var socket: DatagramSocket? = null
    @Volatile private var sendThread: Thread? = null
    @Volatile private var receiveThread: Thread? = null
    @Volatile private var lastStatsTimeNs: Long = 0

    public val sessionId: Long = generateSessionId()

    @Synchronized
    public fun start() {
        check(!closed.get()) { "Sender has been closed" }
        singleStart.claim()
        check(running.compareAndSet(false, true)) { "Sender is already running" }
        val newSocket = try {
            openConnectedSocketOffCallerThread()
        } catch (error: Exception) {
            running.set(false)
            throw IOException("Unable to open UDP sender", error)
        }
        socket = newSocket
        try {
            lastStatsTimeNs = SystemClock.elapsedRealtimeNanos()
            // Start the source before transport threads. Frames are safely buffered by the bounded
            // queue, and a transport failure can no longer race a half-completed sensor start.
            motionSource.start(this)
            sendThread = Thread(::sendLoop, "InertialLink-UDP-Send").apply { start() }
            receiveThread = Thread(::receiveLoop, "InertialLink-UDP-Sync").apply { start() }
            listener.onSenderStarted(sessionId)
        } catch (error: Exception) {
            failAndStop("Unable to start motion sensors", error)
            throw error
        }
    }

    /**
     * Android rejects socket work on the main thread. Keep start() synchronous for
     * API compatibility while performing the bounded socket setup on a worker.
     */
    private fun openConnectedSocketOffCallerThread(): DatagramSocket {
        val gate = Object()
        var completed = false
        var abandoned = false
        var openedSocket: DatagramSocket? = null
        var failure: Exception? = null
        val opener = Thread({
            var candidate: DatagramSocket? = null
            try {
                candidate = DatagramSocket(config.localPort).apply {
                    broadcast = false
                    reuseAddress = false
                    soTimeout = RECEIVE_TIMEOUT_MS
                    connect(config.endpoint.address, config.endpoint.port)
                }
            } catch (error: Exception) {
                candidate?.close()
                failure = error
            }
            synchronized(gate) {
                if (abandoned) {
                    candidate?.close()
                } else {
                    openedSocket = candidate
                    completed = true
                    gate.notifyAll()
                }
            }
        }, "InertialLink-UDP-Open").apply { isDaemon = true }
        opener.start()

        val deadline = System.nanoTime() + SOCKET_OPEN_TIMEOUT_MS * 1_000_000L
        synchronized(gate) {
            while (!completed) {
                val remainingNs = deadline - System.nanoTime()
                if (remainingNs <= 0L) {
                    abandoned = true
                    opener.interrupt()
                    throw SocketTimeoutException("UDP sender setup timed out")
                }
                try {
                    gate.wait((remainingNs / 1_000_000L).coerceAtLeast(1L))
                } catch (error: InterruptedException) {
                    abandoned = true
                    opener.interrupt()
                    Thread.currentThread().interrupt()
                    throw IOException("UDP sender setup was interrupted", error)
                }
            }
        }
        failure?.let { throw it }
        return checkNotNull(openedSocket) { "UDP sender setup completed without a socket" }
    }

    override fun onMotionFrame(frame: MotionFrame) {
        if (!running.get()) return
        if (!frames.offer(frame)) {
            frames.poll()
            if (!frames.offer(frame)) framesDropped.incrementAndGet()
            framesDropped.incrementAndGet()
        }
    }

    override fun onCalibrationUpdate(update: CalibrationUpdate) {
        listener.onCalibrationUpdate(update)
    }

    override fun onSourceError(message: String, cause: Throwable?) {
        failAndStop(message, cause ?: IllegalStateException(message))
    }

    public fun requestStationaryCalibration(): Unit = motionSource.requestStationaryCalibration()

    @Synchronized
    public fun stop() {
        val wasRunning = running.getAndSet(false)
        val sourceStopError = runCatching { motionSource.stop() }.exceptionOrNull()
        socket?.close()
        socket = null
        frames.clear()
        joinUnlessCurrent(sendThread)
        joinUnlessCurrent(receiveThread)
        sendThread = null
        receiveThread = null
        if (wasRunning && sourceStopError != null) {
            runCatching { listener.onSenderError("Motion source did not stop cleanly", sourceStopError) }
        }
        if (wasRunning) runCatching { listener.onSenderStopped() }
    }

    @Synchronized
    override fun close() {
        if (closed.compareAndSet(false, true)) {
            stop()
            ownedKey.destroy()
        }
    }

    private fun sendLoop() {
        while (running.get()) {
            val frame = try {
                frames.poll(200, TimeUnit.MILLISECONDS) ?: continue
            } catch (_: InterruptedException) {
                Thread.currentThread().interrupt()
                break
            }
            val sendTimeNs = SystemClock.elapsedRealtimeNanos()
            if (sendTimeNs < frame.eventTimeNs || sendTimeNs - frame.eventTimeNs > config.maximumFrameAgeNs) {
                framesDropped.incrementAndGet()
                publishStatsIfDue(sendTimeNs)
                continue
            }
            val payload = ImuPayload(
                senderSendTimeNs = sendTimeNs,
                rawAcceleration = frame.rawAcceleration,
                angularVelocity = frame.angularVelocity,
                gravity = frame.gravity,
                linearAcceleration = frame.linearAcceleration,
                rotation = frame.rotation,
                calibrationId = frame.calibrationId,
                statusBits = frame.statusBits,
            )
            val packetSequence = try {
                sequence.take()
            } catch (error: SequenceExhaustedException) {
                failAndStop("Session sequence exhausted; create a new sender to start a fresh session", error)
                break
            }
            val datagram = try {
                PacketCodec.encodeImu(sessionId, packetSequence, frame.eventTimeNs, payload, ownedKey)
            } catch (error: RuntimeException) {
                framesDropped.incrementAndGet()
                failAndStop("A sensor frame failed protocol validation; sender stopped safely", error)
                break
            }
            try {
                send(datagram)
                packetsSent.incrementAndGet()
                lastEventTimeNs.set(frame.eventTimeNs)
                publishStatsIfDue(sendTimeNs)
            } catch (error: IOException) {
                if (running.get()) failAndStop("UDP transmission failed", error)
                break
            }
        }
    }

    private fun receiveLoop() {
        val buffer = ByteArray(InertialLinkProtocol.MAX_DATAGRAM_SIZE)
        while (running.get()) {
            val packet = DatagramPacket(buffer, buffer.size)
            val t1Ns: Long
            try {
                val activeSocket = socket ?: break
                activeSocket.receive(packet)
                t1Ns = SystemClock.elapsedRealtimeNanos()
            } catch (_: SocketTimeoutException) {
                continue
            } catch (error: IOException) {
                if (running.get()) failAndStop("UDP time-sync receiver failed", error)
                break
            }
            val datagram = packet.data.copyOfRange(packet.offset, packet.offset + packet.length)
            val decoded = runCatching { PacketCodec.decode(datagram, ownedKey) }.getOrNull() ?: continue
            if (decoded.sessionId != sessionId) continue
            val request = (decoded.payload as? PacketPayload.SyncRequest)?.value ?: continue
            if (!syncRequests.accept(decoded.sequence, decoded.eventTimeNs, request.nonce, request.t0Ns)) continue
            val t2Ns = SystemClock.elapsedRealtimeNanos()
            val responseSequence = try {
                sequence.take()
            } catch (error: SequenceExhaustedException) {
                failAndStop("Session sequence exhausted; create a new sender to start a fresh session", error)
                break
            }
            val response = PacketCodec.encodeSyncResponse(
                sessionId = sessionId,
                sequence = responseSequence,
                eventTimeNs = t1Ns,
                payload = SyncResponsePayload(request.t0Ns, t1Ns, t2Ns, request.nonce),
                key = ownedKey,
            )
            try {
                send(response)
                syncResponses.incrementAndGet()
            } catch (error: IOException) {
                if (running.get()) failAndStop("UDP time-sync response failed", error)
                break
            }
        }
    }

    private fun send(bytes: ByteArray) {
        synchronized(sendLock) {
            val activeSocket = socket ?: throw IOException("UDP socket is closed")
            activeSocket.send(DatagramPacket(bytes, bytes.size))
        }
    }

    private fun publishStatsIfDue(nowNs: Long) {
        if (nowNs - lastStatsTimeNs < config.statisticsIntervalNs) return
        lastStatsTimeNs = nowNs
        try {
            listener.onSenderStats(
                SenderStats(
                    imuPacketsSent = packetsSent.get(),
                    staleOrBackpressuredFramesDropped = framesDropped.get(),
                    syncResponsesSent = syncResponses.get(),
                    lastEventTimeNs = lastEventTimeNs.get(),
                ),
            )
        } catch (error: RuntimeException) {
            failAndStop("Sender listener failed while receiving statistics", error)
        }
    }

    private fun failAndStop(message: String, error: Throwable) {
        if (!running.getAndSet(false)) return
        runCatching { motionSource.stop() }
        socket?.close()
        socket = null
        frames.clear()
        runCatching { listener.onSenderError(message, error) }
        runCatching { listener.onSenderStopped() }
    }

    private fun joinUnlessCurrent(thread: Thread?) {
        if (thread == null || thread === Thread.currentThread()) return
        runCatching { thread.join(STOP_JOIN_TIMEOUT_MS) }
    }

    private companion object {
        const val RECEIVE_TIMEOUT_MS: Int = 250
        const val SOCKET_OPEN_TIMEOUT_MS: Long = 2_000
        const val STOP_JOIN_TIMEOUT_MS: Long = 1_000
        const val MAX_RECENT_NONCES: Int = 64
        fun generateSessionId(): Long {
            val random = SecureRandom()
            var value: Long
            do value = random.nextLong() while (value == 0L)
            return value
        }

        fun cloneKey(source: PairingKey): PairingKey {
            val bytes = source.copyBytes()
            return try {
                PairingKey.fromBytes(bytes)
            } finally {
                bytes.fill(0)
            }
        }
    }
}

internal class SequenceExhaustedException : IllegalStateException("Unsigned 32-bit sequence space is exhausted")

/** Atomically turns a sender object into a single-attempt, single-session capability. */
internal class SingleStartGuard {
    private val claimed = AtomicBoolean(false)

    fun claim() {
        check(claimed.compareAndSet(false, true)) { "Sender instances can be started only once" }
    }
}

/** Never wraps within a session; callers must create a fresh sender after exhaustion. */
internal class SessionSequence(initialValue: Long = 0L) {
    private val next = AtomicLong(initialValue)

    init {
        require(initialValue in 0L..MAX_SEQUENCE + 1L) { "Initial sequence is outside testable bounds" }
    }

    fun take(): Long {
        while (true) {
            val value = next.get()
            if (value > MAX_SEQUENCE) throw SequenceExhaustedException()
            // Saturate at MAX_SEQUENCE + 1. Exhausted calls can never advance through signed-Long
            // wrap and accidentally re-enter the unsigned-32 range.
            if (next.compareAndSet(value, value + 1L)) return value
        }
    }

    companion object {
        const val MAX_SEQUENCE: Long = 0xffff_ffffL
    }
}

/** Sliding replay window for authenticated inbound unsigned-32 sequence numbers. */
internal class ReplayWindow64 {
    private var highestSequence: Long = -1L
    private var seenBits: Long = 0L

    @Synchronized
    fun accept(sequence: Long): Boolean {
        require(sequence in 0L..SessionSequence.MAX_SEQUENCE) { "Sequence must fit unsigned 32 bits" }
        if (highestSequence < 0L) {
            highestSequence = sequence
            seenBits = 1L
            return true
        }
        if (sequence > highestSequence) {
            val advance = sequence - highestSequence
            seenBits = if (advance >= WINDOW_BITS) 1L else (seenBits shl advance.toInt()) or 1L
            highestSequence = sequence
            return true
        }
        val distance = highestSequence - sequence
        if (distance >= WINDOW_BITS) return false
        val bit = 1L shl distance.toInt()
        if (seenBits and bit != 0L) return false
        seenBits = seenBits or bit
        return true
    }

    private companion object {
        const val WINDOW_BITS: Long = 64L
    }
}

/** Bounded nonce/t0 correlation guard used in addition to the sequence replay window. */
internal class SyncNonceCache(private val capacity: Int = 64) {
    private val entries = LinkedHashMap<Long, Long>()

    init {
        require(capacity in 1..1_024) { "Nonce cache capacity is outside bounds" }
    }

    @Synchronized
    fun accept(nonce: Long, t0Ns: Long): Boolean {
        if (t0Ns <= 0L || entries.containsKey(nonce)) return false
        entries[nonce] = t0Ns
        while (entries.size > capacity) {
            val iterator = entries.iterator()
            iterator.next()
            iterator.remove()
        }
        return true
    }
}

/** One bounded inbound guard per already-authenticated source endpoint and sender session. */
internal class SyncRequestGuard(nonceCapacity: Int = 64) {
    private val replayWindow = ReplayWindow64()
    private val nonces = SyncNonceCache(nonceCapacity)

    @Synchronized
    fun accept(sequence: Long, eventTimeNs: Long, nonce: Long, t0Ns: Long): Boolean {
        // Both timestamps originate in the receiver's clock and are the same send event. Check
        // consistency before consuming replay state so malformed packets cannot advance it.
        if (eventTimeNs <= 0L || eventTimeNs != t0Ns) return false
        if (!replayWindow.accept(sequence)) return false
        return nonces.accept(nonce, t0Ns)
    }
}
