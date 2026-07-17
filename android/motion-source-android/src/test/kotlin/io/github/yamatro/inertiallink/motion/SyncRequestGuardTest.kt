package io.github.yamatro.inertiallink.motion

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class SyncRequestGuardTest {
    @Test
    fun `t0 must equal header event time and invalid request does not consume sequence`() {
        val guard = SyncRequestGuard(nonceCapacity = 2)

        assertFalse(guard.accept(sequence = 4, eventTimeNs = 100, nonce = 7, t0Ns = 101))
        assertFalse(guard.accept(sequence = 4, eventTimeNs = 0, nonce = 7, t0Ns = 0))
        assertTrue(guard.accept(sequence = 4, eventTimeNs = 100, nonce = 7, t0Ns = 100))
    }

    @Test
    fun `replay and nonce reuse are rejected within one endpoint session scope`() {
        val guard = SyncRequestGuard(nonceCapacity = 2)

        assertTrue(guard.accept(10, 100, 1, 100))
        assertFalse(guard.accept(10, 100, 2, 100))
        assertFalse(guard.accept(11, 101, 1, 101))
        assertTrue(guard.accept(12, 102, 2, 102))
    }
}
