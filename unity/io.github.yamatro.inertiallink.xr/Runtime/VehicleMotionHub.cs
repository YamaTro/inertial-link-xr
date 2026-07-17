using System;
using UnityEngine;
using YamaTro.InertialLink.Core;

namespace YamaTro.InertialLink
{
    public struct VehicleMotionState
    {
        public readonly Vector3 RawAcceleration;
        public readonly Vector3 LinearAcceleration;
        public readonly Vector3 AngularVelocity;
        public readonly Vector3 Gravity;
        public readonly Quaternion VehicleRotation;
        public readonly float SafetyWeight;
        public readonly MotionSafetyState SafetyState;
        public readonly bool TimestampSynchronized;
        public readonly ulong SessionId;
        public readonly uint Sequence;
        public readonly uint CalibrationId;
        public readonly uint StatusBits;

        public bool RawAccelerationValid { get { return ((SensorStatusBits)StatusBits & SensorStatusBits.RawAccelerationValid) != 0; } }
        public bool GravityValid { get { return ((SensorStatusBits)StatusBits & SensorStatusBits.GravityValid) != 0; } }
        public bool RotationValid { get { return ((SensorStatusBits)StatusBits & SensorStatusBits.RotationValid) != 0; } }

        public VehicleMotionState(Vector3 rawAcceleration, Vector3 linearAcceleration, Vector3 angularVelocity,
            Vector3 gravity, Quaternion vehicleRotation, float safetyWeight, MotionSafetyState safetyState,
            bool timestampSynchronized, ulong sessionId, uint sequence, uint calibrationId, uint statusBits)
        {
            RawAcceleration = rawAcceleration;
            LinearAcceleration = linearAcceleration;
            AngularVelocity = angularVelocity;
            Gravity = gravity;
            VehicleRotation = vehicleRotation;
            SafetyWeight = safetyWeight;
            SafetyState = safetyState;
            TimestampSynchronized = timestampSynchronized;
            SessionId = sessionId;
            Sequence = sequence;
            CalibrationId = calibrationId;
            StatusBits = statusBits;
        }
    }

    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class VehicleMotionHub : MonoBehaviour
    {
        [Tooltip("A component implementing IMotionSource, such as UdpMotionSource.")]
        [SerializeField] private MonoBehaviour activeSource;
        [SerializeField, Range(0.05f, 20f)] private float linearAccelerationCutoffHertz = 1.5f;
        [SerializeField, Range(0.05f, 20f)] private float angularVelocityCutoffHertz = 2f;
        [SerializeField, Range(1, 512)] private int maximumFramesPerUpdate = 256;

        private IMotionSource source;
        private BoundedLowPassFilter3 linearFilter;
        private BoundedLowPassFilter3 angularFilter;
        private readonly SafetyGate safety = new SafetyGate();
        private MotionSourceFrame latestFrame;
        private bool hasFrame;
        private uint calibrationId;
        private ulong sessionId;
        private Type lastSourceExceptionType;
        private long nextSourceExceptionLogAt;
        private Type lastSubscriberExceptionType;
        private long nextSubscriberExceptionLogAt;

        public event Action<VehicleMotionState> MotionUpdated;

        public VehicleMotionState Current { get; private set; }
        public MotionSourceFrame LatestDiagnosticFrame { get; private set; }
        public MotionSafetyState SafetyState { get { return safety.State; } }
        public IMotionSource ActiveSource { get { return source; } }

        public bool SetSource(MonoBehaviour sourceBehaviour)
        {
            var candidate = sourceBehaviour as IMotionSource;
            if (sourceBehaviour != null && candidate == null)
            {
                Debug.LogError("InertialLink source must implement IMotionSource.", this);
                return false;
            }
            activeSource = sourceBehaviour;
            source = candidate;
            ResetPipeline();
            PublishState();
            return source != null;
        }

        private void Awake()
        {
            linearFilter = new BoundedLowPassFilter3(linearAccelerationCutoffHertz, ProtocolConstants.MaximumAccelerationComponent);
            angularFilter = new BoundedLowPassFilter3(angularVelocityCutoffHertz, ProtocolConstants.MaximumAngularVelocityComponent);
            if (activeSource != null) source = activeSource as IMotionSource;
            if (source == null) FindSourceOnObject();
            ResetPipeline();
            PublishState();
        }

        private void OnDisable()
        {
            ResetPipeline();
            PublishState();
        }

        private void OnDestroy()
        {
            ResetPipeline();
            PublishState();
            MotionUpdated = null;
        }

        private void OnValidate()
        {
            if (activeSource != null && !(activeSource is IMotionSource))
            {
                Debug.LogError("Assigned component does not implement IMotionSource.", this);
                activeSource = null;
            }
        }

        private void Update()
        {
            var now = MonotonicClock.NowNanoseconds;
            if (source == null) FindSourceOnObject();
            if (source != null)
            {
                try { DrainSource(now); }
                catch (Exception exception)
                {
                    ResetPipeline();
                    ReportSourceException(exception, now);
                }
            }
            safety.Tick(now);
            PublishState();
        }

        private void DrainSource(long now)
        {
            MotionSourceFrame frame;
            var drained = 0;
            var frameLimit = Mathf.Clamp(maximumFramesPerUpdate, 1, 512);
            while (drained++ < frameLimit && source.TryDequeue(out frame))
            {
                LatestDiagnosticFrame = frame;
                if (hasFrame && frame.SessionId != sessionId)
                {
                    // A new sender session invalidates both clock trust and previous visual motion.
                    ResetPipeline();
                }
                if (MotionSampleValidator.Validate(frame.Imu) != PacketError.None)
                {
                    safety.RejectContinuity();
                    continue;
                }
                if (frame.TimestampSynchronized &&
                    PacketFreshness.Evaluate(now, frame.LocalEventTimeNanoseconds) != FreshnessDecision.Accepted)
                {
                    safety.RejectContinuity();
                    continue;
                }

                if (!frame.TimestampSynchronized) safety.BeginWarmup();

                if (!MotionEligibility.CanDrive(frame.TimestampSynchronized, frame.Imu.StatusBits))
                {
                    safety.RejectContinuity();
                    continue;
                }

                if (hasFrame && frame.Imu.CalibrationId != calibrationId)
                {
                    linearFilter.Reset();
                    angularFilter.Reset();
                    safety.Reset();
                }

                Float3 filteredLinear;
                Float3 filteredAngular;
                if (!linearFilter.TryUpdate(frame.Imu.LinearAcceleration, frame.LocalEventTimeNanoseconds, out filteredLinear) ||
                    !angularFilter.TryUpdate(frame.Imu.AngularVelocity, frame.LocalEventTimeNanoseconds, out filteredAngular))
                {
                    safety.RejectContinuity();
                    continue;
                }

                latestFrame = new MotionSourceFrame(frame.SessionId, frame.Sequence, frame.ArrivalTimeNanoseconds,
                    frame.LocalEventTimeNanoseconds, frame.TimestampSynchronized,
                    new ImuPayload(frame.Imu.SenderSendTimeNanoseconds, frame.Imu.RawAcceleration, filteredAngular,
                        frame.Imu.Gravity, filteredLinear, frame.Imu.Rotation.Normalized(), frame.Imu.CalibrationId, frame.Imu.StatusBits));
                sessionId = frame.SessionId;
                calibrationId = frame.Imu.CalibrationId;
                hasFrame = true;
                safety.RecordAccepted(now);
            }
        }

        private void PublishState()
        {
            if (!hasFrame)
            {
                Current = new VehicleMotionState(Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                    Quaternion.identity, 0f, safety.State, false, 0, 0, 0, 0);
            }
            else
            {
                var imu = latestFrame.Imu;
                var bits = (SensorStatusBits)imu.StatusBits;
                Current = new VehicleMotionState(
                    (bits & SensorStatusBits.RawAccelerationValid) != 0
                        ? CoordinateMapping.PolarVectorToUnity(imu.RawAcceleration) : Vector3.zero,
                    CoordinateMapping.PolarVectorToUnity(imu.LinearAcceleration),
                    CoordinateMapping.AngularVelocityToUnity(imu.AngularVelocity),
                    (bits & SensorStatusBits.GravityValid) != 0
                        ? CoordinateMapping.PolarVectorToUnity(imu.Gravity) : Vector3.zero,
                    (bits & SensorStatusBits.RotationValid) != 0
                        ? CoordinateMapping.RotationToUnity(imu.Rotation) : Quaternion.identity,
                    safety.Weight, safety.State, latestFrame.TimestampSynchronized, latestFrame.SessionId,
                    latestFrame.Sequence, imu.CalibrationId, imu.StatusBits);
            }
            var callback = MotionUpdated;
            if (callback == null) return;
            var handlers = callback.GetInvocationList();
            for (var i = 0; i < handlers.Length; i++)
            {
                try { ((Action<VehicleMotionState>)handlers[i])(Current); }
                catch (Exception exception) { ReportSubscriberException(exception, MonotonicClock.NowNanoseconds); }
            }
        }

        private void ReportSourceException(Exception exception, long now)
        {
            var type = exception.GetType();
            if (type == lastSourceExceptionType && now < nextSourceExceptionLogAt) return;
            lastSourceExceptionType = type;
            nextSourceExceptionLogAt = now > long.MaxValue - 5000000000L ? long.MaxValue : now + 5000000000L;
            Debug.LogError("InertialLink source failed closed: " + type.Name, this);
        }

        private void ReportSubscriberException(Exception exception, long now)
        {
            var type = exception.GetType();
            if (type == lastSubscriberExceptionType && now < nextSubscriberExceptionLogAt) return;
            lastSubscriberExceptionType = type;
            nextSubscriberExceptionLogAt = now > long.MaxValue - 5000000000L ? long.MaxValue : now + 5000000000L;
            Debug.LogError("InertialLink MotionUpdated subscriber failed: " + type.Name, this);
        }

        private void FindSourceOnObject()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                var candidate = behaviours[i] as IMotionSource;
                if (candidate == null) continue;
                source = candidate;
                activeSource = behaviours[i];
                return;
            }
        }

        private void ResetPipeline()
        {
            if (linearFilter != null) linearFilter.Reset();
            if (angularFilter != null) angularFilter.Reset();
            safety.Reset();
            hasFrame = false;
            sessionId = 0;
            calibrationId = 0;
            latestFrame = default(MotionSourceFrame);
            LatestDiagnosticFrame = default(MotionSourceFrame);
        }
    }
}
