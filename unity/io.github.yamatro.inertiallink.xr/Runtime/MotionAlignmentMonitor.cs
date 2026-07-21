using UnityEngine;
using YamaTro.InertialLink.Core;

namespace YamaTro.InertialLink
{
    public readonly struct MotionAlignmentSnapshot
    {
        public readonly bool Available;
        public readonly Vector3 MeasuredLinearAcceleration;
        public readonly Vector3 VirtualLinearAcceleration;
        public readonly Vector3 Mismatch;
        public readonly Vector3 SuggestedVirtualCorrection;
        public readonly float MismatchMagnitude;
        public readonly bool WithinTolerance;

        public MotionAlignmentSnapshot(bool available, Vector3 measuredLinearAcceleration,
            Vector3 virtualLinearAcceleration, Vector3 mismatch, Vector3 suggestedVirtualCorrection,
            float mismatchMagnitude, bool withinTolerance)
        {
            Available = available;
            MeasuredLinearAcceleration = measuredLinearAcceleration;
            VirtualLinearAcceleration = virtualLinearAcceleration;
            Mismatch = mismatch;
            SuggestedVirtualCorrection = suggestedVirtualCorrection;
            MismatchMagnitude = mismatchMagnitude;
            WithinTolerance = withinTolerance;
        }
    }

    /// <summary>
    /// Compares an application's intended virtual acceleration with the authenticated
    /// vehicle measurement. It only reports a bounded correction suggestion; it never
    /// drives a Camera, XR Origin, vehicle, actuator, or application transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MotionAlignmentMonitor : MonoBehaviour
    {
        [SerializeField] private VehicleMotionHub hub;
        [SerializeField, Range(0.01f, 5f)] private float toleranceMetersPerSecondSquared = 0.35f;
        [SerializeField, Range(0f, 1f)] private float correctionGain = 0.5f;
        [SerializeField, Range(0.01f, 10f)] private float maximumCorrectionMetersPerSecondSquared = 2f;

        private Vector3 virtualLinearAcceleration;
        private bool hasVirtualAcceleration;

        public MotionAlignmentSnapshot Current { get; private set; }

        public void Configure(VehicleMotionHub motionHub)
        {
            hub = motionHub;
            Publish();
        }

        public bool SetVirtualLinearAcceleration(Vector3 acceleration)
        {
            if (!IsFiniteAndBounded(acceleration))
            {
                ClearVirtualLinearAcceleration();
                return false;
            }
            virtualLinearAcceleration = acceleration;
            hasVirtualAcceleration = true;
            Publish();
            return true;
        }

        public void ClearVirtualLinearAcceleration()
        {
            virtualLinearAcceleration = Vector3.zero;
            hasVirtualAcceleration = false;
            Current = default(MotionAlignmentSnapshot);
        }

        private void Update() { Publish(); }
        private void OnDisable() { Current = default(MotionAlignmentSnapshot); }

        private void Publish()
        {
            var state = hub == null ? default(VehicleMotionState) : hub.Current;
            var available = hub != null && hub.isActiveAndEnabled && hasVirtualAcceleration &&
                state.SafetyWeight > 0f &&
                (state.SafetyState == MotionSafetyState.Active || state.SafetyState == MotionSafetyState.Degraded);
            Current = Evaluate(state.LinearAcceleration, virtualLinearAcceleration, available,
                toleranceMetersPerSecondSquared, correctionGain, maximumCorrectionMetersPerSecondSquared);
        }

        public static MotionAlignmentSnapshot Evaluate(Vector3 measured, Vector3 virtualAcceleration,
            bool available, float tolerance, float gain, float maximumCorrection)
        {
            if (!available || !IsFiniteAndBounded(measured) || !IsFiniteAndBounded(virtualAcceleration))
                return default(MotionAlignmentSnapshot);

            var boundedTolerance = Mathf.Clamp(tolerance, 0.01f, 5f);
            var boundedGain = Mathf.Clamp01(gain);
            var boundedMaximum = Mathf.Clamp(maximumCorrection, 0.01f, 10f);
            var mismatch = measured - virtualAcceleration;
            var correction = Vector3.ClampMagnitude(mismatch * boundedGain, boundedMaximum);
            var magnitude = mismatch.magnitude;
            return new MotionAlignmentSnapshot(true, measured, virtualAcceleration, mismatch, correction,
                magnitude, magnitude <= boundedTolerance);
        }

        private static bool IsFiniteAndBounded(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) &&
                Mathf.Abs(value.x) <= ProtocolConstants.MaximumAccelerationComponent &&
                Mathf.Abs(value.y) <= ProtocolConstants.MaximumAccelerationComponent &&
                Mathf.Abs(value.z) <= ProtocolConstants.MaximumAccelerationComponent;
        }

        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
