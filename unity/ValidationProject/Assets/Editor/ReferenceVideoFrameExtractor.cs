using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

public static class ReferenceVideoFrameExtractor
{
    private const string PendingKey = "InertialLink.ReferenceVideo.Pending";
    private static VideoPlayer player;
    private static string outputDirectory;
    private static long[] targetFrames;
    private static int targetIndex;
    private static double deadline;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        try
        {
            var source = @"C:\Users\kokam\Downloads\ccc24200-d335-4dd9-a763-4bec9159ba51.mp4";
            if (!File.Exists(source)) throw new FileNotFoundException("Reference MP4 is missing.", source);
            outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../outputs/reference-video-analysis"));
            Directory.CreateDirectory(outputDirectory);
            SessionState.SetBool(PendingKey, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("Reference Video Extractor");
            player = host.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.renderMode = VideoRenderMode.APIOnly;
            player.source = VideoSource.Url;
            player.url = source;
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetBool(PendingKey, false);
            EditorApplication.Exit(1);
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(PendingKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) StartExtraction();
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(PendingKey, false);
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }

    private static void StartExtraction()
    {
        outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../outputs/reference-video-analysis"));
        Directory.CreateDirectory(outputDirectory);
        player = GameObject.Find("Reference Video Extractor")?.GetComponent<VideoPlayer>();
        if (player == null) { Fail(new InvalidOperationException("VideoPlayer did not survive Play Mode.")); return; }
        deadline = EditorApplication.timeSinceStartup + 45d;
        player.prepareCompleted += OnPrepared;
        player.frameReady += OnFrameReady;
        player.sendFrameReadyEvents = true;
        player.Prepare();
        EditorApplication.update += CheckDeadline;
    }

    private static void OnPrepared(VideoPlayer source)
    {
        var last = Math.Max(0L, (long)source.frameCount - 1L);
        targetFrames = new[] { 0L, last / 4L, last / 2L, last * 3L / 4L, last };
        targetIndex = 0;
        source.frame = targetFrames[0];
        source.Play();
    }

    private static void OnFrameReady(VideoPlayer source, long frame)
    {
        try
        {
            if (targetFrames == null || targetIndex >= targetFrames.Length || frame < targetFrames[targetIndex]) return;
            Capture(source.texture, targetIndex + 1);
            targetIndex++;
            if (targetIndex >= targetFrames.Length)
            {
                WriteMetadata(source);
                EditorApplication.update -= CheckDeadline;
                source.Stop();
                EditorApplication.isPlaying = false;
                return;
            }
            source.frame = targetFrames[targetIndex];
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void Capture(Texture texture, int number)
    {
        if (texture == null) throw new InvalidOperationException("Video texture is unavailable.");
        var width = texture.width;
        var height = texture.height;
        var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Graphics.Blit(texture, temporary);
        RenderTexture.active = temporary;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(outputDirectory, "frame-" + number.ToString("D2") + ".png"), image.EncodeToPNG());
        UnityEngine.Object.Destroy(image);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temporary);
    }

    private static void WriteMetadata(VideoPlayer source)
    {
        var duration = source.frameRate > 0f ? source.frameCount / source.frameRate : 0d;
        var text = "width=" + source.width + "\n" +
            "height=" + source.height + "\n" +
            "fps=" + source.frameRate.ToString("F3", CultureInfo.InvariantCulture) + "\n" +
            "frames=" + source.frameCount + "\n" +
            "duration=" + duration.ToString("F3", CultureInfo.InvariantCulture) + "\n";
        File.WriteAllText(Path.Combine(outputDirectory, "metadata.txt"), text);
        Debug.Log("Reference video frames extracted without copying the source video.");
    }

    private static void CheckDeadline()
    {
        if (EditorApplication.timeSinceStartup > deadline) Fail(new TimeoutException("Reference video extraction timed out."));
    }

    private static void Fail(Exception exception)
    {
        EditorApplication.update -= CheckDeadline;
        Debug.LogException(exception);
        SessionState.SetBool(PendingKey, false);
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        else EditorApplication.Exit(1);
    }
}
