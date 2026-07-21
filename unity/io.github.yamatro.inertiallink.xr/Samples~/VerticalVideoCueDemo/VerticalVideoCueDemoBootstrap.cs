using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace YamaTro.InertialLink.Samples
{
    /// <summary>
    /// Creates a hardware-free demonstration: a fixed 9:16 video in the center and
    /// a vehicle-synchronized curved star grid behind the protected content.
    /// </summary>
    public sealed class VerticalVideoCueDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private Camera viewer;
        [SerializeField] private VideoClip videoClip = null;
        [SerializeField, Tooltip("Optional absolute path to a local MP4/WebM/OGV/MOV file. Network URLs are refused.")]
        private string localVideoPath;

        private VehicleMotionHub hub;
        private MotionAlignmentMonitor alignmentMonitor;
        private Texture2D fallbackTexture;
        private Material runtimeVideoMaterial;
        private MeshRenderer videoRenderer;
        private GameObject videoPanel;
        private GameObject cueObject;

        public bool ConfigureLocalVideoPath(string path)
        {
            if (!IsAllowedLocalVideo(path)) return false;
            localVideoPath = Path.GetFullPath(path);
            return true;
        }

        private void Awake()
        {
            EnsureViewer();
            var source = gameObject.AddComponent<SyntheticMotionSource>();
            hub = gameObject.AddComponent<VehicleMotionHub>();
            hub.SetSource(source);

            alignmentMonitor = gameObject.AddComponent<MotionAlignmentMonitor>();
            alignmentMonitor.Configure(hub);

            CreateCentralVideoPanel();

            cueObject = new GameObject("InertialLink directional star-grid background");
            cueObject.AddComponent<MeshFilter>();
            cueObject.AddComponent<MeshRenderer>();
            cueObject.AddComponent<DirectionalMotionDome>().Configure(hub, viewer);

            var overlay = gameObject.AddComponent<VehicleMotionDiagnosticsOverlay>();
            overlay.Configure(hub, alignmentMonitor);
            Debug.Log("InertialLink vertical-video demo started. The video, Camera, and XR Origin stay fixed; only the directional star grid moves.");
        }

        private void Update()
        {
            if (hub == null || alignmentMonitor == null) return;
            // Deliberately model a 28% under-response so the alignment monitor exposes
            // a visible mismatch and a bounded correction suggestion in the demo.
            alignmentMonitor.SetVirtualLinearAcceleration(hub.Current.LinearAcceleration * 0.72f);
            if (fallbackTexture != null)
            {
                var offset = fallbackTexture.wrapMode == TextureWrapMode.Repeat ? Time.unscaledTime * 0.025f : 0f;
                if (videoRenderer != null && videoRenderer.sharedMaterial != null)
                    videoRenderer.sharedMaterial.mainTextureOffset = new Vector2(0f, offset);
            }
        }

        private void EnsureViewer()
        {
            if (viewer == null) viewer = Camera.main;
            if (viewer != null) return;
            var cameraObject = new GameObject("InertialLink Demo Camera");
            cameraObject.tag = "MainCamera";
            viewer = cameraObject.AddComponent<Camera>();
            viewer.clearFlags = CameraClearFlags.SolidColor;
            viewer.backgroundColor = new Color(0.018f, 0.028f, 0.055f);
            viewer.fieldOfView = 58f;
        }

        private void CreateCentralVideoPanel()
        {
            videoPanel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            videoPanel.name = "InertialLink 9x16 video";
            videoPanel.transform.position = viewer.transform.TransformPoint(new Vector3(0f, 0f, 2.8f));
            videoPanel.transform.rotation = viewer.transform.rotation;
            videoPanel.transform.localScale = new Vector3(0.9f, 1.6f, 1f);
            var collider = videoPanel.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            videoRenderer = videoPanel.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                runtimeVideoMaterial = new Material(shader) { name = "InertialLink local video material" };
                videoRenderer.sharedMaterial = runtimeVideoMaterial;
            }

            var player = videoPanel.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.renderMode = VideoRenderMode.MaterialOverride;
            player.targetMaterialRenderer = videoRenderer;
            player.targetMaterialProperty = "_MainTex";
            player.aspectRatio = VideoAspectRatio.FitInside;

            if (videoClip != null)
            {
                player.source = VideoSource.VideoClip;
                player.clip = videoClip;
                player.Play();
                return;
            }
            if (IsAllowedLocalVideo(localVideoPath))
            {
                player.source = VideoSource.Url;
                player.url = new Uri(Path.GetFullPath(localVideoPath)).AbsoluteUri;
                player.Play();
                return;
            }

            player.enabled = false;
            fallbackTexture = CreateFallbackTexture();
            fallbackTexture.wrapMode = TextureWrapMode.Repeat;
            if (videoRenderer.sharedMaterial != null) videoRenderer.sharedMaterial.mainTexture = fallbackTexture;
            Debug.LogWarning("InertialLink demo is using its animated fallback. Assign a VideoClip or absolute local video path to play a real vertical video.", this);
        }

        private static bool IsAllowedLocalVideo(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 4096) return false;
            if (!Path.IsPathRooted(path) || !File.Exists(path)) return false;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".mp4" || extension == ".webm" || extension == ".ogv" || extension == ".mov";
        }

        private static Texture2D CreateFallbackTexture()
        {
            var texture = new Texture2D(32, 64, TextureFormat.RGBA32, false) { name = "InertialLink animated fallback" };
            for (var y = 0; y < texture.height; y++)
            {
                var t = y / (float)(texture.height - 1);
                var color = Color.Lerp(new Color(0.06f, 0.18f, 0.3f), new Color(0.95f, 0.55f, 0.24f), t);
                for (var x = 0; x < texture.width; x++) texture.SetPixel(x, y, color);
            }
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (fallbackTexture != null) Destroy(fallbackTexture);
            if (runtimeVideoMaterial != null) Destroy(runtimeVideoMaterial);
            if (videoPanel != null) Destroy(videoPanel);
            if (cueObject != null) Destroy(cueObject);
        }
    }
}
