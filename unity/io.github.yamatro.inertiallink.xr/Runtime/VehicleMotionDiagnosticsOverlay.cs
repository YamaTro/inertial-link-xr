using UnityEngine;

namespace YamaTro.InertialLink
{
    [DisallowMultipleComponent]
    public sealed class VehicleMotionDiagnosticsOverlay : MonoBehaviour
    {
        [SerializeField] private VehicleMotionHub hub = null;
        [SerializeField] private MotionAlignmentMonitor alignmentMonitor = null;
        [SerializeField] private bool visible = true;

        public void Configure(VehicleMotionHub motionHub, MotionAlignmentMonitor monitor = null)
        {
            hub = motionHub;
            alignmentMonitor = monitor;
        }

        private void OnGUI()
        {
            if (!visible || hub == null) return;
            var state = hub.Current;
            var source = hub.ActiveSource;
            var text = string.Format("InertialLink XR\nSource: {0}\nSafety: {1} ({2:P0})\nTime sync: {3}\nMeasured: {4}\nAngular: {5}\nSequence: {6}",
                source == null ? "none" : source.Status, state.SafetyState, state.SafetyWeight,
                state.TimestampSynchronized ? "ready" : "pending", state.LinearAcceleration.ToString("F2"),
                state.AngularVelocity.ToString("F2"), state.Sequence);
            var height = 150f;
            if (alignmentMonitor != null)
            {
                var alignment = alignmentMonitor.Current;
                text += alignment.Available
                    ? string.Format("\nVirtual: {0}\nMismatch: {1:F2} m/s^2\nSuggested correction: {2}",
                        alignment.VirtualLinearAcceleration.ToString("F2"), alignment.MismatchMagnitude,
                        alignment.SuggestedVirtualCorrection.ToString("F2"))
                    : "\nAlignment: waiting for valid input";
                height = 210f;
            }
            GUI.Box(new Rect(12, 12, 390, height), text);
        }
    }
}
