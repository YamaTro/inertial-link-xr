package io.github.yamatro.inertiallink.motion

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Handler
import android.os.HandlerThread
import android.os.Process
import io.github.yamatro.inertiallink.protocol.ImuStatus
import io.github.yamatro.inertiallink.protocol.Quaternionf
import io.github.yamatro.inertiallink.protocol.Vector3f

/** SensorManager-backed source. No location, camera, microphone, storage, or telemetry is used. */
public class AndroidMotionSource(
    context: Context,
    private val config: MotionSourceConfig = MotionSourceConfig(),
    private val mountTransform: MountTransform = MountTransform.SCREEN_UP_TOP_FORWARD,
) : MotionSource, SensorEventListener {
    private val sensorManager = context.applicationContext.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val calibrator = StationaryCalibrator(config.calibrationSamples)
    private val stateLock = Any()

    @Volatile private var running = false
    @Volatile private var listener: MotionSource.Listener? = null
    private var callbackThread: HandlerThread? = null
    private var callbackHandler: Handler? = null

    private var rawAcceleration = ZERO_VECTOR
    private var gravity = ZERO_VECTOR
    private var linearAcceleration = ZERO_VECTOR
    private var angularVelocity = ZERO_VECTOR
    private var rotation = IDENTITY_QUATERNION
    private var rawTimestampNs = Long.MIN_VALUE
    private var gravityTimestampNs = Long.MIN_VALUE
    private var linearTimestampNs = Long.MIN_VALUE
    private var gyroTimestampNs = Long.MIN_VALUE
    private var rotationTimestampNs = Long.MIN_VALUE
    private var lastFrameTimestampNs = Long.MIN_VALUE
    private var accelerometerAccuracy = SensorManager.SENSOR_STATUS_UNRELIABLE
    private var gyroscopeAccuracy = SensorManager.SENSOR_STATUS_UNRELIABLE
    private var lastCalibrationProgress = -1

    private var gravitySensorAvailable = false
    private var linearSensorAvailable = false
    private var rotationSensorType: Int? = null

    override fun start(listener: MotionSource.Listener) {
        synchronized(stateLock) {
            check(!running) { "Motion source is already running" }
            val accelerometer = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
                ?: throw IllegalStateException("This device does not expose an accelerometer")
            val gyroscope = sensorManager.getDefaultSensor(Sensor.TYPE_GYROSCOPE)
                ?: throw IllegalStateException("This device does not expose a gyroscope")
            val gravitySensor = sensorManager.getDefaultSensor(Sensor.TYPE_GRAVITY)
            val linearSensor = sensorManager.getDefaultSensor(Sensor.TYPE_LINEAR_ACCELERATION)
            val gameRotation = sensorManager.getDefaultSensor(Sensor.TYPE_GAME_ROTATION_VECTOR)
            val absoluteRotation = sensorManager.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR)
            val rotationSensor = gameRotation ?: absoluteRotation

            resetSamples()
            this.listener = listener
            val thread = HandlerThread("InertialLink-Sensors", Process.THREAD_PRIORITY_MORE_FAVORABLE).also { it.start() }
            val handler = Handler(thread.looper)
            callbackThread = thread
            callbackHandler = handler
            try {
                val requiredStarted =
                    register(accelerometer, handler) && register(gyroscope, handler)
                if (!requiredStarted) {
                    throw IllegalStateException("Android rejected required sensor registration")
                }
                gravitySensorAvailable = gravitySensor?.let { register(it, handler) } == true
                linearSensorAvailable = linearSensor?.let { register(it, handler) } == true
                rotationSensorType = rotationSensor?.takeIf { register(it, handler) }?.type
                running = true
            } catch (error: RuntimeException) {
                runCatching { sensorManager.unregisterListener(this) }
                thread.quitSafely()
                callbackThread = null
                callbackHandler = null
                this.listener = null
                throw IllegalStateException("Unable to register Android motion sensors", error)
            }
        }
    }

    override fun requestStationaryCalibration() {
        val handler = callbackHandler ?: throw IllegalStateException("Motion source is not running")
        handler.post {
            if (!running) return@post
            try {
                calibrator.begin()
                lastCalibrationProgress = -1
                listener?.onCalibrationUpdate(CalibrationUpdate.Collecting(0, config.calibrationSamples))
            } catch (error: RuntimeException) {
                failSource("Stationary calibration could not start", error)
            }
        }
    }

    override fun stop() {
        val thread: HandlerThread?
        synchronized(stateLock) {
            if (!running && callbackThread == null) return
            running = false
            thread = callbackThread
            callbackThread = null
            callbackHandler = null
            listener = null
        }
        // State is already fail-closed even if a vendor SensorManager throws here.
        runCatching { sensorManager.unregisterListener(this) }
        thread?.quitSafely()
    }

    override fun onSensorChanged(event: SensorEvent) {
        if (!running) return
        try {
            when (event.sensor.type) {
                Sensor.TYPE_ACCELEROMETER -> {
                    rawAcceleration = mountTransform.map(event.values.toVector())
                    rawTimestampNs = event.timestamp
                }
                Sensor.TYPE_GRAVITY -> {
                    gravity = mountTransform.map(event.values.toVector())
                    gravityTimestampNs = event.timestamp
                }
                Sensor.TYPE_LINEAR_ACCELERATION -> {
                    linearAcceleration = mountTransform.map(event.values.toVector())
                    linearTimestampNs = event.timestamp
                }
                Sensor.TYPE_GYROSCOPE -> {
                    angularVelocity = mountTransform.map(event.values.toVector())
                    gyroTimestampNs = event.timestamp
                    emitFrameIfReady(event.timestamp)
                }
                Sensor.TYPE_GAME_ROTATION_VECTOR, Sensor.TYPE_ROTATION_VECTOR -> {
                    if (event.sensor.type == rotationSensorType) {
                        rotation = mountTransform.map(event.values.toQuaternion())
                        rotationTimestampNs = event.timestamp
                    }
                }
            }
        } catch (error: RuntimeException) {
            failSource("Sensor sample processing failed", error)
        }
    }

    override fun onAccuracyChanged(sensor: Sensor, accuracy: Int) {
        when (sensor.type) {
            Sensor.TYPE_ACCELEROMETER -> accelerometerAccuracy = accuracy
            Sensor.TYPE_GYROSCOPE -> gyroscopeAccuracy = accuracy
        }
    }

    private fun emitFrameIfReady(eventTimeNs: Long) {
        if (rawTimestampNs == Long.MIN_VALUE) return
        if (lastFrameTimestampNs != Long.MIN_VALUE && eventTimeNs - lastFrameTimestampNs < config.minimumFrameIntervalNs) return
        lastFrameTimestampNs = eventTimeNs

        val rawValid = isFresh(rawTimestampNs, eventTimeNs)
        val gyroValid = isFresh(gyroTimestampNs, eventTimeNs)
        val gravityDirectValid = gravitySensorAvailable && isFresh(gravityTimestampNs, eventTimeNs)
        val linearDirectValid = linearSensorAvailable && isFresh(linearTimestampNs, eventTimeNs)

        val frameGravity: Vector3f
        val gravityValid: Boolean
        if (gravityDirectValid) {
            frameGravity = gravity
            gravityValid = true
        } else if (rawValid && linearDirectValid) {
            frameGravity = rawAcceleration - linearAcceleration
            gravityValid = true
        } else {
            frameGravity = ZERO_VECTOR
            gravityValid = false
        }

        val uncorrectedLinear: Vector3f
        val linearValid: Boolean
        if (linearDirectValid) {
            uncorrectedLinear = linearAcceleration
            linearValid = true
        } else if (rawValid && gravityDirectValid) {
            uncorrectedLinear = rawAcceleration - gravity
            linearValid = true
        } else {
            uncorrectedLinear = ZERO_VECTOR
            linearValid = false
        }

        handleCalibration(angularVelocity, uncorrectedLinear, gyroValid && linearValid)
        val bias = calibrator.bias
        val correctedGyro = if (gyroValid) angularVelocity - bias.gyroscope else ZERO_VECTOR
        val correctedLinear = if (linearValid) uncorrectedLinear - bias.linearAcceleration else ZERO_VECTOR
        val rotationValid = rotationSensorType != null && isFresh(rotationTimestampNs, eventTimeNs)

        var status = 0L
        if (rawValid) status = status or ImuStatus.RAW_ACCEL_VALID
        if (gyroValid) status = status or ImuStatus.GYROSCOPE_VALID
        if (gravityValid) status = status or ImuStatus.GRAVITY_VALID
        if (linearValid) status = status or ImuStatus.LINEAR_ACCEL_VALID
        if (rotationValid) status = status or ImuStatus.ROTATION_VALID
        if (calibrator.hasCalibration) status = status or ImuStatus.CALIBRATED
        if (calibrator.isCollecting) status = status or ImuStatus.CALIBRATING
        status = status or accuracyStatus()

        listener?.onMotionFrame(
            MotionFrame(
                eventTimeNs = eventTimeNs,
                rawAcceleration = if (rawValid) rawAcceleration else ZERO_VECTOR,
                angularVelocity = correctedGyro,
                gravity = frameGravity,
                linearAcceleration = correctedLinear,
                rotation = if (rotationValid) rotation else IDENTITY_QUATERNION,
                calibrationId = calibrator.calibrationId,
                statusBits = status,
            ),
        )
    }

    private fun handleCalibration(gyro: Vector3f, linear: Vector3f, inputsValid: Boolean) {
        if (!calibrator.isCollecting || !inputsValid) return
        when (val result = calibrator.accept(gyro, linear)) {
            CalibrationResult.Idle -> Unit
            is CalibrationResult.Collecting -> {
                if (result.accepted == 0 || result.accepted == 1 || result.accepted == result.required ||
                    result.accepted - lastCalibrationProgress >= 10
                ) {
                    lastCalibrationProgress = result.accepted
                    listener?.onCalibrationUpdate(CalibrationUpdate.Collecting(result.accepted, result.required))
                }
            }
            is CalibrationResult.Completed -> {
                listener?.onCalibrationUpdate(CalibrationUpdate.Completed(result.calibrationId))
            }
            is CalibrationResult.Failed -> listener?.onCalibrationUpdate(CalibrationUpdate.Failed(result.reason))
        }
    }

    private fun accuracyStatus(): Long = when (minOf(accelerometerAccuracy, gyroscopeAccuracy)) {
        SensorManager.SENSOR_STATUS_ACCURACY_LOW -> ImuStatus.SENSOR_ACCURACY_LOW
        SensorManager.SENSOR_STATUS_ACCURACY_MEDIUM -> ImuStatus.SENSOR_ACCURACY_MEDIUM
        SensorManager.SENSOR_STATUS_ACCURACY_HIGH -> ImuStatus.SENSOR_ACCURACY_HIGH
        else -> 0L
    }

    private fun isFresh(timestampNs: Long, nowNs: Long): Boolean =
        timestampNs != Long.MIN_VALUE && nowNs >= timestampNs && nowNs - timestampNs <= MAX_SAMPLE_AGE_NS

    private fun register(sensor: Sensor, handler: Handler): Boolean = sensorManager.registerListener(
        this,
        sensor,
        config.samplePeriodUs,
        0,
        handler,
    )

    private fun resetSamples() {
        rawAcceleration = ZERO_VECTOR
        gravity = ZERO_VECTOR
        linearAcceleration = ZERO_VECTOR
        angularVelocity = ZERO_VECTOR
        rotation = IDENTITY_QUATERNION
        rawTimestampNs = Long.MIN_VALUE
        gravityTimestampNs = Long.MIN_VALUE
        linearTimestampNs = Long.MIN_VALUE
        gyroTimestampNs = Long.MIN_VALUE
        rotationTimestampNs = Long.MIN_VALUE
        lastFrameTimestampNs = Long.MIN_VALUE
        accelerometerAccuracy = SensorManager.SENSOR_STATUS_UNRELIABLE
        gyroscopeAccuracy = SensorManager.SENSOR_STATUS_UNRELIABLE
    }

    private fun FloatArray.toVector(): Vector3f {
        require(size >= 3) { "Sensor vector must contain three values" }
        require(this[0].isFinite() && this[1].isFinite() && this[2].isFinite()) {
            "Sensor vector must contain finite values"
        }
        return Vector3f(this[0], this[1], this[2])
    }

    private fun FloatArray.toQuaternion(): Quaternionf {
        require(size >= 3) { "Rotation vector must contain at least three values" }
        require(all(Float::isFinite)) { "Rotation vector must contain finite values" }
        val wxyz = FloatArray(4)
        SensorManager.getQuaternionFromVector(wxyz, this)
        require(wxyz.all(Float::isFinite)) { "Android returned a non-finite rotation quaternion" }
        return Quaternionf(x = wxyz[1], y = wxyz[2], z = wxyz[3], w = wxyz[0])
    }

    private fun failSource(message: String, error: RuntimeException) {
        val errorListener = listener
        // Stop before calling application code so a broken sample or callback cannot leave
        // sensors active in a half-failed state.
        stop()
        runCatching { errorListener?.onSourceError(message, error) }
    }

    private companion object {
        const val MAX_SAMPLE_AGE_NS: Long = 200_000_000L
        val ZERO_VECTOR = Vector3f(0f, 0f, 0f)
        val IDENTITY_QUATERNION = Quaternionf(0f, 0f, 0f, 1f)
    }
}
