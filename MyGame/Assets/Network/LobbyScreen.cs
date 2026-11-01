using System;
using MyGame.Backend.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace MyGame.Network
{
    /// <summary>
    /// Giao diện sảnh dựng từ asset UI Toolkit (Assets/UI/Lobby.uxml + Lobby.uss), thay cho IMGUI vẽ bằng code.
    /// Chỉ điều khiển hiển thị: đăng nhập / làm mới / vào phòng đều gọi qua <see cref="RoomJoiner"/> và <see cref="Launcher"/>.
    /// Đổi bố cục hay màu sắc: sửa Lobby.uxml / Lobby.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyScreen : MonoBehaviour
    {
        /// <summary>Có LobbyScreen đang chạy thì Launcher không vẽ giao diện IMGUI cũ nữa.</summary>
        public static bool Active { get; private set; }

        public Launcher launcher;
        public RoomJoiner joiner;

        private VisualElement loginPanel;
        private VisualElement lobbyPanel;
        private VisualElement gamePanel;
        private TextField nameField;
        private TextField passwordField;
        private Button loginButton;
        private Button refreshButton;
        private Button logoutButton;
        private Button leaveButton;
        private Button hostButton;
        private Button podButton;
        private Label welcome;
        private Label gameInfo;
        private Label status;
        private Label emptyRooms;
        private ScrollView roomList;
        private VisualElement onlinePanel;
        private Label onlineTitle;
        private ScrollView onlineList;
        private int builtOnlineVersion = -1;
        private Foldout devTools;

        private string lastSignature = "";
        private int builtRoomsVersion = -1;

        private void OnEnable()
        {
            Active = true;
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            loginPanel = root.Q<VisualElement>("login-panel");
            lobbyPanel = root.Q<VisualElement>("lobby-panel");
            gamePanel = root.Q<VisualElement>("game-panel");
            nameField = root.Q<TextField>("name-field");
            passwordField = root.Q<TextField>("password-field");
            loginButton = root.Q<Button>("login-button");
            refreshButton = root.Q<Button>("refresh-button");
            logoutButton = root.Q<Button>("logout-button");
            leaveButton = root.Q<Button>("leave-button");
            hostButton = root.Q<Button>("host-button");
            podButton = root.Q<Button>("pod-button");
            welcome = root.Q<Label>("welcome");
            gameInfo = root.Q<Label>("game-info");
            status = root.Q<Label>("status");
            emptyRooms = root.Q<Label>("empty-rooms");
            roomList = root.Q<ScrollView>("room-list");
            devTools = root.Q<Foldout>("dev-tools");
            onlinePanel = root.Q<VisualElement>("online-panel");
            onlineTitle = root.Q<Label>("online-title");
            onlineList = root.Q<ScrollView>("online-list");

            if (nameField != null && string.IsNullOrEmpty(nameField.value))
            {
                nameField.value = "ana";
            }

            loginButton.clicked += Login;
            refreshButton.clicked += () => _ = joiner.RefreshRooms();
            logoutButton.clicked += () =>
            {
                passwordField.value = "";
                joiner.Logout();
            };
            leaveButton.clicked += () => launcher.LeaveRoom();
            hostButton.clicked += () => launcher.BeginHost();
            podButton.clicked += () => launcher.BeginPod();

            // Enter ở ô nhập = đăng nhập
            nameField.RegisterCallback<KeyDownEvent>(SubmitOnEnter);
            passwordField.RegisterCallback<KeyDownEvent>(SubmitOnEnter);

            lastSignature = "";
            builtRoomsVersion = -1;
            builtOnlineVersion = -1;
        }

        private void OnDisable()
        {
            Active = false;
        }

        private void SubmitOnEnter(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                Login();
                evt.StopPropagation();
            }
        }

        private void Login()
        {
            if (joiner != null && !joiner.Busy && !joiner.LoggedIn)
            {
                _ = joiner.Login(nameField.value, passwordField.value);
            }
        }

        private void Update()
        {
            if (joiner == null || launcher == null)
            {
                return;
            }

            bool inGame = launcher.ClientRunning || launcher.ServerRunning;
            bool loggedIn = joiner.LoggedIn;
            bool busy = joiner.Busy;

            string signature = $"{inGame}|{loggedIn}|{busy}|{joiner.LoggedInAs}|{joiner.Status}|{joiner.IsError}|{joiner.RoomsVersion}|{joiner.OnlineVersion}|{launcher.CurrentRoom}";
            if (signature == lastSignature)
            {
                return;
            }

            lastSignature = signature;

            Show(loginPanel, !inGame && !loggedIn);
            Show(lobbyPanel, !inGame && loggedIn);
            Show(gamePanel, inGame);
            Show(devTools, !inGame);
            Show(onlinePanel, loggedIn);

            loginPanel.SetEnabled(!busy);
            lobbyPanel.SetEnabled(!busy);

            welcome.text = $"Xin chào, {joiner.LoggedInAs}";
            gameInfo.text = string.IsNullOrEmpty(launcher.CurrentRoom)
                ? "Đang trong game"
                : $"Phòng {launcher.CurrentRoom}";
            leaveButton.text = launcher.ServerRunning ? "Dừng server" : "Thoát phòng (về sảnh)";

            status.text = joiner.Status ?? "";
            status.EnableInClassList("error", joiner.IsError);

            if (builtOnlineVersion != joiner.OnlineVersion)
            {
                builtOnlineVersion = joiner.OnlineVersion;
                RebuildOnline();
            }

            if (builtRoomsVersion != joiner.RoomsVersion)
            {
                builtRoomsVersion = joiner.RoomsVersion;
                RebuildRooms();
            }
        }

        private void RebuildRooms()
        {
            roomList.Clear();
            foreach (RoomInfo room in joiner.Rooms)
            {
                roomList.Add(RoomRow(room));
            }

            Show(emptyRooms, joiner.Rooms.Count == 0);
        }

        private void RebuildOnline()
        {
            onlineList.Clear();
            onlineTitle.text = $"Đang online ({joiner.Online.Count})";
            foreach (OnlinePlayer player in joiner.Online)
            {
                var row = new VisualElement();
                row.AddToClassList("online-row");

                var dot = new VisualElement();
                dot.AddToClassList("online-dot");
                row.Add(dot);

                var name = new Label(player.Name);
                name.AddToClassList("online-name");
                row.Add(name);

                var place = new Label(string.IsNullOrEmpty(player.Room) ? "ở sảnh" : $"trong {player.Room}");
                place.AddToClassList("online-place");
                row.Add(place);

                onlineList.Add(row);
            }
        }

        private VisualElement RoomRow(RoomInfo room)
        {
            string key = room.RoomKey;
            var row = new Button(() => Join(key));
            row.AddToClassList("room-row");

            var name = new Label(key == "auto" ? "Tự động ghép trận" : key);
            name.AddToClassList("room-name");
            row.Add(name);

            var count = new Label($"{room.Players}/{room.Capacity}");
            count.AddToClassList("room-count");
            row.Add(count);

            string chipClass = room.State == "full" ? "chip-full" : room.State == "waiting" ? "chip-waiting" : "chip-open";
            string chipText = room.State == "full" ? "Đầy" : room.State == "waiting" ? "Có người chờ" : "Trống";
            var chip = new Label(chipText);
            chip.AddToClassList("room-chip");
            chip.AddToClassList(chipClass);
            row.Add(chip);
            return row;
        }

        private void Join(string roomKey)
        {
            launcher.SelectRoom(roomKey);
            _ = joiner.JoinRoom(roomKey);
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
