using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RealPhoneIntegrationLauncher
{
    [MenuItem("InertialLink/Run Xiaomi 13T Integration Test")]
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var controller = new GameObject("InertialLink Real Phone Test");
        var harness = controller.AddComponent<RealPhoneIntegrationHarness>();
        harness.SetReceiverAddressHint("192.168.1.16");
        Selection.activeGameObject = controller;
        EditorApplication.isPlaying = true;
    }
}
