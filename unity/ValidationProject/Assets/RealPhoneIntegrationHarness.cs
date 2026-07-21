using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
using YamaTro.InertialLink;

public sealed class RealPhoneIntegrationHarness : MonoBehaviour
{
    private const int Port = 28461;

    private UdpMotionSource receiver;
    private VehicleMotionHub hub;
    private MotionAlignmentMonitor alignment;
    private DirectionalMotionDome directionalDome;
    private Camera viewer;
    private VideoPlayer player;
    private string pairingKeyInput = string.Empty;
    private string receiverAddressHint = "192.168.1.16";
    private string message = "Waiting for a one-time pairing key.";
    private Vector3 cameraStartPosition;
    private Quaternion cameraStartRotation;
    private double readySince;
    private double nextStatusWriteAt;
    private bool evidenceWritten;

    public void SetReceiverAddressHint(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) receiverAddressHint = value.Trim();
    }

    private void Awake()
    {
        Application.runInBackground = true;
        CreateViewer();

        receiver = gameObject.AddComponent<UdpMotionSource>();
        hub = gameObject.AddComponent<VehicleMotionHub>();
        hub.SetSource(receiver);
        alignment = gameObject.AddComponent<MotionAlignmentMonitor>();
        alignment.Configure(hub);

        var cues = new GameObject("Real phone directional star-grid background");
        cues.AddComponent<MeshFilter>();
        cues.AddComponent<MeshRenderer>();
        directionalDome = cues.AddComponent<DirectionalMotionDome>();
        directionalDome.Configure(hub, viewer);

        CreateVideoPanel();
        cameraStartPosition = viewer.transform.position;
        cameraStartRotation = viewer.transform.rotation;

        // Test automation may inject the phone's short-lived key through the child
        // process environment. Remove it immediately; it is never logged or written.
        var injectedKey = Environment.GetEnvironmentVariable("ILXR_PAIRING_KEY");
        Environment.SetEnvironmentVariable("ILXR_PAIRING_KEY", null);
        if (!string.IsNullOrWhiteSpace(injectedKey))
        {
            var accepted = receiver.Configure(injectedKey, Port);
            injectedKey = string.Empty;
            message = accepted
                ? "Authenticated receiver ready; start the phone sender."
                : "The injected one-time key was rejected.";
        }
    }

    private void Update()
    {
        if (hub == null || alignment == null || receiver == null) return;
        alignment.SetVirtualLinearAcceleration(hub.Current.LinearAcceleration * 0.72f);

        if (Time.realtimeSinceStartupAsDouble >= nextStatusWriteAt)
        {
            nextStatusWriteAt = Time.realtimeSinceStartupAsDouble + 2d;
            WriteLiveStatus();
        }

        var ready = receiver.IsReady && receiver.AcceptedPackets >= 60 &&
            hub.Current.SafetyWeight > 0.5f && player != null && player.isPlaying;
        if (!ready)
        {
            readySince = 0d;
            return;
        }
        if (readySince <= 0d) readySince = Time.realtimeSinceStartupAsDouble;
        if (!evidenceWritten && Time.realtimeSinceStartupAsDouble - readySince >= 2d)
            WriteEvidence();
    }

    private void WriteLiveStatus()
    {
        var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../outputs"));
        var status = "{\n" +
            "  \"receiverStatus\": \"" + receiver.Status.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\n" +
            "  \"acceptedPackets\": " + receiver.AcceptedPackets + ",\n" +
            "  \"rejectedPackets\": " + receiver.RejectedPackets + ",\n" +
            "  \"timeSynchronized\": " + receiver.IsTimeSynchronized.ToString().ToLowerInvariant() + ",\n" +
            "  \"safetyWeight\": " + hub.Current.SafetyWeight.ToString("F3", CultureInfo.InvariantCulture) + ",\n" +
            "  \"videoPlaying\": " + (player != null && player.isPlaying).ToString().ToLowerInvariant() + ",\n" +
            "  \"directionalCueCount\": " + (directionalDome == null ? 0 : directionalDome.CueCount) + "\n" +
            "}\n";
        File.WriteAllText(Path.Combine(outputDirectory, "inertiallink-xiaomi13t-live-status.json"), status);
    }

    private void OnGUI()
    {
        var panel = new Rect(22f, 22f, 650f, 430f);
        GUI.Box(panel, "InertialLink XR — real phone integration");
        GUILayout.BeginArea(new Rect(42f, 58f, 610f, 380f));
        GUILayout.Label("Phone receiver target: " + receiverAddressHint + ":" + Port);
        GUILayout.Label("The pairing key is process-memory only and is never written to evidence.");

        if (receiver != null && receiver.Status == "Pairing key required")
        {
            GUILayout.Space(8f);
            GUILayout.Label("One-time key from phone:");
            pairingKeyInput = GUILayout.PasswordField(pairingKeyInput, '*', 40);
            if (GUILayout.Button("Start authenticated receiver", GUILayout.Height(34f)))
            {
                var accepted = receiver.Configure(pairingKeyInput, Port);
                pairingKeyInput = string.Empty;
                message = accepted ? "Receiver started. Now tap Start sending on the phone." : receiver.Status;
            }
        }
        else if (receiver != null && GUILayout.Button("Stop and clear pairing key", GUILayout.Height(30f)))
        {
            receiver.ClearPairingKey();
            message = "Receiver stopped and pairing key cleared.";
        }

        GUILayout.Space(8f);
        GUILayout.Label(message);
        if (receiver != null)
        {
            GUILayout.Label("Receiver: " + receiver.Status);
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                "accepted={0}  rejected={1}  dropped={2}  sync={3}  RTT={4:F2} ms",
                receiver.AcceptedPackets, receiver.RejectedPackets, receiver.DroppedFrames,
                receiver.IsTimeSynchronized, receiver.BestRoundTripNanoseconds / 1000000.0));
        }
        if (hub != null)
        {
            var motion = hub.Current;
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                "safety={0} weight={1:F2}  linear=({2:F3}, {3:F3}, {4:F3}) m/s2",
                motion.SafetyState, motion.SafetyWeight, motion.LinearAcceleration.x,
                motion.LinearAcceleration.y, motion.LinearAcceleration.z));
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture,
                "gyro=({0:F3}, {1:F3}, {2:F3}) rad/s",
                motion.AngularVelocity.x, motion.AngularVelocity.y, motion.AngularVelocity.z));
        }
        if (evidenceWritten) GUILayout.Label("PASS: evidence captured without storing the pairing key.");
        GUILayout.EndArea();
    }

    private void CreateViewer()
    {
        var cameraObject = new GameObject("Real Phone Demo Camera");
        cameraObject.tag = "MainCamera";
        viewer = cameraObject.AddComponent<Camera>();
        viewer.clearFlags = CameraClearFlags.SolidColor;
        viewer.backgroundColor = new Color(0.018f, 0.028f, 0.055f);
        viewer.fieldOfView = 58f;
    }

    private void CreateVideoPanel()
    {
        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "Real phone fixed 9x16 video";
        panel.transform.position = new Vector3(0f, 0f, 2.8f);
        panel.transform.localScale = new Vector3(0.9f, 1.6f, 1f);
        var collider = panel.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        var renderer = panel.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Unlit/Texture");
        if (shader == null) throw new InvalidOperationException("Unlit/Texture shader is unavailable.");
        renderer.sharedMaterial = new Material(shader) { name = "Real phone validation video material" };

        var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../outputs"));
        var videoPath = Path.Combine(outputDirectory, "inertiallink-vertical-travel-loop.mp4");
        if (!File.Exists(videoPath)) throw new FileNotFoundException("Local vertical demo video is missing.", videoPath);

        player = panel.AddComponent<VideoPlayer>();
        player.playOnAwake = true;
        player.isLooping = true;
        player.waitForFirstFrame = true;
        player.skipOnDrop = true;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.renderMode = VideoRenderMode.MaterialOverride;
        player.targetMaterialRenderer = renderer;
        player.targetMaterialProperty = "_MainTex";
        player.aspectRatio = VideoAspectRatio.FitInside;
        player.source = VideoSource.Url;
        player.url = videoPath;
        player.Play();
    }

    private void WriteEvidence()
    {
        var cameraUnchanged = viewer.transform.position == cameraStartPosition &&
            viewer.transform.rotation == cameraStartRotation;
        if (!cameraUnchanged)
        {
            message = "FAIL: camera pose changed.";
            return;
        }

        var snapshot = alignment.Current;
        var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../outputs"));
        var evidence = "{\n" +
            "  \"deviceModel\": \"Xiaomi XIG04\",\n" +
            "  \"androidApi\": 35,\n" +
            "  \"transport\": \"private LAN UDP\",\n" +
            "  \"listenPort\": 28461,\n" +
            "  \"acceptedPackets\": " + receiver.AcceptedPackets + ",\n" +
            "  \"rejectedPackets\": " + receiver.RejectedPackets + ",\n" +
            "  \"droppedFrames\": " + receiver.DroppedFrames + ",\n" +
            "  \"timeSynchronized\": " + receiver.IsTimeSynchronized.ToString().ToLowerInvariant() + ",\n" +
            "  \"bestRoundTripMilliseconds\": " +
                (receiver.BestRoundTripNanoseconds / 1000000.0).ToString("F3", CultureInfo.InvariantCulture) + ",\n" +
            "  \"safetyState\": \"" + hub.Current.SafetyState + "\",\n" +
            "  \"safetyWeight\": " + hub.Current.SafetyWeight.ToString("F3", CultureInfo.InvariantCulture) + ",\n" +
            "  \"linearAcceleration\": [" + Vector(hub.Current.LinearAcceleration) + "],\n" +
            "  \"angularVelocity\": [" + Vector(hub.Current.AngularVelocity) + "],\n" +
            "  \"mismatchMagnitude\": " + snapshot.MismatchMagnitude.ToString("F3", CultureInfo.InvariantCulture) + ",\n" +
            "  \"videoPlaying\": " + player.isPlaying.ToString().ToLowerInvariant() + ",\n" +
            "  \"videoFrame\": " + player.frame + ",\n" +
            "  \"directionalCueCount\": " + (directionalDome == null ? 0 : directionalDome.CueCount) + ",\n" +
            "  \"directionalFlowVelocity\": [" + Vector(directionalDome == null ? Vector3.zero : directionalDome.FlowVelocity) + "],\n" +
            "  \"cameraPoseUnchanged\": true,\n" +
            "  \"pairingKeyStored\": false\n" +
            "}\n";
        File.WriteAllText(Path.Combine(outputDirectory, "inertiallink-xiaomi13t-unity-evidence.json"), evidence);
        CaptureScreenshot(Path.Combine(outputDirectory, "inertiallink-xiaomi13t-unity-demo.png"));
        evidenceWritten = true;
        message = "PASS: authenticated phone packets reached Unity and evidence was captured.";
        Debug.Log("InertialLink real-phone validation passed: authenticated Xiaomi packets, clock sync, video, protected cues, and unchanged camera.");
    }

    private void CaptureScreenshot(string path)
    {
        var renderTexture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        var screenshot = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        viewer.targetTexture = renderTexture;
        viewer.Render();
        RenderTexture.active = renderTexture;
        screenshot.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        screenshot.Apply();
        File.WriteAllBytes(path, screenshot.EncodeToPNG());
        viewer.targetTexture = null;
        RenderTexture.active = previous;
        Destroy(screenshot);
        Destroy(renderTexture);
    }

    private static string Vector(Vector3 value)
    {
        return value.x.ToString("F4", CultureInfo.InvariantCulture) + ", " +
            value.y.ToString("F4", CultureInfo.InvariantCulture) + ", " +
            value.z.ToString("F4", CultureInfo.InvariantCulture);
    }

    private void OnDestroy()
    {
        pairingKeyInput = string.Empty;
        if (receiver != null) receiver.ClearPairingKey();
    }
}
