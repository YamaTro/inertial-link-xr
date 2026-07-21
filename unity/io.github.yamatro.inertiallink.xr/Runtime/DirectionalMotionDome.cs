using System;
using UnityEngine;

namespace YamaTro.InertialLink
{
    /// <summary>
    /// Procedural star-grid background inspired by a curved horizon. Authenticated
    /// vehicle motion changes only the cue flow; the Camera, XR Origin, and content
    /// remain untouched. Cue polarity and gain require application-specific testing.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class DirectionalMotionDome : MonoBehaviour
    {
        [SerializeField] private VehicleMotionHub hub;
        [SerializeField] private Camera viewer;
        [SerializeField, Range(16, 80)] private int columns = 48;
        [SerializeField, Range(12, 72)] private int rows = 42;
        // Keep cues behind the fixed 9:16 panel used by the sample (z=2.8).
        [SerializeField, Range(2.9f, 6f)] private float nearDepthMeters = 3.2f;
        [SerializeField, Range(12f, 60f)] private float farDepthMeters = 38f;
        [SerializeField, Range(5f, 30f)] private float halfWidthMeters = 20f;
        [SerializeField, Range(-3f, -0.2f)] private float floorHeightMeters = -1.15f;
        [SerializeField, Range(0f, 0.01f)] private float horizonCurvature = 0.0025f;
        [SerializeField, Range(0.002f, 0.04f)] private float dotAngularSize = 0.009f;
        [SerializeField, Range(0f, 3f)] private float linearFlowGain = 0.9f;
        [SerializeField, Range(0f, 6f)] private float yawFlowGain = 2.2f;
        [SerializeField, Range(0.2f, 12f)] private float responsePerSecond = 4f;
        [SerializeField, Range(0.2f, 8f)] private float maximumFlowMetersPerSecond = 3.5f;
        [SerializeField, Range(-1f, 1f)] private float polarity = -1f;
        [SerializeField] private Color dotColor = new Color(0.82f, 0.92f, 1f, 0.82f);

        private Mesh mesh;
        private Material material;
        private Texture2D dotTexture;
        private Vector3[] baseCenters;
        private Vector3[] vertices;
        private Vector3 flowVelocity;
        private Vector3 flowPhase;
        private bool safeHierarchy;

        public int CueCount { get { return baseCenters == null ? 0 : baseCenters.Length; } }
        public Vector3 FlowVelocity { get { return flowVelocity; } }

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
            BuildMesh();
            EnsureMaterial();
        }

        private void OnValidate()
        {
            farDepthMeters = Mathf.Max(farDepthMeters, nearDepthMeters + 2f);
        }

        private void LateUpdate()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (!safeHierarchy || !EnvironmentMotionDriver.IsSafeTarget(transform))
            {
                if (renderer != null) renderer.enabled = false;
                enabled = false;
                Debug.LogError("InertialLink stopped DirectionalMotionDome because its hierarchy became unsafe.", this);
                return;
            }
            if (viewer == null) viewer = Camera.main;
            if (viewer == null || hub == null || !hub.isActiveAndEnabled)
            {
                if (renderer != null) renderer.enabled = false;
                return;
            }

            var state = hub.Current;
            var weight = Mathf.Clamp01(state.SafetyWeight);
            var target = EvaluateTargetFlow(state.LinearAcceleration, state.AngularVelocity,
                linearFlowGain, yawFlowGain, maximumFlowMetersPerSecond, polarity) * weight;
            var blend = 1f - Mathf.Exp(-Mathf.Max(0.2f, responsePerSecond) * Time.unscaledDeltaTime);
            flowVelocity = Vector3.Lerp(flowVelocity, target, blend);
            flowPhase += flowVelocity * Time.unscaledDeltaTime;
            flowPhase.x = Mathf.Repeat(flowPhase.x + halfWidthMeters, halfWidthMeters * 2f) - halfWidthMeters;
            flowPhase.z = Mathf.Repeat(flowPhase.z, farDepthMeters - nearDepthMeters);

            transform.position = viewer.transform.position;
            transform.rotation = viewer.transform.rotation;
            UpdateVertices();
            if (renderer != null) renderer.enabled = weight > 0.001f;
        }

        public static Vector3 EvaluateTargetFlow(Vector3 acceleration, Vector3 angularVelocity,
            float requestedLinearGain, float requestedYawGain, float requestedMaximumSpeed, float requestedPolarity)
        {
            if (!IsFinite(acceleration) || !IsFinite(angularVelocity)) return Vector3.zero;
            var gain = Mathf.Clamp(requestedLinearGain, 0f, 3f);
            var yawGain = Mathf.Clamp(requestedYawGain, 0f, 6f);
            var maximum = Mathf.Clamp(requestedMaximumSpeed, 0.2f, 8f);
            var sign = Mathf.Clamp(requestedPolarity, -1f, 1f);
            var flow = new Vector3(
                acceleration.x * gain + angularVelocity.y * yawGain,
                acceleration.y * gain * 0.2f,
                -acceleration.z * gain);
            return Vector3.ClampMagnitude(flow * sign, maximum);
        }

        public static Vector3[] BuildGroundCenters(int requestedColumns, int requestedRows,
            float requestedNearDepth, float requestedFarDepth, float requestedHalfWidth,
            float requestedFloorHeight, float requestedCurvature)
        {
            var boundedColumns = Mathf.Clamp(requestedColumns, 16, 80);
            var boundedRows = Mathf.Clamp(requestedRows, 12, 72);
            var near = Mathf.Clamp(requestedNearDepth, 2.9f, 6f);
            var far = Mathf.Clamp(requestedFarDepth, near + 2f, 60f);
            var width = Mathf.Clamp(requestedHalfWidth, 5f, 30f);
            var floor = Mathf.Clamp(requestedFloorHeight, -3f, -0.2f);
            var curve = Mathf.Clamp(requestedCurvature, 0f, 0.01f);
            var result = new Vector3[boundedColumns * boundedRows + 3];
            var index = 0;
            for (var row = 0; row < boundedRows; row++)
            {
                var depthT = row / (float)(boundedRows - 1);
                var z = Mathf.Lerp(near, far, depthT);
                for (var column = 0; column < boundedColumns; column++)
                {
                    var x = Mathf.Lerp(-width, width, column / (float)(boundedColumns - 1));
                    var y = floor - curve * x * x;
                    result[index++] = new Vector3(x, y, z);
                }
            }
            result[index++] = new Vector3(-5.6f, 2.8f, 17f);
            result[index++] = new Vector3(0f, 2.35f, 16f);
            result[index] = new Vector3(5.8f, 2.7f, 18f);
            return result;
        }

        private void BuildMesh()
        {
            baseCenters = BuildGroundCenters(columns, rows, nearDepthMeters, farDepthMeters,
                halfWidthMeters, floorHeightMeters, horizonCurvature);
            mesh = new Mesh { name = "InertialLink directional motion dome" };
            if (baseCenters.Length * 4 > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            vertices = new Vector3[baseCenters.Length * 4];
            var triangles = new int[baseCenters.Length * 6];
            var uvs = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            for (var i = 0; i < baseCenters.Length; i++)
            {
                var v = i * 4;
                uvs[v] = new Vector2(0f, 0f); uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f); uvs[v + 3] = new Vector2(0f, 1f);
                var depthFade = i >= columns * rows ? 1f : Mathf.Lerp(0.95f, 0.35f,
                    (baseCenters[i].z - nearDepthMeters) / (farDepthMeters - nearDepthMeters));
                var color = new Color(1f, 1f, 1f, depthFade);
                colors[v] = color; colors[v + 1] = color; colors[v + 2] = color; colors[v + 3] = color;
                var t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.bounds = new Bounds(new Vector3(0f, 0f, farDepthMeters * 0.5f),
                new Vector3(halfWidthMeters * 3f, 12f, farDepthMeters * 2f));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            UpdateVertices();
        }

        private void UpdateVertices()
        {
            if (mesh == null || baseCenters == null) return;
            var span = farDepthMeters - nearDepthMeters;
            for (var i = 0; i < baseCenters.Length; i++)
            {
                var center = baseCenters[i];
                if (i < columns * rows)
                {
                    center.z = nearDepthMeters + Mathf.Repeat(center.z - nearDepthMeters + flowPhase.z, span);
                    center.x = Mathf.Repeat(center.x + flowPhase.x + halfWidthMeters, halfWidthMeters * 2f) - halfWidthMeters;
                    center.y = floorHeightMeters - horizonCurvature * center.x * center.x + flowPhase.y * 0.15f;
                }
                else
                {
                    center.x += flowPhase.x * 0.04f;
                    center.y += flowPhase.y * 0.04f;
                }
                var size = dotAngularSize * Mathf.Max(1f, center.z);
                if (i >= columns * rows) size *= 4f;
                var v = i * 4;
                vertices[v] = center + new Vector3(-size, -size, 0f);
                vertices[v + 1] = center + new Vector3(size, -size, 0f);
                vertices[v + 2] = center + new Vector3(size, size, 0f);
                vertices[v + 3] = center + new Vector3(-size, size, 0f);
            }
            mesh.vertices = vertices;
            mesh.UploadMeshData(false);
        }

        private void EnsureMaterial()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial != null) return;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) throw new InvalidOperationException("No transparent unlit shader is available.");
            dotTexture = CreateDotTexture();
            material = new Material(shader) { name = "InertialLink directional star-grid material", color = dotColor };
            material.mainTexture = dotTexture;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Texture2D CreateDotTexture()
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "InertialLink soft dot" };
            for (var y = 0; y < texture.height; y++)
            for (var x = 0; x < texture.width; x++)
            {
                var dx = (x + 0.5f) / texture.width * 2f - 1f;
                var dy = (y + 0.5f) / texture.height * 2f - 1f;
                var alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                alpha *= alpha;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply(false, true);
            return texture;
        }

        private void ValidateHierarchy()
        {
            safeHierarchy = EnvironmentMotionDriver.IsSafeTarget(transform);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
            if (dotTexture != null) Destroy(dotTexture);
        }
    }
}
