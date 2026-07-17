package io.github.yamatro.inertiallink.motion

import io.github.yamatro.inertiallink.protocol.Quaternionf
import io.github.yamatro.inertiallink.protocol.Vector3f
import kotlin.math.abs
import kotlin.math.sqrt

/**
 * Converts Android device axes to the consumer coordinate basis.
 *
 * The default assumes the phone is screen-up with its top edge toward vehicle travel and maps
 * Android `(x right, y phone-top, z out-of-screen)` to OpenXR-style vehicle
 * `(x right, y up, z backward)` as `(x, z, -y)`.
 */
public class MountTransform private constructor(private val matrix: FloatArray) {
    public fun map(vector: Vector3f): Vector3f {
        require(vector.x.isFinite() && vector.y.isFinite() && vector.z.isFinite()) {
            "Vector must contain finite values"
        }
        return Vector3f(
            x = matrix[0] * vector.x + matrix[1] * vector.y + matrix[2] * vector.z,
            y = matrix[3] * vector.x + matrix[4] * vector.y + matrix[5] * vector.z,
            z = matrix[6] * vector.x + matrix[7] * vector.y + matrix[8] * vector.z,
        )
    }

    /** Basis-maps the quaternion's vector component and normalizes it. Component order remains xyzw. */
    public fun map(quaternion: Quaternionf): Quaternionf {
        require(
            quaternion.x.isFinite() && quaternion.y.isFinite() &&
                quaternion.z.isFinite() && quaternion.w.isFinite(),
        ) { "Quaternion must contain finite values" }
        val vector = map(Vector3f(quaternion.x, quaternion.y, quaternion.z))
        val norm = sqrt(vector.x * vector.x + vector.y * vector.y + vector.z * vector.z + quaternion.w * quaternion.w)
        require(norm.isFinite() && norm >= 0.0001f) { "Quaternion norm is too small or non-finite" }
        return Quaternionf(vector.x / norm, vector.y / norm, vector.z / norm, quaternion.w / norm)
    }

    public companion object {
        @JvmField
        public val SCREEN_UP_TOP_FORWARD: MountTransform = MountTransform(
            floatArrayOf(
                1f, 0f, 0f,
                0f, 0f, 1f,
                0f, -1f, 0f,
            ),
        )

        /** Creates a proper orthonormal rotation from a row-major 3x3 matrix. */
        @JvmStatic
        public fun fromRowMajor(values: FloatArray): MountTransform {
            require(values.size == 9) { "Mount transform must contain nine values" }
            require(values.all(Float::isFinite)) { "Mount transform must contain finite values" }
            for (row in 0..2) {
                val lengthSquared = (0..2).sumOf { column ->
                    val value = values[row * 3 + column]
                    (value * value).toDouble()
                }
                require(abs(lengthSquared - 1.0) < 0.002) { "Mount transform rows must have unit length" }
            }
            for (first in 0..1) {
                for (second in first + 1..2) {
                    val dot = (0..2).sumOf { column ->
                        (values[first * 3 + column] * values[second * 3 + column]).toDouble()
                    }
                    require(abs(dot) < 0.002) { "Mount transform rows must be perpendicular" }
                }
            }
            val determinant =
                values[0] * (values[4] * values[8] - values[5] * values[7]) -
                    values[1] * (values[3] * values[8] - values[5] * values[6]) +
                    values[2] * (values[3] * values[7] - values[4] * values[6])
            require(abs(determinant - 1f) < 0.002f) { "Mount transform must be a proper rotation" }
            return MountTransform(values.copyOf())
        }
    }
}
