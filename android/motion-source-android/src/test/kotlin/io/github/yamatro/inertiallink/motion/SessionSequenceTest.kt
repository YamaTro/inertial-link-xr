package io.github.yamatro.inertiallink.motion

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class SessionSequenceTest {
    @Test
    fun `sequence never wraps inside one session`() {
        val sequence = SessionSequence(SessionSequence.MAX_SEQUENCE - 1)

        assertEquals(0xffff_fffeL, sequence.take())
        assertEquals(0xffff_ffffL, sequence.take())
        assertFailsWith<SequenceExhaustedException> { sequence.take() }
        assertFailsWith<SequenceExhaustedException> { sequence.take() }
    }

    @Test
    fun `exhaustion sentinel remains saturated`() {
        val sequence = SessionSequence(SessionSequence.MAX_SEQUENCE + 1L)

        repeat(100) {
            assertFailsWith<SequenceExhaustedException> { sequence.take() }
        }
    }
}
