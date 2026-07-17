package io.github.yamatro.inertiallink.motion

import io.github.yamatro.inertiallink.protocol.Quaternionf
import io.github.yamatro.inertiallink.protocol.Vector3f
import kotlin.math.abs
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

class MountTransformTest {
    @Test
    fun `default mount maps phone top to OpenXR forward`() {
        val transform = MountTransform.SCREEN_UP_TOP_FORWARD

        assertEquals(Vector3f(1f, 3f, -2f), transform.map(Vector3f(1f, 2f, 3f)))
    }

    @Test
    fun `quaternion mapping preserves unit norm`() {
        val mapped = MountTransform.SCREEN_UP_TOP_FORWARD.map(Quaternionf(0.5f, 0.5f, 0.5f, 0.5f))
        val normSquared = mapped.x * mapped.x + mapped.y * mapped.y + mapped.z * mapped.z + mapped.w * mapped.w

        assertTrue(abs(normSquared - 1f) < 0.0001f)
    }

    @Test
    fun `custom mount rejects reflection and scaling`() {
        assertFailsWith<IllegalArgumentException> {
            MountTransform.fromRowMajor(floatArrayOf(2f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f))
        }
        assertFailsWith<IllegalArgumentException> {
            MountTransform.fromRowMajor(floatArrayOf(-1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f))
        }
    }

    @Test
    fun `invalid quaternion is rejected instead of becoming a valid identity`() {
        val transform = MountTransform.SCREEN_UP_TOP_FORWARD

        assertFailsWith<IllegalArgumentException> {
            transform.map(Vector3f(Float.POSITIVE_INFINITY, 0f, 0f))
        }
        assertFailsWith<IllegalArgumentException> {
            transform.map(Quaternionf(Float.NaN, 0f, 0f, 1f))
        }
        assertFailsWith<IllegalArgumentException> {
            transform.map(Quaternionf(0f, 0f, 0f, 0f))
        }
    }
}
