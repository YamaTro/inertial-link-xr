using UnityEngine;

namespace YamaTro.InertialLink
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class PeripheralCueField : MonoBehaviour
    {
        [SerializeField] private VehicleMotionHub hub;
        [SerializeField] private Camera viewer;
        [SerializeField, Range(8, 128)] private int cueCount = 40;
        [SerializeField, Range(0.5f, 5f)] private float distanceMeters = 2f;
        [SerializeField, Range(0.5f, 2f)] private float ringRadiusMeters = 1.35f;
        [SerializeField, Range(0.005f, 0.08f)] private float cueSizeMeters = 0.025f;
        [SerializeField] private Color cueColor = new Color(0.85f, 0.95f, 1f, 0.7f);
        [SerializeField, Range(0f, 0.08f)] private float displacementPerMeterPerSecondSquared = 0.018f;
        [SerializeField, Range(0f, 4f)] private float rotationDegreesPerRadianPerSecond = 1.5f;
        [SerializeField] private float polarity = -1f;

        private Mesh generatedMesh;
        private Material generatedMaterial;
        private bool safeHierarchy;
        private bool hierarchyViolationLogged;

        public void Configure(VehicleMotionHub motionHub, Camera viewingCamera)
        {
            hub = motionHub;
            viewer = viewingCamera;
            ValidateHierarchy();
        }

        private void Awake()
        {
            if (viewer == null) viewer = Camera.main;
            ValidateHierarchy();
            GenerateMesh();
            EnsureMaterial();
        }

        private void OnValidate()
        {
            cueCount = Mathf.Clamp(cueCount, 8, 128);
            if (Application.isPlaying) GenerateMesh();
        }

        private void LateUpdate()
        {
            if (!safeHierarchy || !EnvironmentMotionDriver.IsSafeTarget(transform))
            {
                var unsafeRenderer = GetComponent<MeshRenderer>();
                if (unsafeRenderer != null) unsafeRenderer.enabled = false;
                safeHierarchy = false;
                if (!hierarchyViolationLogged)
                {
                    Debug.LogError("InertialLink stopped PeripheralCueField because its hierarchy became unsafe.", this);
                    hierarchyViolationLogged = true;
                }
                enabled = false;
                return;
            }
            if (hub == null || !hub.isActiveAndEnabled)
            {
                var inactiveRenderer = GetComponent<MeshRenderer>();
                if (inactiveRenderer != null) inactiveRenderer.enabled = false;
                return;
            }
            if (viewer == null) viewer = Camera.main;
            if (viewer == null)
            {
                var missingViewerRenderer = GetComponent<MeshRenderer>();
                if (missingViewerRenderer != null) missingViewerRenderer.enabled = false;
                return;
            }
            var state = hub == null ? default(VehicleMotionState) : hub.Current;
            var weight = state.SafetyWeight;
            var localOffset = Vector3.ClampMagnitude(state.LinearAcceleration * (polarity * displacementPerMeterPerSecondSquared * weight), 0.12f);
            var rotation = Vector3.ClampMagnitude(state.AngularVelocity * (polarity * rotationDegreesPerRadianPerSecond * weight), 6f);

            // This object follows the view, but never writes to the Camera or XR Origin.
            transform.position = viewer.transform.TransformPoint(localOffset);
            transform.rotation = viewer.transform.rotation * Quaternion.Euler(rotation);
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = weight > 0.001f;
        }

        private void OnTransformParentChanged() { ValidateHierarchy(); }
        private void OnTransformChildrenChanged() { ValidateHierarchy(); }

        private void ValidateHierarchy()
        {
            safeHierarchy = EnvironmentMotionDriver.IsSafeTarget(transform);
            if (!safeHierarchy && !hierarchyViolationLogged)
            {
                Debug.LogError("InertialLink refused to animate PeripheralCueField inside or around a Camera/XR Origin hierarchy.", this);
                hierarchyViolationLogged = true;
            }
            else if (safeHierarchy)
            {
                hierarchyViolationLogged = false;
            }
        }

        private void GenerateMesh()
        {
            if (generatedMesh != null) Destroy(generatedMesh);
            generatedMesh = new Mesh { name = "InertialLink peripheral cues" };
            var boundedCueCount = Mathf.Clamp(cueCount, 8, 128);
            var vertices = new Vector3[boundedCueCount * 4];
            var triangles = new int[boundedCueCount * 6];
            var uvs = new Vector2[vertices.Length];
            for (var i = 0; i < boundedCueCount; i++)
            {
                var angle = (Mathf.PI * 2f * i) / boundedCueCount;
                var center = new Vector3(Mathf.Cos(angle) * ringRadiusMeters, Mathf.Sin(angle) * ringRadiusMeters, distanceMeters);
                var v = i * 4;
                vertices[v] = center + new Vector3(-cueSizeMeters, -cueSizeMeters, 0f);
                vertices[v + 1] = center + new Vector3(cueSizeMeters, -cueSizeMeters, 0f);
                vertices[v + 2] = center + new Vector3(cueSizeMeters, cueSizeMeters, 0f);
                vertices[v + 3] = center + new Vector3(-cueSizeMeters, cueSizeMeters, 0f);
                uvs[v] = new Vector2(0f, 0f); uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f); uvs[v + 3] = new Vector2(0f, 1f);
                var t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            generatedMesh.vertices = vertices;
            generatedMesh.triangles = triangles;
            generatedMesh.uv = uvs;
            generatedMesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = generatedMesh;
        }

        private void EnsureMaterial()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != null) return;
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("InertialLink could not find a built-in unlit shader. Assign a material to PeripheralCueField.", this);
                return;
            }
            generatedMaterial = new Material(shader) { name = "InertialLink cue material", color = cueColor };
            renderer.sharedMaterial = generatedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void OnDestroy()
        {
            if (generatedMesh != null) Destroy(generatedMesh);
            if (generatedMaterial != null) Destroy(generatedMaterial);
        }
    }
}
