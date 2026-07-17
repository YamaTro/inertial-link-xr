package io.github.yamatro.inertiallink.motion

import kotlin.test.Test
import kotlin.test.assertEquals

class MotionSourceConfigTest {
    @Test
    fun `default sampling target is one hundred hertz`() {
        val config = MotionSourceConfig()

        assertEquals(10_000, config.samplePeriodUs)
        assertEquals(10_000_000L, config.minimumFrameIntervalNs)
    }
}
