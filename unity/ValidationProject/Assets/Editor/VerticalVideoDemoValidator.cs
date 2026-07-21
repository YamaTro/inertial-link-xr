using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using YamaTro.InertialLink;
using YamaTro.InertialLink.Core;

public static class VerticalVideoDemoValidator
{
    private const string PendingKey = "InertialLink.VerticalVideoValidation.Pending";
    private const string SuccessKey = "InertialLink.VerticalVideoValidation.Success";

    private static Camera camera;
    private static VideoPlayer player;
    private static VehicleMotionHub hub;
    private static Vector3 cameraStartPosition;
    private static Quaternion cameraStartRotation;
    private static string outputDirectory;
    private static double deadline;
    private static double playbackStartedAt;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        if (SessionState.GetBool(PendingKey, false) && EditorApplication.isPlaying)
            EditorApplication.delayCall += StartPlayValidation;
    }

    public static void Run()
    {
        try
        {
            outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../outputs"));
            var videoPath = Path.Combine(outputDirectory, "inertiallink-vertical-travel-loop.mp4");
            if (!File.Exists(videoPath)) throw new FileNotFoundException("Local vertical demo video is missing.", videoPath);

            SessionState.SetBool(PendingKey, true);
            SessionState.SetBool(SuccessKey, false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateDemo(videoPath);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/InertialLinkVerticalVideoValidation.unity");
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetBool(PendingKey, false);
            EditorApplication.Exit(1);
        }
    }

    private static void CreateDemo(string videoPath)
    {
        var cameraObject = new GameObject("Demo Camera");
        cameraObject.tag = "MainCamera";
        camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.018f, 0.028f, 0.055f);
        camera.fieldOfView = 58f;

        var controller = new GameObject("InertialLink Demo Controller");
        controller.AddComponent<SyntheticMotionSource>();
        controller.AddComponent<VehicleMotionHub>();

        var cueObject = new GameObject("Vehicle-synchronized margin particles");
        cueObject.AddComponent<MeshFilter>();
        cueObject.AddComponent<MeshRenderer>();
        cueObject.AddComponent<PeripheralCueMargins>();

        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "Fixed 9x16 video";
        panel.transform.position = new Vector3(0f, 0f, 2.8f);
        panel.transform.localScale = new Vector3(0.9f, 1.6f, 1f);
        var collider = panel.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        var renderer = panel.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Unlit/Texture");
        if (shader == null) throw new InvalidOperationException("Unlit/Texture shader is unavailable.");
        renderer.sharedMaterial = new Material(shader) { name = "Validated local-video material" };

        player = panel.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
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
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(PendingKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) StartPlayValidation();
        if (change != PlayModeStateChange.EnteredEditMode) return;

        var succeeded = SessionState.GetBool(SuccessKey, false);
        SessionState.SetBool(PendingKey, false);
        EditorApplication.delayCall += () => EditorApplication.Exit(succeeded ? 0 : 1);
    }

    private static void StartPlayValidation()
    {
        EditorApplication.update -= WaitForVideo;
        outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../outputs"));
        camera = GameObject.Find("Demo Camera")?.GetComponent<Camera>();
        player = GameObject.Find("Fixed 9x16 video")?.GetComponent<VideoPlayer>();
        hub = GameObject.Find("InertialLink Demo Controller")?.GetComponent<VehicleMotionHub>();
        var cues = GameObject.Find("Vehicle-synchronized margin particles")?.GetComponent<PeripheralCueMargins>();
        if (camera == null || player == null || hub == null || cues == null)
        {
            FailPlayValidation(new InvalidOperationException("The validation scene did not survive the Play Mode transition."));
            return;
        }

        cameraStartPosition = camera.transform.position;
        cameraStartRotation = camera.transform.rotation;
        cues.Configure(hub, camera);

        deadline = EditorApplication.timeSinceStartup + 30d;
        playbackStartedAt = 0d;
        player.Prepare();
        EditorApplication.update += WaitForVideo;
    }

    private static void WaitForVideo()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("Unity VideoPlayer did not prepare and play the local video within 30 seconds.");
            if (!player.isPrepared) return;
            if (hub.Current.SafetyWeight < 0.99f) return;
            if (!player.isPlaying)
            {
                player.Play();
                playbackStartedAt = EditorApplication.timeSinceStartup;
                return;
            }
            if (playbackStartedAt <= 0d) playbackStartedAt = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - playbackStartedAt < 1d) return;

            CaptureAndWriteEvidence();
            EditorApplication.update -= WaitForVideo;
            SessionState.SetBool(SuccessKey, true);
            EditorApplication.isPlaying = false;
        }
        catch (Exception exception)
        {
            FailPlayValidation(exception);
        }
    }

    private static void FailPlayValidation(Exception exception)
    {
        EditorApplication.update -= WaitForVideo;
        Debug.LogException(exception);
        SessionState.SetBool(SuccessKey, false);
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        else EditorApplication.Exit(1);
    }

    private static void CaptureAndWriteEvidence()
    {
        var cameraUnchanged = camera.transform.position == cameraStartPosition &&
            camera.transform.rotation == cameraStartRotation;
        if (!cameraUnchanged) throw new InvalidOperationException("The demo changed the camera pose.");

        var centers = PeripheralCueMargins.BuildCueCenters(4, 9, 0.72f, 1.45f, 1.05f, 2.8f);
        for (var i = 0; i < centers.Length; i++)
            if (Mathf.Abs(centers[i].x) <= 0.72f)
                throw new InvalidOperationException("A margin cue entered the protected central region.");

        var measured = hub.Current.LinearAcceleration;
        var virtualAcceleration = measured * 0.72f;
        var alignment = MotionAlignmentMonitor.Evaluate(measured, virtualAcceleration, true, 0.35f, 0.5f, 2f);
        if (!alignment.Available) throw new InvalidOperationException("Alignment evidence was unavailable.");

        var renderTexture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        var screenshot = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;
        screenshot.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        screenshot.Apply();
        File.WriteAllBytes(Path.Combine(outputDirectory, "inertiallink-vertical-video-cue-demo.png"), screenshot.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(screenshot);
        UnityEngine.Object.DestroyImmediate(renderTexture);

        var evidence = "{\n" +
            "  \"videoPrepared\": true,\n" +
            "  \"videoPlaying\": " + player.isPlaying.ToString().ToLowerInvariant() + ",\n" +
            "  \"videoFrame\": " + player.frame + ",\n" +
            "  \"videoWidth\": " + player.width + ",\n" +
            "  \"videoHeight\": " + player.height + ",\n" +
            "  \"cueCount\": " + centers.Length + ",\n" +
            "  \"protectedHalfWidthMeters\": 0.72,\n" +
            "  \"cameraPoseUnchanged\": true,\n" +
            "  \"measuredAccelerationX\": " + measured.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ",\n" +
            "  \"virtualAccelerationX\": " + virtualAcceleration.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ",\n" +
            "  \"mismatchMagnitude\": " + alignment.MismatchMagnitude.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ",\n" +
            "  \"suggestedCorrectionX\": " + alignment.SuggestedVirtualCorrection.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "}\n";
        File.WriteAllText(Path.Combine(outputDirectory, "inertiallink-vertical-video-cue-evidence.json"), evidence);
        Debug.Log("InertialLink vertical-video validation passed: local video playing, 72 cues outside the protected center, camera unchanged.");
    }

}
