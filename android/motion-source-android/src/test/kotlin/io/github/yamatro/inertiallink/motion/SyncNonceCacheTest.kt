package io.github.yamatro.inertiallink.motion

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class SyncNonceCacheTest {
    @Test
    fun `nonce reuse and conflicting t0 are rejected`() {
        val cache = SyncNonceCache(capacity = 2)

        assertTrue(cache.accept(nonce = 7, t0Ns = 100))
        assertFalse(cache.accept(nonce = 7, t0Ns = 100))
        assertFalse(cache.accept(nonce = 7, t0Ns = 101))
        assertFalse(cache.accept(nonce = 8, t0Ns = 0))
    }

    @Test
    fun `cache is bounded while replay window carries long-term packet defense`() {
        val cache = SyncNonceCache(capacity = 2)
        assertTrue(cache.accept(1, 100))
        assertTrue(cache.accept(2, 101))
        assertTrue(cache.accept(3, 102))

        assertTrue(cache.accept(1, 103))
    }
}
