using System.IO;
using Fomoxa.Networking;
using Fomoxa.Unity;
using Fomoxa.Unity.Editor;
using MyGame.Network;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DemoScene
{
    private const string ScenePath = "Assets/Scenes/Game.unity";
    private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";
    private const string PlayerBuildPath = "Builds/MyGame.exe";

    [MenuItem("Tools/Demo/Rebuild Scene")]
    public static void Build()
    {        try
        {
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Prefabs");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLight();
            CreateFloor();
            CreateCamera();

            GameObject prefab = CreatePlayerPrefab();

            var managerGo = new GameObject("Network Manager");
            var manager = managerGo.AddComponent<NetworkManager>();
            var encrypted = managerGo.AddComponent<EncryptedUdpNetworkTransport>();
            AssignTransport(manager, encrypted);

            var launcher = managerGo.AddComponent<Launcher>();
            launcher.manager = manager;
            launcher.playerPrefab = prefab.GetComponent<NetworkObject>();
            launcher.port = 7777;
            launcher.encryptedTransport = encrypted;

            // HUD trong trận (asset UI Toolkit). Sảnh nằm ở scene Lobby riêng.
            LobbyUiSetup.AddMatchHud(launcher);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            foreach (string error in FomoxaPrefabStep.RunForProject())
            {
                Debug.LogError("[Demo] prefab: " + error);
            }

            foreach (string error in FomoxaSceneStep.RunForProject())
            {
                Debug.LogError("[Demo] scene: " + error);
            }

            FomoxaPrefabStep.AssignCollected(scene, FomoxaPrefabStep.FindList());
            FomoxaSceneStep.AssignCollected(scene, FomoxaSceneStep.FindList());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            BuildLobby();

            Debug.Log("[Demo] scene built: " + ScenePath);
            Debug.Log("[Demo] prefab built: " + PrefabPath);
            Quit(0);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("[Demo] build failed: " + exception);
            Quit(1);
        }
    }

    /// <summary>
    /// Scene sảnh: camera nền tối + RoomJoiner (kết nối backend, giữ phiên đăng nhập) + giao diện sảnh (asset).
    /// Không chứa NetworkManager / sàn / nhân vật - những thứ đó chỉ được tải khi người chơi bấm vào một phòng.
    /// </summary>
    private static void BuildLobby()
    {
        Scene lobby = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Lobby Camera");
        cameraGo.tag = "MainCamera";
        var camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.07f, 0.09f, 0.14f);
        cameraGo.AddComponent<AudioListener>();

        var backendGo = new GameObject("Backend");
        var tcp = backendGo.AddComponent<TcpNetworkTransport>();
        var joiner = backendGo.AddComponent<RoomJoiner>();
        joiner.backendTransport = tcp;
        backendGo.AddComponent<BootRouter>();

        LobbyUiSetup.AddLobby(joiner);

        EditorSceneManager.SaveScene(lobby, LobbyScenePath);

        // Sảnh là scene đầu tiên của bản build; Game được tải khi bấm vào phòng.
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(LobbyScenePath, true),
            new EditorBuildSettingsScene(ScenePath, true),
        };
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Demo] lobby scene built: " + LobbyScenePath);
    }

    [MenuItem("Tools/Demo/Build Windows Player")]
    public static void BuildPlayer()
    {
        try
        {
            Directory.CreateDirectory("Builds");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { LobbyScenePath, ScenePath },
                locationPathName = PlayerBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            Debug.Log("[Demo] player build: " + report.summary.result + " -> " + PlayerBuildPath);
            Quit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("[Demo] player build failed: " + exception);
            Quit(1);
        }
    }

    private static void Quit(int code)
    {
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(code);
        }
    }

    private static void AssignTransport(NetworkManager manager, NetworkTransport transport)
    {
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("transport").objectReferenceValue = transport;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateLight()
    {
        var go = new GameObject("Directional Light");
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void CreateFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(5f, 1f, 5f);
        floor.GetComponent<Renderer>().sharedMaterial = Material("Floor", new Color(0.22f, 0.24f, 0.28f));

        for (int index = 0; index < 8; index++)
        {
            float angle = index * Mathf.PI * 2f / 8f;
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = "Pillar " + index;
            pillar.transform.localScale = new Vector3(2f, 3f, 2f);
            pillar.transform.position = new Vector3(Mathf.Sin(angle) * 20f, 1.5f, Mathf.Cos(angle) * 20f);
            pillar.GetComponent<Renderer>().sharedMaterial = Material("Pillar", new Color(0.75f, 0.4f, 0.2f));
        }
    }

    private static void CreateCamera()
    {
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 60f;
        camera.farClipPlane = 200f;
        go.AddComponent<AudioListener>();
        go.AddComponent<PlayerCamera>();
        go.transform.position = new Vector3(0f, 14f, -14f);
        go.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
    }

    private static GameObject CreatePlayerPrefab()
    {
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        template.name = "Player";
        template.transform.position = new Vector3(0f, 1f, 0f);
        template.GetComponent<Renderer>().sharedMaterial = Material("Player", new Color(0.25f, 0.6f, 1f));

        template.AddComponent<NetworkObject>();
        template.AddComponent<NetworkTransform>();
        template.AddComponent<MyGame.Network.Player>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(template, PrefabPath);
        Object.DestroyImmediate(template);
        return prefab;
    }

    private static Material Material(string name, Color color)
    {
        string path = "Assets/Materials/" + name + ".mat";
        EnsureFolder("Assets/Materials");

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        material.color = color;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, name);
    }
}
