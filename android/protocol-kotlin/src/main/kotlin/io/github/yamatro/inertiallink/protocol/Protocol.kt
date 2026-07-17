package io.github.yamatro.inertiallink.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import kotlin.math.sqrt

/** Constants for the bounded InertialLink XR v1 UDP wire format. */
public object InertialLinkProtocol {
    public const val MAJOR_VERSION: Int = 1
    public const val MINOR_VERSION: Int = 0
    public const val HEADER_SIZE: Int = 32
    public const val AUTH_TAG_SIZE: Int = 16
    public const val IMU_PAYLOAD_SIZE: Int = 80
    public const val SYNC_REQUEST_PAYLOAD_SIZE: Int = 16
    public const val SYNC_RESPONSE_PAYLOAD_SIZE: Int = 32
    public const val IMU_PACKET_SIZE: Int = HEADER_SIZE + IMU_PAYLOAD_SIZE + AUTH_TAG_SIZE
    public const val MAX_DATAGRAM_SIZE: Int = 512
    public const val DEFAULT_PORT: Int = 28_461

    public const val TYPE_IMU: Int = 1
    public const val TYPE_SYNC_REQUEST: Int = 2
    public const val TYPE_SYNC_RESPONSE: Int = 3
    public const val FLAG_AUTHENTICATED: Int = 1

    internal val magic: ByteArray = byteArrayOf('I'.code.toByte(), 'L'.code.toByte(), 'X'.code.toByte(), 'R'.code.toByte())
}

/** Bit assignments carried in [ImuPayload.statusBits]. */
public object ImuStatus {
    public const val RAW_ACCEL_VALID: Long = 1L shl 0
    public const val GYROSCOPE_VALID: Long = 1L shl 1
    public const val GRAVITY_VALID: Long = 1L shl 2
    public const val LINEAR_ACCEL_VALID: Long = 1L shl 3
    public const val ROTATION_VALID: Long = 1L shl 4
    public const val CALIBRATED: Long = 1L shl 5
    public const val CALIBRATING: Long = 1L shl 6
    public const val SENSOR_ACCURACY_LOW: Long = 1L shl 8
    public const val SENSOR_ACCURACY_MEDIUM: Long = 1L shl 9
    public const val SENSOR_ACCURACY_HIGH: Long = 1L shl 10

    internal const val ACCURACY_MASK: Long =
        SENSOR_ACCURACY_LOW or SENSOR_ACCURACY_MEDIUM or SENSOR_ACCURACY_HIGH
    internal const val ALLOWED_MASK: Long = 0x77fL
}

public data class Vector3f(public val x: Float, public val y: Float, public val z: Float)

public data class Quaternionf(public val x: Float, public val y: Float, public val z: Float, public val w: Float)

public data class ImuPayload(
    public val senderSendTimeNs: Long,
    public val rawAcceleration: Vector3f,
    public val angularVelocity: Vector3f,
    public val gravity: Vector3f,
    public val linearAcceleration: Vector3f,
    public val rotation: Quaternionf,
    /** Unsigned 32-bit value represented by a [Long]. */
    public val calibrationId: Long,
    /** Unsigned 32-bit value represented by a [Long]. */
    public val statusBits: Long,
)

public data class SyncRequestPayload(public val t0Ns: Long, public val nonce: Long)

public data class SyncResponsePayload(
    public val t0Ns: Long,
    public val t1Ns: Long,
    public val t2Ns: Long,
    public val nonce: Long,
)

public sealed interface PacketPayload {
    public data class Imu(public val value: ImuPayload) : PacketPayload
    public data class SyncRequest(public val value: SyncRequestPayload) : PacketPayload
    public data class SyncResponse(public val value: SyncResponsePayload) : PacketPayload
}

public data class DecodedPacket(
    public val majorVersion: Int,
    public val minorVersion: Int,
    /** Unsigned 32-bit value represented by a [Long]. */
    public val sequence: Long,
    /** Unsigned 64-bit session identifier represented by its signed [Long] bit pattern. */
    public val sessionId: Long,
    public val eventTimeNs: Long,
    public val payload: PacketPayload,
)

/** Raised when an untrusted datagram is malformed, unauthenticated, or outside safety bounds. */
public class ProtocolException(message: String) : IllegalArgumentException(message)

/**
 * A process-ephemeral 128-bit pairing key. The secret is never returned from [toString].
 * Call [destroy] when the owner is finished with it.
 */
public class PairingKey private constructor(bytes: ByteArray) : AutoCloseable {
    private val value: ByteArray = bytes.copyOf()
    @Volatile private var destroyed: Boolean = false

    public fun copyBytes(): ByteArray = synchronized(value) {
        check(!destroyed) { "Pairing key has been destroyed" }
        value.copyOf()
    }

    public fun toHex(): String = copyBytes().let { bytes ->
        try {
            bytes.joinToString(separator = "") { byte -> "%02X".format(byte.toInt() and 0xff) }
        } finally {
            bytes.fill(0)
        }
    }

    public fun toDisplayString(): String = toHex().chunked(4).joinToString("-")

    public fun destroy(): Unit = synchronized(value) {
        value.fill(0)
        destroyed = true
    }

    override fun close(): Unit = destroy()

    override fun toString(): String = "PairingKey([redacted])"

    public companion object {
        public const val SIZE_BYTES: Int = 16

        @JvmStatic
        public fun generate(random: SecureRandom = SecureRandom()): PairingKey {
            val bytes = ByteArray(SIZE_BYTES)
            random.nextBytes(bytes)
            return try {
                PairingKey(bytes)
            } finally {
                bytes.fill(0)
            }
        }

        @JvmStatic
        public fun fromBytes(bytes: ByteArray): PairingKey {
            if (bytes.size != SIZE_BYTES) {
                throw IllegalArgumentException("Pairing key must contain exactly $SIZE_BYTES bytes")
            }
            return PairingKey(bytes)
        }

        /** Parses 32 hexadecimal characters; ASCII spaces and hyphens are ignored. */
        @JvmStatic
        public fun parse(text: String): PairingKey {
            val compact = buildString(text.length) {
                text.forEach { character ->
                    when (character) {
                        '-', ' ', '\t', '\r', '\n' -> Unit
                        else -> append(character)
                    }
                }
            }
            if (compact.length != SIZE_BYTES * 2 || compact.any { !it.isAsciiHexDigit() }) {
                throw IllegalArgumentException("Pairing key must contain exactly 32 hexadecimal characters")
            }
            val bytes = ByteArray(SIZE_BYTES) { index -> compact.substring(index * 2, index * 2 + 2).toInt(16).toByte() }
            return try {
                PairingKey(bytes)
            } finally {
                bytes.fill(0)
            }
        }

        private fun Char.isAsciiHexDigit(): Boolean =
            this in '0'..'9' || this in 'A'..'F' || this in 'a'..'f'
    }
}

/** Stateless network-byte-order encoder and authenticated decoder. */
public object PacketCodec {
    private const val MAX_UNSIGNED_INT: Long = 0xffff_ffffL
    private const val HMAC_ALGORITHM: String = "HmacSHA256"

    @JvmStatic
    public fun encodeImu(
        sessionId: Long,
        sequence: Long,
        eventTimeNs: Long,
        payload: ImuPayload,
        key: PairingKey,
    ): ByteArray {
        validateCommon(sessionId, sequence, eventTimeNs)
        validateImu(payload)
        if (payload.senderSendTimeNs < eventTimeNs) {
            throw ProtocolException("senderSendTimeNs must not precede eventTimeNs")
        }
        return encode(InertialLinkProtocol.TYPE_IMU, sessionId, sequence, eventTimeNs, key) { buffer ->
            buffer.putLong(payload.senderSendTimeNs)
            buffer.putVector(payload.rawAcceleration)
            buffer.putVector(payload.angularVelocity)
            buffer.putVector(payload.gravity)
            buffer.putVector(payload.linearAcceleration)
            buffer.putFloat(payload.rotation.x)
            buffer.putFloat(payload.rotation.y)
            buffer.putFloat(payload.rotation.z)
            buffer.putFloat(payload.rotation.w)
            buffer.putInt(payload.calibrationId.toInt())
            buffer.putInt(payload.statusBits.toInt())
        }
    }

    @JvmStatic
    public fun encodeSyncRequest(
        sessionId: Long,
        sequence: Long,
        eventTimeNs: Long,
        payload: SyncRequestPayload,
        key: PairingKey,
    ): ByteArray {
        validateCommon(sessionId, sequence, eventTimeNs)
        requireTimestamp("t0Ns", payload.t0Ns)
        if (payload.t0Ns != eventTimeNs) {
            throw ProtocolException("Sync request eventTimeNs must equal t0Ns")
        }
        return encode(InertialLinkProtocol.TYPE_SYNC_REQUEST, sessionId, sequence, eventTimeNs, key) { buffer ->
            buffer.putLong(payload.t0Ns)
            buffer.putLong(payload.nonce)
        }
    }

    @JvmStatic
    public fun encodeSyncResponse(
        sessionId: Long,
        sequence: Long,
        eventTimeNs: Long,
        payload: SyncResponsePayload,
        key: PairingKey,
    ): ByteArray {
        validateCommon(sessionId, sequence, eventTimeNs)
        requireTimestamp("t0Ns", payload.t0Ns)
        requireTimestamp("t1Ns", payload.t1Ns)
        requireTimestamp("t2Ns", payload.t2Ns)
        if (payload.t2Ns < payload.t1Ns) throw ProtocolException("t2Ns must not precede t1Ns")
        return encode(InertialLinkProtocol.TYPE_SYNC_RESPONSE, sessionId, sequence, eventTimeNs, key) { buffer ->
            buffer.putLong(payload.t0Ns)
            buffer.putLong(payload.t1Ns)
            buffer.putLong(payload.t2Ns)
            buffer.putLong(payload.nonce)
        }
    }

    @JvmStatic
    public fun decode(datagram: ByteArray, key: PairingKey): DecodedPacket {
        if (datagram.size < InertialLinkProtocol.HEADER_SIZE + InertialLinkProtocol.AUTH_TAG_SIZE ||
            datagram.size > InertialLinkProtocol.MAX_DATAGRAM_SIZE
        ) {
            throw ProtocolException("Datagram length is outside protocol bounds")
        }

        val buffer = ByteBuffer.wrap(datagram).order(ByteOrder.BIG_ENDIAN)
        InertialLinkProtocol.magic.forEach { expected ->
            if (buffer.get() != expected) throw ProtocolException("Invalid packet magic")
        }
        val major = buffer.get().toInt() and 0xff
        val minor = buffer.get().toInt() and 0xff
        if (major != InertialLinkProtocol.MAJOR_VERSION || minor != InertialLinkProtocol.MINOR_VERSION) {
            throw ProtocolException("Unsupported protocol version $major.$minor")
        }
        val type = buffer.get().toInt() and 0xff
        val flags = buffer.get().toInt() and 0xff
        if (flags != InertialLinkProtocol.FLAG_AUTHENTICATED) {
            throw ProtocolException("Packet must be authenticated and contain no unknown flags")
        }
        val headerLength = buffer.short.toInt() and 0xffff
        val payloadLength = buffer.short.toInt() and 0xffff
        if (headerLength != InertialLinkProtocol.HEADER_SIZE) throw ProtocolException("Invalid header length")
        val requiredPayloadLength = payloadLengthForType(type)
        if (payloadLength != requiredPayloadLength) throw ProtocolException("Invalid payload length for packet type")
        val expectedLength = headerLength + payloadLength + InertialLinkProtocol.AUTH_TAG_SIZE
        if (datagram.size != expectedLength) throw ProtocolException("Datagram has trailing or missing bytes")

        val sequence = buffer.int.toLong() and MAX_UNSIGNED_INT
        val sessionId = buffer.long
        val eventTimeNs = buffer.long
        validateCommon(sessionId, sequence, eventTimeNs)

        val bodyLength = headerLength + payloadLength
        val calculatedTag = hmac(datagram, bodyLength, key)
        val receivedTag = datagram.copyOfRange(bodyLength, expectedLength)
        if (!MessageDigest.isEqual(calculatedTag, receivedTag)) throw ProtocolException("Packet authentication failed")

        val payload = when (type) {
            InertialLinkProtocol.TYPE_IMU -> {
                val imu = readImu(buffer)
                if (imu.senderSendTimeNs < eventTimeNs) {
                    throw ProtocolException("senderSendTimeNs must not precede eventTimeNs")
                }
                PacketPayload.Imu(imu)
            }
            InertialLinkProtocol.TYPE_SYNC_REQUEST -> {
                val request = SyncRequestPayload(buffer.long, buffer.long)
                requireTimestamp("t0Ns", request.t0Ns)
                if (request.t0Ns != eventTimeNs) {
                    throw ProtocolException("Sync request eventTimeNs must equal t0Ns")
                }
                PacketPayload.SyncRequest(request)
            }
            InertialLinkProtocol.TYPE_SYNC_RESPONSE -> {
                val response = SyncResponsePayload(buffer.long, buffer.long, buffer.long, buffer.long)
                requireTimestamp("t0Ns", response.t0Ns)
                requireTimestamp("t1Ns", response.t1Ns)
                requireTimestamp("t2Ns", response.t2Ns)
                if (response.t2Ns < response.t1Ns) throw ProtocolException("t2Ns must not precede t1Ns")
                PacketPayload.SyncResponse(response)
            }
            else -> throw ProtocolException("Unknown packet type")
        }
        return DecodedPacket(major, minor, sequence, sessionId, eventTimeNs, payload)
    }

    private inline fun encode(
        type: Int,
        sessionId: Long,
        sequence: Long,
        eventTimeNs: Long,
        key: PairingKey,
        writePayload: (ByteBuffer) -> Unit,
    ): ByteArray {
        val payloadLength = payloadLengthForType(type)
        val bodyLength = InertialLinkProtocol.HEADER_SIZE + payloadLength
        val packet = ByteArray(bodyLength + InertialLinkProtocol.AUTH_TAG_SIZE)
        val buffer = ByteBuffer.wrap(packet).order(ByteOrder.BIG_ENDIAN)
        buffer.put(InertialLinkProtocol.magic)
        buffer.put(InertialLinkProtocol.MAJOR_VERSION.toByte())
        buffer.put(InertialLinkProtocol.MINOR_VERSION.toByte())
        buffer.put(type.toByte())
        buffer.put(InertialLinkProtocol.FLAG_AUTHENTICATED.toByte())
        buffer.putShort(InertialLinkProtocol.HEADER_SIZE.toShort())
        buffer.putShort(payloadLength.toShort())
        buffer.putInt(sequence.toInt())
        buffer.putLong(sessionId)
        buffer.putLong(eventTimeNs)
        writePayload(buffer)
        check(buffer.position() == bodyLength) { "Internal payload length mismatch" }
        val tag = hmac(packet, bodyLength, key)
        System.arraycopy(tag, 0, packet, bodyLength, InertialLinkProtocol.AUTH_TAG_SIZE)
        return packet
    }

    private fun readImu(buffer: ByteBuffer): ImuPayload {
        val payload = ImuPayload(
            senderSendTimeNs = buffer.long,
            rawAcceleration = buffer.getVector(),
            angularVelocity = buffer.getVector(),
            gravity = buffer.getVector(),
            linearAcceleration = buffer.getVector(),
            rotation = Quaternionf(buffer.float, buffer.float, buffer.float, buffer.float),
            calibrationId = buffer.int.toLong() and MAX_UNSIGNED_INT,
            statusBits = buffer.int.toLong() and MAX_UNSIGNED_INT,
        )
        validateImu(payload)
        return payload
    }

    private fun validateCommon(sessionId: Long, sequence: Long, eventTimeNs: Long) {
        if (sessionId == 0L) throw ProtocolException("Session identifier must be non-zero")
        requireUnsignedInt("sequence", sequence)
        requireTimestamp("eventTimeNs", eventTimeNs)
    }

    private fun validateImu(payload: ImuPayload) {
        requireTimestamp("senderSendTimeNs", payload.senderSendTimeNs)
        requireVector("rawAcceleration", payload.rawAcceleration, 200f)
        requireVector("angularVelocity", payload.angularVelocity, 50f)
        requireVector("gravity", payload.gravity, 30f)
        requireVector("linearAcceleration", payload.linearAcceleration, 200f)
        val quaternion = payload.rotation
        requireFinite("rotation.x", quaternion.x, 1.5f)
        requireFinite("rotation.y", quaternion.y, 1.5f)
        requireFinite("rotation.z", quaternion.z, 1.5f)
        requireFinite("rotation.w", quaternion.w, 1.5f)
        val norm = sqrt(
            quaternion.x * quaternion.x + quaternion.y * quaternion.y +
                quaternion.z * quaternion.z + quaternion.w * quaternion.w,
        )
        if (norm !in 0.5f..1.5f) throw ProtocolException("rotation quaternion norm is outside bounds")
        requireUnsignedInt("calibrationId", payload.calibrationId)
        requireUnsignedInt("statusBits", payload.statusBits)
        if (payload.statusBits and ImuStatus.ALLOWED_MASK.inv() != 0L) {
            throw ProtocolException("statusBits contains reserved bits")
        }
        val accuracy = payload.statusBits and ImuStatus.ACCURACY_MASK
        if (accuracy != 0L && accuracy and (accuracy - 1L) != 0L) {
            throw ProtocolException("statusBits contains multiple sensor accuracy levels")
        }
    }

    private fun requireVector(name: String, value: Vector3f, maximumMagnitudePerAxis: Float) {
        requireFinite("$name.x", value.x, maximumMagnitudePerAxis)
        requireFinite("$name.y", value.y, maximumMagnitudePerAxis)
        requireFinite("$name.z", value.z, maximumMagnitudePerAxis)
    }

    private fun requireFinite(name: String, value: Float, maximumMagnitude: Float) {
        if (!value.isFinite() || value !in -maximumMagnitude..maximumMagnitude) {
            throw ProtocolException("$name is non-finite or outside bounds")
        }
    }

    private fun requireTimestamp(name: String, value: Long) {
        if (value <= 0L) throw ProtocolException("$name must be a positive monotonic timestamp")
    }

    private fun requireUnsignedInt(name: String, value: Long) {
        if (value !in 0L..MAX_UNSIGNED_INT) throw ProtocolException("$name must fit an unsigned 32-bit integer")
    }

    private fun payloadLengthForType(type: Int): Int = when (type) {
        InertialLinkProtocol.TYPE_IMU -> InertialLinkProtocol.IMU_PAYLOAD_SIZE
        InertialLinkProtocol.TYPE_SYNC_REQUEST -> InertialLinkProtocol.SYNC_REQUEST_PAYLOAD_SIZE
        InertialLinkProtocol.TYPE_SYNC_RESPONSE -> InertialLinkProtocol.SYNC_RESPONSE_PAYLOAD_SIZE
        else -> throw ProtocolException("Unknown packet type")
    }

    private fun hmac(bytes: ByteArray, length: Int, key: PairingKey): ByteArray {
        val secret = key.copyBytes()
        return try {
            val mac = Mac.getInstance(HMAC_ALGORITHM)
            mac.init(SecretKeySpec(secret, HMAC_ALGORITHM))
            mac.update(bytes, 0, length)
            mac.doFinal().copyOf(InertialLinkProtocol.AUTH_TAG_SIZE)
        } finally {
            secret.fill(0)
        }
    }

    private fun ByteBuffer.putVector(vector: Vector3f) {
        putFloat(vector.x)
        putFloat(vector.y)
        putFloat(vector.z)
    }

    private fun ByteBuffer.getVector(): Vector3f = Vector3f(float, float, float)
}
