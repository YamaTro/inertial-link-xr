package io.github.yamatro.inertiallink.motion

import kotlin.test.Test
import kotlin.test.assertFailsWith

class SingleStartGuardTest {
    @Test
    fun `sender session cannot be restarted after its first start attempt`() {
        val guard = SingleStartGuard()

        guard.claim()
        assertFailsWith<IllegalStateException> { guard.claim() }
    }
}
