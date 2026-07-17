package io.github.yamatro.inertiallink.motion

import io.github.yamatro.inertiallink.protocol.Vector3f
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertTrue

class StationaryCalibratorTest {
    @Test
    fun `stationary samples produce averaged biases and increment ID`() {
        val calibrator = StationaryCalibrator(requiredSamples = 3)
        calibrator.begin()

        assertIs<CalibrationResult.Collecting>(calibrator.accept(Vector3f(0.01f, 0f, 0f), Vector3f(0.1f, 0f, 0f)))
        assertIs<CalibrationResult.Collecting>(calibrator.accept(Vector3f(0.02f, 0f, 0f), Vector3f(0.2f, 0f, 0f)))
        val result = assertIs<CalibrationResult.Completed>(
            calibrator.accept(Vector3f(0.03f, 0f, 0f), Vector3f(0.3f, 0f, 0f)),
        )

        assertEquals(1L, result.calibrationId)
        assertTrue(calibrator.hasCalibration)
        assertTrue(kotlin.math.abs(result.bias.gyroscope.x - 0.02f) < 0.0001f)
        assertTrue(kotlin.math.abs(result.bias.linearAcceleration.x - 0.2f) < 0.0001f)
    }

    @Test
    fun `moving window is rejected without replacing previous bias`() {
        val calibrator = StationaryCalibrator(requiredSamples = 3, maximumRejectedSamples = 2)
        calibrator.begin()

        assertIs<CalibrationResult.Collecting>(calibrator.accept(Vector3f(1f, 0f, 0f), Vector3f(0f, 0f, 0f)))
        assertIs<CalibrationResult.Failed>(calibrator.accept(Vector3f(1f, 0f, 0f), Vector3f(0f, 0f, 0f)))
        assertEquals(0L, calibrator.calibrationId)
        assertEquals(Vector3f(0f, 0f, 0f), calibrator.bias.gyroscope)
    }

    @Test
    fun `non-finite samples fail without poisoning an existing bias`() {
        val calibrator = StationaryCalibrator(requiredSamples = 2, maximumRejectedSamples = 2)
        calibrator.begin()
        calibrator.accept(Vector3f(0.02f, 0f, 0f), Vector3f(0.1f, 0f, 0f))
        assertIs<CalibrationResult.Completed>(
            calibrator.accept(Vector3f(0.02f, 0f, 0f), Vector3f(0.1f, 0f, 0f)),
        )
        val previousBias = calibrator.bias
        val previousId = calibrator.calibrationId

        calibrator.begin()
        assertIs<CalibrationResult.Collecting>(
            calibrator.accept(Vector3f(Float.NaN, 0f, 0f), Vector3f(0f, 0f, 0f)),
        )
        assertIs<CalibrationResult.Failed>(
            calibrator.accept(Vector3f(0f, 0f, 0f), Vector3f(Float.POSITIVE_INFINITY, 0f, 0f)),
        )

        assertEquals(previousId, calibrator.calibrationId)
        assertEquals(previousBias, calibrator.bias)
    }

    @Test
    fun `invalid calibration policy is rejected before collection`() {
        assertFailsWith<IllegalArgumentException> { StationaryCalibrator(requiredSamples = 0) }
        assertFailsWith<IllegalArgumentException> {
            StationaryCalibrator(requiredSamples = 2, maximumAngularSpeed = Float.NaN)
        }
        assertFailsWith<IllegalArgumentException> {
            StationaryCalibrator(requiredSamples = 2, maximumLinearAcceleration = Float.POSITIVE_INFINITY)
        }
    }
}
