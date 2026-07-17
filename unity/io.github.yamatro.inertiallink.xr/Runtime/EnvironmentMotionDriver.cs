using UnityEngine;
using YamaTro.InertialLink.Core;

namespace YamaTro.InertialLink
{
    [DisallowMultipleComponent]
    public sealed class EnvironmentMotionDriver : MonoBehaviour
    {
        [SerializeField] private VehicleMotionHub hub;
        [Tooltip("Only this transform is moved. It must not contain a Camera or XR Origin.")]
        [SerializeField] private Transform contentRoot;
        [SerializeField] private float polarity = -1f;
        [SerializeField, Range(0f, 0.05f)] private float translationMetersPerMeterPerSecondSquared = 0.012f;
        [SerializeField, Range(0f, 5f)] private float rotationDegreesPerRadianPerSecond = 1.25f;
        [SerializeField, Range(0f, 0.25f)] private float maximumTranslationMeters = 0.06f;
        [SerializeField, Range(0f, 10f)] private float maximumRotationDegrees = 4f;
        [SerializeField, Range(0.01f, 1f)] private float responseSeconds = 0.12f;

        private Vector3 baseLocalPosition;
        private Quaternion baseLocalRotation;
        private Vector3 positionVelocity;
        private bool safeTarget;
        private bool hasBasePose;
        private bool safetyViolationLogged;

        public Transform ContentRoot { get { return contentRoot; } }

        public bool Configure(VehicleMotionHub motionHub, Transform assignedContentRoot)
        {
            RestoreNeutralIfSafe();
            positionVelocity = Vector3.zero;
            hub = motionHub;
            contentRoot = assignedContentRoot;
            return CaptureAndValidateTarget();
        }

        private void Awake() { CaptureAndValidateTarget(); }
        private void OnEnable() { CaptureAndValidateTarget(); }

        private void LateUpdate()
        {
            if (contentRoot == null) return;
            if (!safeTarget || !IsSafeTarget(contentRoot))
            {
                FailClosed();
                return;
            }
            if (hub == null || !hub.isActiveAndEnabled)
            {
                RestoreNeutralPose();
                return;
            }
            var state = hub.Current;
            var weight = state.SafetyWeight;
            if (weight <= 0f || (state.SafetyState != MotionSafetyState.Active &&
                                state.SafetyState != MotionSafetyState.Degraded))
            {
                RestoreNeutralPose();
                return;
            }
            var offset = Vector3.ClampMagnitude(state.LinearAcceleration * (polarity * translationMetersPerMeterPerSecondSquared * weight),
                maximumTranslationMeters);
            var rotationVector = Vector3.ClampMagnitude(state.AngularVelocity * (polarity * rotationDegreesPerRadianPerSecond * weight),
                maximumRotationDegrees);
            var targetPosition = baseLocalPosition + offset;
            var targetRotation = baseLocalRotation * Quaternion.Euler(rotationVector);
            contentRoot.localPosition = Vector3.SmoothDamp(contentRoot.localPosition, targetPosition, ref positionVelocity,
                Mathf.Max(0.01f, responseSeconds), Mathf.Infinity, Time.unscaledDeltaTime);
            contentRoot.localRotation = Quaternion.Slerp(contentRoot.localRotation, targetRotation,
                1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, responseSeconds)));
        }

        private void OnDisable()
        {
            RestoreNeutralIfSafe();
        }

        private bool CaptureAndValidateTarget()
        {
            hasBasePose = contentRoot != null;
            if (hasBasePose)
            {
                baseLocalPosition = contentRoot.localPosition;
                baseLocalRotation = contentRoot.localRotation;
            }
            safeTarget = IsSafeTarget(contentRoot);
            if (!safeTarget)
            {
                if (contentRoot != null && !safetyViolationLogged)
                {
                    Debug.LogError("InertialLink refused to move contentRoot because it contains a Camera or XR Origin. Assign a separate visual-content root.", this);
                    safetyViolationLogged = true;
                }
                return false;
            }
            safetyViolationLogged = false;
            return true;
        }

        private void FailClosed()
        {
            // Once the hierarchy contains or sits under tracked objects, never write that
            // transform again—not even to restore a cached pose.
            positionVelocity = Vector3.zero;
            safeTarget = false;
            if (!safetyViolationLogged)
            {
                Debug.LogError("InertialLink stopped EnvironmentMotionDriver because its content hierarchy became unsafe.", this);
                safetyViolationLogged = true;
            }
            enabled = false;
        }

        private void RestoreNeutralIfSafe()
        {
            if (!hasBasePose || contentRoot == null || !safeTarget || !IsSafeTarget(contentRoot)) return;
            RestoreNeutralPose();
        }

        private void RestoreNeutralPose()
        {
            if (!hasBasePose || contentRoot == null) return;
            contentRoot.localPosition = baseLocalPosition;
            contentRoot.localRotation = baseLocalRotation;
            positionVelocity = Vector3.zero;
        }

        public static bool IsSafeTarget(Transform candidate)
        {
            if (candidate == null) return false;
            if (candidate.GetComponentInChildren<Camera>(true) != null) return false;
            var behaviours = candidate.GetComponentsInChildren<MonoBehaviour>(true);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var type = behaviours[i] == null ? null : behaviours[i].GetType();
                if (type != null && (type.Name == "XROrigin" || type.FullName == "Unity.XR.CoreUtils.XROrigin")) return false;
            }
            var ancestor = candidate.parent;
            while (ancestor != null)
            {
                if (ancestor.GetComponent<Camera>() != null) return false;
                var ancestorBehaviours = ancestor.GetComponents<MonoBehaviour>();
                for (var i = 0; i < ancestorBehaviours.Length; i++)
                {
                    var type = ancestorBehaviours[i] == null ? null : ancestorBehaviours[i].GetType();
                    if (type != null && (type.Name == "XROrigin" || type.FullName == "Unity.XR.CoreUtils.XROrigin")) return false;
                }
                ancestor = ancestor.parent;
            }
            return true;
        }
    }
}
