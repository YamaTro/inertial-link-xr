using UnityEngine;

namespace YamaTro.InertialLink.Samples
{
    public sealed class SafeMotionCueBootstrap : MonoBehaviour
    {
        [SerializeField] private Camera viewer;

        private void Awake()
        {
            if (viewer == null) viewer = Camera.main;
            var source = gameObject.AddComponent<SyntheticMotionSource>();
            var hub = gameObject.AddComponent<VehicleMotionHub>();
            hub.SetSource(source);

            var environment = new GameObject("InertialLink Visual Content");
            for (var x = -4; x <= 4; x++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Horizon marker";
                marker.transform.SetParent(environment.transform, false);
                marker.transform.localPosition = new Vector3(x, -1.5f, 5f);
                marker.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
            }

            var driver = gameObject.AddComponent<EnvironmentMotionDriver>();
            driver.Configure(hub, environment.transform);

            var cues = new GameObject("InertialLink Peripheral Cues");
            cues.AddComponent<MeshFilter>();
            cues.AddComponent<MeshRenderer>();
            var cueField = cues.AddComponent<PeripheralCueField>();
            cueField.Configure(hub, viewer);
            Debug.Log("InertialLink safe demo started with a synthetic source. The Camera/XR Origin is never moved.");
        }
    }
}
