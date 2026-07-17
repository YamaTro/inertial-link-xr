package io.github.yamatro.inertiallink.motion

import io.github.yamatro.inertiallink.protocol.Quaternionf
import io.github.yamatro.inertiallink.protocol.Vector3f

/** A coherent snapshot in the Android elapsed-realtime clock domain. */
public data class MotionFrame(
    public val eventTimeNs: Long,
    public val rawAcceleration: Vector3f,
    public val angularVelocity: Vector3f,
    public val gravity: Vector3f,
    public val linearAcceleration: Vector3f,
    public val rotation: Quaternionf,
    public val calibrationId: Long,
    public val statusBits: Long,
)

public sealed interface CalibrationUpdate {
    public data class Collecting(public val acceptedSamples: Int, public val requiredSamples: Int) : CalibrationUpdate
    public data class Completed(public val calibrationId: Long) : CalibrationUpdate
    public data class Failed(public val reason: String) : CalibrationUpdate
}

public interface MotionSource : AutoCloseable {
    public interface Listener {
        public fun onMotionFrame(frame: MotionFrame)
        public fun onCalibrationUpdate(update: CalibrationUpdate) {}
        public fun onSourceError(message: String, cause: Throwable? = null) {}
    }

    /** Starts sampling. Implementations must reject a second concurrent start. */
    public fun start(listener: Listener)

    /** Requests stationary gyro/linear-acceleration bias calibration while sampling. */
    public fun requestStationaryCalibration()

    public fun stop()

    override fun close(): Unit = stop()
}

public data class MotionSourceConfig(
    /** Requested sensor interval. Android may deliver a different rate. */
    public val samplePeriodUs: Int = 10_000,
    /** Caps emitted frames independently of Android sensor batching. */
    public val minimumFrameIntervalNs: Long = 10_000_000L,
    public val calibrationSamples: Int = 75,
) {
    init {
        require(samplePeriodUs in 10_000..200_000) { "samplePeriodUs must be between 10,000 and 200,000" }
        require(minimumFrameIntervalNs in 5_000_000L..1_000_000_000L) { "minimumFrameIntervalNs is outside bounds" }
        require(calibrationSamples in 25..500) { "calibrationSamples must be between 25 and 500" }
    }
}
