using MyGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Gắn giao diện (UI Toolkit) vào scene:
///   - scene Lobby: Assets/UI/Lobby.uxml  (đăng nhập, danh sách phòng, người online)
///   - scene Game : Assets/UI/MatchHud.uxml (HUD trong trận)
/// Menu: Tools/Demo/Add Lobby UI (scene đang mở). "Rebuild Scene" gọi sẵn cho cả hai scene.
/// </summary>
public static class LobbyUiSetup
{
    private const string LobbyUxml = "Assets/UI/Lobby.uxml";
    private const string HudUxml = "Assets/UI/MatchHud.uxml";
    private const string ThemePath = "Assets/UI/LobbyTheme.tss";
    private const string PanelSettingsPath = "Assets/UI/LobbyPanelSettings.asset";

    [MenuItem("Tools/Demo/Add Lobby UI")]
    public static void AddToOpenScene()
    {
        var joiner = Object.FindFirstObjectByType<RoomJoiner>();
        var launcher = Object.FindFirstObjectByType<Launcher>();
        if (joiner == null && launcher == null)
        {
            Debug.LogError("[Lobby] scene khong co RoomJoiner (scene Lobby) hay Launcher (scene Game). Chay Tools/Demo/Rebuild Scene.");
            return;
        }

        if (joiner != null)
        {
            AddLobby(joiner);
        }

        if (launcher != null)
        {
            AddMatchHud(launcher);
        }

        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[Lobby] da gan giao dien vao scene dang mo");
    }

    public static void AddLobby(RoomJoiner joiner)
    {
        GameObject go = UiObject<LobbyScreen>("Lobby UI", LobbyUxml);
        go.GetComponent<LobbyScreen>().joiner = joiner;
    }

    public static void AddMatchHud(Launcher launcher)
    {
        GameObject go = UiObject<MatchHudScreen>("Match HUD", HudUxml);
        go.GetComponent<MatchHudScreen>().launcher = launcher;
    }

    private static GameObject UiObject<T>(string name, string uxmlPath) where T : MonoBehaviour
    {
        var existing = Object.FindFirstObjectByType<T>();
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);

        var document = go.GetComponent<UIDocument>();
        if (document == null)
        {
            document = go.AddComponent<UIDocument>();
        }

        document.panelSettings = EnsurePanelSettings();
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
        if (document.visualTreeAsset == null)
        {
            Debug.LogError("[Lobby] khong thay " + uxmlPath);
        }

        if (go.GetComponent<T>() == null)
        {
            go.AddComponent<T>();
        }

        return go;
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
