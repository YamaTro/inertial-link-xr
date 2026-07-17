package io.github.yamatro.inertiallink.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

class PacketCodecTest {
    private val key = PairingKey.parse("00010203-04050607 08090A0B0C0D0E0F")

    @Test
    fun `IMU packet round trips with exact bounded size`() {
        val encoded = PacketCodec.encodeImu(SESSION_ID, SEQUENCE, EVENT_TIME, sample(), key)

        assertEquals(InertialLinkProtocol.IMU_PACKET_SIZE, encoded.size)
        assertContentEquals(byteArrayOf(0x49, 0x4c, 0x58, 0x52), encoded.copyOfRange(0, 4))
        val decoded = PacketCodec.decode(encoded, key)
        assertEquals(SEQUENCE, decoded.sequence)
        assertEquals(SESSION_ID, decoded.sessionId)
        assertEquals(EVENT_TIME, decoded.eventTimeNs)
        assertEquals(sample(), assertIs<PacketPayload.Imu>(decoded.payload).value)
    }

    @Test
    fun `IMU encoding matches the cross-language golden vector`() {
        val goldenPayload = ImuPayload(
            senderSendTimeNs = 1_000_500_000L,
            rawAcceleration = Vector3f(1f, 2f, 3f),
            angularVelocity = Vector3f(0.1f, 0.2f, 0.3f),
            gravity = Vector3f(0f, -9.80665f, 0f),
            linearAcceleration = Vector3f(1.1f, 2.2f, 3.3f),
            rotation = Quaternionf(0f, 0f, 0f, 1f),
            calibrationId = 0xaabbccddL,
            statusBits = 0x0000041fL,
        )

        val actual = PacketCodec.encodeImu(
            sessionId = SESSION_ID,
            sequence = 0x11223344L,
            eventTimeNs = EVENT_TIME,
            payload = goldenPayload,
            key = key,
        )

        assertEquals(GOLDEN_IMU_HEX, actual.toHex())
        assertEquals(goldenPayload, assertIs<PacketPayload.Imu>(PacketCodec.decode(actual, key).payload).value)
    }

    @Test
    fun `authentication rejects a one-bit payload modification`() {
        val encoded = PacketCodec.encodeImu(SESSION_ID, SEQUENCE, EVENT_TIME, sample(), key)
        encoded[48] = (encoded[48].toInt() xor 1).toByte()

        assertFailsWith<ProtocolException> { PacketCodec.decode(encoded, key) }
    }

    @Test
    fun `authentication rejects the wrong pairing key`() {
        val encoded = PacketCodec.encodeImu(SESSION_ID, SEQUENCE, EVENT_TIME, sample(), key)
        val other = PairingKey.parse("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")

        assertFailsWith<ProtocolException> { PacketCodec.decode(encoded, other) }
    }

    @Test
    fun `decoder rejects trailing bytes and forged length`() {
        val encoded = PacketCodec.encodeImu(SESSION_ID, SEQUENCE, EVENT_TIME, sample(), key)

        assertFailsWith<ProtocolException> { PacketCodec.decode(encoded + byteArrayOf(0), key) }
        encoded[11] = 79
        assertFailsWith<ProtocolException> { PacketCodec.decode(encoded, key) }
    }

    @Test
    fun `encoder rejects non-finite sensor values before networking`() {
        val invalid = sample().copy(rawAcceleration = Vector3f(Float.NaN, 0f, 0f))

        assertFailsWith<ProtocolException> {
            PacketCodec.encodeImu(SESSION_ID, SEQUENCE, EVENT_TIME, invalid, key)
        }
    }

    @Test
    fun `encoder enforces vector bounds and timestamp ordering`() {
        assertFailsWith<ProtocolException> {
            PacketCodec.encodeImu(
                SESSION_ID,
                SEQUENCE,
                EVENT_TIME,
                sample().copy(rawAcceleration = Vector3f(200.01f, 0f, 0f)),
                key,
            )
        }
        assertFailsWith<ProtocolException> {
            PacketCodec.encodeImu(
                SESSION_ID,
                SEQUENCE,
                EVENT_TIME,
                sample().copy(senderSendTimeNs = EVENT_TIME - 1),
                key,
            )
        }
        assertFailsWith<ProtocolException> {
            PacketCodec.encodeSyncRequest(SESSION_ID, 0, 0, SyncRequestPayload(1, 1), key)
        }
    }

    @Test
    fun `encoder accepts documented physical boundaries`() {
        val boundary = sample().copy(
            rawAcceleration = Vector3f(-200f, 200f, 0f),
            angularVelocity = Vector3f(-50f, 50f, 0f),
            gravity = Vector3f(-30f, 30f, 0f),
            linearAcceleration = Vector3f(-200f, 200f, 0f),
            rotation = Quaternionf(0.5f, 0.5f, 0.5f, 0.5f),
        )

        assertEquals(InertialLinkProtocol.IMU_PACKET_SIZE, PacketCodec.encodeImu(SESSION_ID, 0, EVENT_TIME, boundary, key).size)
    }

    @Test
    fun `encoder rejects reserved and contradictory status bits`() {
        assertFailsWith<ProtocolException> {
            PacketCodec.encodeImu(SESSION_ID, 0, EVENT_TIME, sample().copy(statusBits = 1L shl 7), key)
        }
        assertFailsWith<ProtocolException> {
            PacketCodec.encodeImu(
                SESSION_ID,
                0,
                EVENT_TIME,
                sample().copy(statusBits = ImuStatus.SENSOR_ACCURACY_LOW or ImuStatus.SENSOR_ACCURACY_HIGH),
                key,
            )
        }
    }

    @Test
    fun `sync request and response round trip and preserve nonce`() {
        val request = SyncRequestPayload(EVENT_TIME, 0x1122334455667788L)
        val requestPacket = PacketCodec.encodeSyncRequest(SESSION_ID, 1, EVENT_TIME, request, key)
        assertEquals(64, requestPacket.size)
        assertEquals(request, assertIs<PacketPayload.SyncRequest>(PacketCodec.decode(requestPacket, key).payload).value)

        val response = SyncResponsePayload(EVENT_TIME, EVENT_TIME + 40, EVENT_TIME + 60, request.nonce)
        val responsePacket = PacketCodec.encodeSyncResponse(SESSION_ID, 2, EVENT_TIME + 40, response, key)
        assertEquals(80, responsePacket.size)
        assertEquals(response, assertIs<PacketPayload.SyncResponse>(PacketCodec.decode(responsePacket, key).payload).value)
    }

    @Test
    fun `sync request requires header event time to equal payload t0 on encode and decode`() {
        assertFailsWith<ProtocolException> {
            PacketCodec.encodeSyncRequest(
                SESSION_ID,
                1,
                EVENT_TIME,
                SyncRequestPayload(EVENT_TIME + 1, 9),
                key,
            )
        }

        val authenticatedMismatch = PacketCodec.encodeSyncRequest(
            SESSION_ID,
            1,
            EVENT_TIME,
            SyncRequestPayload(EVENT_TIME, 9),
            key,
        )
        ByteBuffer.wrap(authenticatedMismatch).order(ByteOrder.BIG_ENDIAN)
            .putLong(InertialLinkProtocol.HEADER_SIZE, EVENT_TIME + 1)
        resign(authenticatedMismatch)

        assertFailsWith<ProtocolException> { PacketCodec.decode(authenticatedMismatch, key) }
    }

    @Test
    fun `sync packets match the cross-language golden vectors`() {
        val session = 0x8877665544332211uL.toLong()
        val nonce = 0x1020304050607080L
        val request = PacketCodec.encodeSyncRequest(
            sessionId = session,
            sequence = 1,
            eventTimeNs = 2_000_000_000L,
            payload = SyncRequestPayload(2_000_000_000L, nonce),
            key = key,
        )
        assertEquals(GOLDEN_SYNC_REQUEST_HEX, request.toHex())

        val response = PacketCodec.encodeSyncResponse(
            sessionId = session,
            sequence = 2,
            eventTimeNs = 2_000_050_000L,
            payload = SyncResponsePayload(
                t0Ns = 2_000_000_000L,
                t1Ns = 2_000_050_000L,
                t2Ns = 2_000_075_000L,
                nonce = nonce,
            ),
            key = key,
        )
        assertEquals(GOLDEN_SYNC_RESPONSE_HEX, response.toHex())
    }

    @Test
    fun `pairing key formatting is stable and toString never exposes the secret`() {
        assertEquals("000102030405060708090A0B0C0D0E0F", key.toHex())
        assertEquals("0001-0203-0405-0607-0809-0A0B-0C0D-0E0F", key.toDisplayString())
        assertNotEquals(key.toHex(), key.toString())
        assertTrue(key.toString().contains("redacted"))
    }

    @Test
    fun `pairing key parser rejects ambiguous or incorrectly sized input`() {
        assertFailsWith<IllegalArgumentException> { PairingKey.parse("1234") }
        assertFailsWith<IllegalArgumentException> { PairingKey.parse("GG0102030405060708090A0B0C0D0E0F") }
        assertFailsWith<IllegalArgumentException> { PairingKey.parse("0001:02030405060708090A0B0C0D0E0F") }
        assertFailsWith<IllegalArgumentException> { PairingKey.parse("0001\u00A002030405060708090A0B0C0D0E0F") }
        assertFailsWith<IllegalArgumentException> { PairingKey.parse("\uFF1000102030405060708090A0B0C0D0E0F") }
        assertFailsWith<IllegalArgumentException> { PairingKey.parse("\uFF2100102030405060708090A0B0C0D0E0F") }
    }

    @Test
    fun `destroyed pairing key cannot be reused or displayed`() {
        val disposable = PairingKey.generate()
        disposable.destroy()

        assertFailsWith<IllegalStateException> { disposable.copyBytes() }
        assertFailsWith<IllegalStateException> { disposable.toHex() }
    }

    private fun sample(): ImuPayload = ImuPayload(
        senderSendTimeNs = EVENT_TIME + 123,
        rawAcceleration = Vector3f(1f, -2f, 3.5f),
        angularVelocity = Vector3f(0.1f, -0.2f, 0.3f),
        gravity = Vector3f(0f, 9.80665f, 0f),
        linearAcceleration = Vector3f(-0.5f, 0.25f, 1.25f),
        rotation = Quaternionf(0f, 0f, 0f, 1f),
        calibrationId = 7,
        statusBits = 0x37,
    )

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private fun resign(packet: ByteArray) {
        val bodyLength = packet.size - InertialLinkProtocol.AUTH_TAG_SIZE
        val secret = key.copyBytes()
        try {
            val mac = Mac.getInstance("HmacSHA256")
            mac.init(SecretKeySpec(secret, "HmacSHA256"))
            val tag = mac.doFinal(packet.copyOf(bodyLength))
            System.arraycopy(tag, 0, packet, bodyLength, InertialLinkProtocol.AUTH_TAG_SIZE)
        } finally {
            secret.fill(0)
        }
    }

    private companion object {
        const val SESSION_ID: Long = 0x0102030405060708L
        const val SEQUENCE: Long = 0x0a0b0c0dL
        const val EVENT_TIME: Long = 1_000_000_000L
        const val GOLDEN_IMU_HEX: String =
            "494c58520100010100200050112233440102030405060708000000003b9aca00" +
                "000000003ba26b203f80000040000000404000003dcccccd3e4ccccd3e99999a" +
                "00000000c11ce80a000000003f8ccccd400ccccd405333330000000000000000" +
                "000000003f800000aabbccdd0000041fc469443bfeaa907111df804297ea6214"
        const val GOLDEN_SYNC_REQUEST_HEX: String =
            "494c585201000201002000100000000188776655443322110000000077359400" +
                "00000000773594001020304050607080a644347726832b2f8f879f74b9bf6a41"
        const val GOLDEN_SYNC_RESPONSE_HEX: String =
            "494c585201000301002000200000000288776655443322110000000077365750" +
                "00000000773594000000000077365750000000007736b8f81020304050607080" +
                "ccdad3a1c13f90a19a5b8c1cdf2e3baf"
    }
}
