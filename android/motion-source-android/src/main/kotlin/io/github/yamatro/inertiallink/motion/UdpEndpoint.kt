package io.github.yamatro.inertiallink.motion

import io.github.yamatro.inertiallink.protocol.InertialLinkProtocol
import java.net.Inet4Address
import java.net.Inet6Address
import java.net.InetAddress

public enum class AddressPolicy {
    /** Loopback, IPv4 link-local/RFC1918, and unique-local IPv6 only. */
    LOCAL_ONLY,
    /** Explicit integration opt-in. Still rejects unspecified, broadcast, and multicast destinations. */
    ANY_UNICAST,
}

public data class UdpEndpoint(
    public val address: InetAddress,
    public val port: Int = InertialLinkProtocol.DEFAULT_PORT,
) {
    init {
        require(port in 1_024..65_535) { "UDP port must be between 1024 and 65535" }
        require(!address.isAnyLocalAddress) { "Unspecified receiver address is not allowed" }
        require(!address.isMulticastAddress) { "Multicast receiver address is not allowed" }
        require(address !is Inet6Address || !address.isLinkLocalAddress) {
            "IPv6 link-local receivers require a scope identifier and are not supported"
        }
        if (address is Inet4Address) {
            val bytes = address.address.map { it.toInt() and 0xff }
            require(bytes != listOf(255, 255, 255, 255)) { "Broadcast receiver address is not allowed" }
        }
    }

    public companion object {
        /** Parses a numeric IP literal without resolving hostnames. */
        @JvmStatic
        @JvmOverloads
        public fun parse(
            ipLiteral: String,
            port: Int = InertialLinkProtocol.DEFAULT_PORT,
            policy: AddressPolicy = AddressPolicy.LOCAL_ONLY,
        ): UdpEndpoint {
            val address = parseNumericAddress(ipLiteral.trim())
            val endpoint = UdpEndpoint(address, port)
            if (policy == AddressPolicy.LOCAL_ONLY && !address.isLocalUnicast()) {
                throw IllegalArgumentException("Receiver must be a local/private unicast IP address")
            }
            return endpoint
        }

        private fun parseNumericAddress(value: String): InetAddress {
            if (value.isEmpty()) throw IllegalArgumentException("Receiver IP address is required")
            if (':' in value) {
                // A colon forces literal parsing. Restricting characters prevents accidental DNS resolution.
                if (!value.matches(Regex("^[0-9A-Fa-f:.]+$"))) {
                    throw IllegalArgumentException("IPv6 receiver must be an unscoped numeric literal")
                }
                val parsed = runCatching { InetAddress.getByName(value) }.getOrNull()
                if (parsed !is Inet6Address) throw IllegalArgumentException("Invalid IPv6 receiver address")
                return parsed
            }
            val octets = value.split('.')
            if (octets.size != 4 || octets.any { it.isEmpty() || it.length > 3 || it.any { char -> char !in '0'..'9' } }) {
                throw IllegalArgumentException("Receiver must be a numeric IPv4 or IPv6 address")
            }
            val bytes = ByteArray(4)
            octets.forEachIndexed { index, octet ->
                val number = octet.toIntOrNull() ?: throw IllegalArgumentException("Invalid IPv4 receiver address")
                if (number !in 0..255) throw IllegalArgumentException("Invalid IPv4 receiver address")
                bytes[index] = number.toByte()
            }
            return InetAddress.getByAddress(bytes)
        }

        private fun InetAddress.isLocalUnicast(): Boolean {
            if (isAnyLocalAddress || isMulticastAddress) return false
            if (isLoopbackAddress) return true
            if (this is Inet6Address) {
                val first = address[0].toInt() and 0xff
                return first and 0xfe == 0xfc // RFC 4193 unique-local fc00::/7
            }
            if (isLinkLocalAddress || isSiteLocalAddress) return true
            if (this is Inet4Address) {
                val bytes = address
                val first = bytes[0].toInt() and 0xff
                val second = bytes[1].toInt() and 0xff
                return first == 100 && second in 64..127 // RFC 6598 shared address space
            }
            return false
        }
    }
}
