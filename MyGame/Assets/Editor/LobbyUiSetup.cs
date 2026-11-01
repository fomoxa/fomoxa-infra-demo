using MyGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Gắn giao diện sảnh (UI Toolkit: Assets/UI/Lobby.uxml + Lobby.uss) vào scene hiện tại.
/// Menu: Tools/Demo/Add Lobby UI. "Rebuild Scene" cũng gọi hàm này.
/// </summary>
public static class LobbyUiSetup
{
    private const string UxmlPath = "Assets/UI/Lobby.uxml";
    private const string ThemePath = "Assets/UI/LobbyTheme.tss";
    private const string PanelSettingsPath = "Assets/UI/LobbyPanelSettings.asset";

    [MenuItem("Tools/Demo/Add Lobby UI")]
    public static void AddToOpenScene()
    {
        var launcher = Object.FindFirstObjectByType<Launcher>();
        var joiner = Object.FindFirstObjectByType<RoomJoiner>();
        if (launcher == null || joiner == null)
        {
            Debug.LogError("[Lobby] scene thieu Launcher/RoomJoiner. Chay Tools/Demo/Rebuild Scene truoc.");
            return;
        }

        AddTo(launcher, joiner);
        EditorSceneManager.MarkSceneDirty(launcher.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[Lobby] da them Lobby UI vao scene");
    }

    public static void AddTo(Launcher launcher, RoomJoiner joiner)
    {
        var existing = Object.FindFirstObjectByType<LobbyScreen>();
        GameObject go = existing != null ? existing.gameObject : new GameObject("Lobby UI");

        var document = go.GetComponent<UIDocument>();
        if (document == null)
        {
            document = go.AddComponent<UIDocument>();
        }

        document.panelSettings = EnsurePanelSettings();
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        if (document.visualTreeAsset == null)
        {
            Debug.LogError("[Lobby] khong thay " + UxmlPath);
        }

        var screen = go.GetComponent<LobbyScreen>();
        if (screen == null)
        {
            screen = go.AddComponent<LobbyScreen>();
        }

        screen.launcher = launcher;
        screen.joiner = joiner;
    }

    private static PanelSettings EnsurePanelSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
        }

        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1280, 720);
        settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        settings.match = 0.5f;
        settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }
}
