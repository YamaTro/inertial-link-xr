package io.github.yamatro.inertiallink.motion

import io.github.yamatro.inertiallink.protocol.Vector3f
import kotlin.math.sqrt

internal data class SensorBias(val gyroscope: Vector3f, val linearAcceleration: Vector3f)

internal sealed interface CalibrationResult {
    data object Idle : CalibrationResult
    data class Collecting(val accepted: Int, val required: Int) : CalibrationResult
    data class Completed(val calibrationId: Long, val bias: SensorBias) : CalibrationResult
    data class Failed(val reason: String) : CalibrationResult
}

/** Rejects moving calibration windows instead of silently learning vehicle motion as bias. */
internal class StationaryCalibrator(
    private val requiredSamples: Int,
    private val maximumRejectedSamples: Int = 25,
    private val maximumAngularSpeed: Float = 0.12f,
    private val maximumLinearAcceleration: Float = 0.75f,
) {
    init {
        require(requiredSamples > 0) { "requiredSamples must be positive" }
        require(maximumRejectedSamples > 0) { "maximumRejectedSamples must be positive" }
        require(maximumAngularSpeed.isFinite() && maximumAngularSpeed > 0f) {
            "maximumAngularSpeed must be finite and positive"
        }
        require(maximumLinearAcceleration.isFinite() && maximumLinearAcceleration > 0f) {
            "maximumLinearAcceleration must be finite and positive"
        }
    }

    var bias: SensorBias = SensorBias(Vector3f(0f, 0f, 0f), Vector3f(0f, 0f, 0f))
        private set
    var calibrationId: Long = 0
        private set
    var hasCalibration: Boolean = false
        private set
    var isCollecting: Boolean = false
        private set

    private var accepted = 0
    private var rejected = 0
    private var gyroX = 0.0
    private var gyroY = 0.0
    private var gyroZ = 0.0
    private var linearX = 0.0
    private var linearY = 0.0
    private var linearZ = 0.0

    fun begin() {
        accepted = 0
        rejected = 0
        gyroX = 0.0
        gyroY = 0.0
        gyroZ = 0.0
        linearX = 0.0
        linearY = 0.0
        linearZ = 0.0
        isCollecting = true
    }

    fun accept(gyroscope: Vector3f, linearAcceleration: Vector3f): CalibrationResult {
        if (!isCollecting) return CalibrationResult.Idle
        if (!gyroscope.isFinite() || !linearAcceleration.isFinite() ||
            gyroscope.magnitude() > maximumAngularSpeed || linearAcceleration.magnitude() > maximumLinearAcceleration
        ) {
            rejected += 1
            if (rejected >= maximumRejectedSamples) {
                isCollecting = false
                return CalibrationResult.Failed("Movement detected. Keep the mounted phone still and retry.")
            }
            return CalibrationResult.Collecting(accepted, requiredSamples)
        }
        gyroX += gyroscope.x
        gyroY += gyroscope.y
        gyroZ += gyroscope.z
        linearX += linearAcceleration.x
        linearY += linearAcceleration.y
        linearZ += linearAcceleration.z
        accepted += 1
        if (accepted < requiredSamples) return CalibrationResult.Collecting(accepted, requiredSamples)

        val count = accepted.toFloat()
        bias = SensorBias(
            Vector3f((gyroX / count).toFloat(), (gyroY / count).toFloat(), (gyroZ / count).toFloat()),
            Vector3f((linearX / count).toFloat(), (linearY / count).toFloat(), (linearZ / count).toFloat()),
        )
        calibrationId = (calibrationId + 1) and 0xffff_ffffL
        hasCalibration = true
        isCollecting = false
        return CalibrationResult.Completed(calibrationId, bias)
    }

    private fun Vector3f.magnitude(): Float = sqrt(x * x + y * y + z * z)

    private fun Vector3f.isFinite(): Boolean = x.isFinite() && y.isFinite() && z.isFinite()
}

internal operator fun Vector3f.minus(other: Vector3f): Vector3f =
    Vector3f(x - other.x, y - other.y, z - other.z)
