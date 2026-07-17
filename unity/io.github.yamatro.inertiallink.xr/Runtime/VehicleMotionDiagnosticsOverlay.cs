using UnityEngine;

namespace YamaTro.InertialLink
{
    [DisallowMultipleComponent]
    public sealed class VehicleMotionDiagnosticsOverlay : MonoBehaviour
    {
        [SerializeField] private VehicleMotionHub hub = null;
        [SerializeField] private bool visible = true;

        private void OnGUI()
        {
            if (!visible || hub == null) return;
            var state = hub.Current;
            var source = hub.ActiveSource;
            var text = string.Format("InertialLink XR\nSource: {0}\nSafety: {1} ({2:P0})\nTime sync: {3}\nLinear: {4}\nAngular: {5}\nSequence: {6}",
                source == null ? "none" : source.Status, state.SafetyState, state.SafetyWeight,
                state.TimestampSynchronized ? "ready" : "pending", state.LinearAcceleration.ToString("F2"),
                state.AngularVelocity.ToString("F2"), state.Sequence);
            GUI.Box(new Rect(12, 12, 360, 150), text);
        }
    }
}
