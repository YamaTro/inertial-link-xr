package io.github.yamatro.inertiallink.motion

import io.github.yamatro.inertiallink.protocol.InertialLinkProtocol
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class UdpEndpointTest {
    @Test
    fun `local policy accepts private numeric IPv4 without DNS`() {
        val endpoint = UdpEndpoint.parse("192.168.10.42")

        assertEquals("192.168.10.42", endpoint.address.hostAddress)
        assertEquals(InertialLinkProtocol.DEFAULT_PORT, endpoint.port)
        assertEquals("fc00:0:0:0:0:0:0:42", UdpEndpoint.parse("fc00::42").address.hostAddress)
        assertEquals("100.64.0.1", UdpEndpoint.parse("100.64.0.1").address.hostAddress)
        assertEquals("100.127.255.254", UdpEndpoint.parse("100.127.255.254").address.hostAddress)
    }

    @Test
    fun `local policy rejects public IP hostname broadcast and malformed port`() {
        assertFailsWith<IllegalArgumentException> { UdpEndpoint.parse("8.8.8.8") }
        assertFailsWith<IllegalArgumentException> { UdpEndpoint.parse("receiver.local") }
        assertFailsWith<IllegalArgumentException> { UdpEndpoint.parse("１２７.０.０.１") }
        assertFailsWith<IllegalArgumentException> { UdpEndpoint.parse("255.255.255.255") }
        assertFailsWith<IllegalArgumentException> { UdpEndpoint.parse("fe80::1") }
        assertFailsWith<IllegalArgumentException> { UdpEndpoint.parse("127.0.0.1", 0) }
        assertFailsWith<IllegalArgumentException> { UdpEndpoint.parse("127.0.0.1", 1_023) }
        assertFailsWith<IllegalArgumentException> { UdpEndpoint.parse("100.63.255.254") }
        assertFailsWith<IllegalArgumentException> { UdpEndpoint.parse("100.128.0.1") }
    }

    @Test
    fun `advanced policy is explicit and remains unicast only`() {
        val endpoint = UdpEndpoint.parse("8.8.8.8", 28_461, AddressPolicy.ANY_UNICAST)
        assertEquals("8.8.8.8", endpoint.address.hostAddress)
        assertFailsWith<IllegalArgumentException> {
            UdpEndpoint.parse("224.0.0.1", 28_461, AddressPolicy.ANY_UNICAST)
        }
    }
}
