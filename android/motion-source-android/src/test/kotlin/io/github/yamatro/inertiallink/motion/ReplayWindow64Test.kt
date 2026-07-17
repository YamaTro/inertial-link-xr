package io.github.yamatro.inertiallink.motion

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class ReplayWindow64Test {
    @Test
    fun `duplicate and too-old authenticated requests are rejected`() {
        val window = ReplayWindow64()

        assertTrue(window.accept(100))
        assertTrue(window.accept(99))
        assertFalse(window.accept(99))
        assertTrue(window.accept(101))
        assertFalse(window.accept(100))
        assertFalse(window.accept(37)) // 64 behind the highest value
    }

    @Test
    fun `replay remains rejected after more than nonce-cache-size new requests`() {
        val window = ReplayWindow64()
        assertTrue(window.accept(1))
        for (sequence in 2L..70L) assertTrue(window.accept(sequence))

        assertFalse(window.accept(1))
        assertFalse(window.accept(70))
    }

    @Test
    fun `same session max to zero is rejected while a new session window accepts zero`() {
        val oldSession = ReplayWindow64()
        assertTrue(oldSession.accept(SessionSequence.MAX_SEQUENCE))
        assertFalse(oldSession.accept(0))

        val newSession = ReplayWindow64()
        assertTrue(newSession.accept(0))
    }
}
