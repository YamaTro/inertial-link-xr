using UnityEngine;

namespace YamaTro.InertialLink
{
    /// <summary>Draws motion cues in left/right margins while protecting central content.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class PeripheralCueMargins : MonoBehaviour
    {
        [SerializeField] private VehicleMotionHub hub;
        [SerializeField] private Camera viewer;
        [SerializeField, Range(1, 12)] private int columnsPerSide = 4;
        [SerializeField, Range(2, 20)] private int rows = 9;
        [SerializeField, Range(0.2f, 2f)] private float protectedHalfWidthMeters = 0.72f;
        [SerializeField, Range(0.4f, 4f)] private float fieldHalfWidthMeters = 1.45f;
        [SerializeField, Range(0.4f, 3f)] private float verticalHalfHeightMeters = 1.05f;
        [SerializeField, Range(0.5f, 5f)] private float distanceMeters = 2.8f;
        [SerializeField, Range(0.005f, 0.08f)] private float cueSizeMeters = 0.022f;
        [SerializeField] private Color cueColor = new Color(0.55f, 0.9f, 1f, 0.62f);
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
            columnsPerSide = Mathf.Clamp(columnsPerSide, 1, 12);
            rows = Mathf.Clamp(rows, 2, 20);
            fieldHalfWidthMeters = Mathf.Max(fieldHalfWidthMeters, protectedHalfWidthMeters + 0.05f);
        }

        private void LateUpdate()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (!safeHierarchy || !EnvironmentMotionDriver.IsSafeTarget(transform))
            {
                if (renderer != null) renderer.enabled = false;
                safeHierarchy = false;
                if (!hierarchyViolationLogged)
                {
                    Debug.LogError("InertialLink stopped PeripheralCueMargins because its hierarchy became unsafe.", this);
                    hierarchyViolationLogged = true;
                }
                enabled = false;
                return;
            }
            if (hub == null || !hub.isActiveAndEnabled)
            {
                if (renderer != null) renderer.enabled = false;
                return;
            }
            if (viewer == null) viewer = Camera.main;
            if (viewer == null)
            {
                if (renderer != null) renderer.enabled = false;
                return;
            }

            var state = hub.Current;
            var weight = state.SafetyWeight;
            var localOffset = Vector3.ClampMagnitude(state.LinearAcceleration *
                (polarity * displacementPerMeterPerSecondSquared * weight), 0.12f);
            var rotation = Vector3.ClampMagnitude(state.AngularVelocity *
                (polarity * rotationDegreesPerRadianPerSecond * weight), 6f);
            transform.position = viewer.transform.TransformPoint(localOffset);
            transform.rotation = viewer.transform.rotation * Quaternion.Euler(rotation);
            if (renderer != null) renderer.enabled = weight > 0.001f;
        }

        private void OnTransformParentChanged() { ValidateHierarchy(); }
        private void OnTransformChildrenChanged() { ValidateHierarchy(); }

        private void ValidateHierarchy()
        {
            safeHierarchy = EnvironmentMotionDriver.IsSafeTarget(transform);
            if (!safeHierarchy && !hierarchyViolationLogged)
            {
                Debug.LogError("InertialLink refused to animate PeripheralCueMargins inside or around a Camera/XR Origin hierarchy.", this);
                hierarchyViolationLogged = true;
            }
            else if (safeHierarchy) hierarchyViolationLogged = false;
        }

        private void GenerateMesh()
        {
            if (generatedMesh != null) Destroy(generatedMesh);
            generatedMesh = new Mesh { name = "InertialLink protected-margin cues" };
            var centers = BuildCueCenters(columnsPerSide, rows, protectedHalfWidthMeters,
                fieldHalfWidthMeters, verticalHalfHeightMeters, distanceMeters);
            var vertices = new Vector3[centers.Length * 4];
            var triangles = new int[centers.Length * 6];
            var uvs = new Vector2[vertices.Length];
            for (var i = 0; i < centers.Length; i++)
            {
                var v = i * 4;
                vertices[v] = centers[i] + new Vector3(-cueSizeMeters, -cueSizeMeters, 0f);
                vertices[v + 1] = centers[i] + new Vector3(cueSizeMeters, -cueSizeMeters, 0f);
                vertices[v + 2] = centers[i] + new Vector3(cueSizeMeters, cueSizeMeters, 0f);
                vertices[v + 3] = centers[i] + new Vector3(-cueSizeMeters, cueSizeMeters, 0f);
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

        public static Vector3[] BuildCueCenters(int requestedColumnsPerSide, int requestedRows,
            float requestedProtectedHalfWidth, float requestedFieldHalfWidth,
            float requestedVerticalHalfHeight, float requestedDistance)
        {
            var columns = Mathf.Clamp(requestedColumnsPerSide, 1, 12);
            var boundedRows = Mathf.Clamp(requestedRows, 2, 20);
            var protectedWidth = Mathf.Clamp(requestedProtectedHalfWidth, 0.2f, 2f);
            var fieldWidth = Mathf.Clamp(requestedFieldHalfWidth, protectedWidth + 0.05f, 4f);
            var verticalHeight = Mathf.Clamp(requestedVerticalHalfHeight, 0.4f, 3f);
            var distance = Mathf.Clamp(requestedDistance, 0.5f, 5f);
            var result = new Vector3[columns * boundedRows * 2];
            var index = 0;
            for (var side = -1; side <= 1; side += 2)
            {
                for (var column = 0; column < columns; column++)
                {
                    var x = Mathf.Lerp(protectedWidth, fieldWidth, (column + 0.5f) / columns) * side;
                    for (var row = 0; row < boundedRows; row++)
                    {
                        var y = Mathf.Lerp(-verticalHeight, verticalHeight, (row + 0.5f) / boundedRows);
                        result[index++] = new Vector3(x, y, distance);
                    }
                }
            }
            return result;
        }

        private void EnsureMaterial()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != null) return;
            var shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("InertialLink could not find a built-in unlit shader. Assign a material to PeripheralCueMargins.", this);
                return;
            }
            generatedMaterial = new Material(shader) { name = "InertialLink margin cue material", color = cueColor };
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
