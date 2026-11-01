using System;
using MyGame.Backend.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace MyGame.Network
{
    /// <summary>
    /// Giao diện sảnh dựng từ asset UI Toolkit (Assets/UI/Lobby.uxml + Lobby.uss): đăng nhập / đăng ký / quên mật khẩu,
    /// danh sách phòng, bạn bè và lời mời, đổi mật khẩu, người đang online.
    /// Chỉ điều khiển hiển thị; mọi thao tác gọi qua <see cref="RoomJoiner"/>. Vào phòng xong, RoomJoiner mới tải scene
    /// trận đấu (xem <see cref="MatchSession"/>).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyScreen : MonoBehaviour
    {
        private enum AuthMode { Login, Register, Recover }

        private enum LobbyTab { Rooms, Friends, Account }

        public RoomJoiner joiner;

        private VisualElement authPanel, recoveryPanel, lobbyPanel, onlinePanel;
        private VisualElement roomsTab, friendsTab, accountTab;
        private Button tabLogin, tabRegister, tabRecover, tabRooms, tabFriends, tabAccount;
        private TextField nameField, passwordField, recoveryField, recoveryCode, friendField, oldPasswordField, newPasswordField;
        private Button authButton, recoveryOk, refreshButton, logoutButton, addFriendButton, changePasswordButton;
        private Label authHint, welcome, profileLabel, status, emptyRooms, emptyFriends, onlineTitle, invitesTitle, incomingTitle;
        private ScrollView roomList, friendList, onlineList;
        private VisualElement inviteList, incomingList;

        private AuthMode authMode = AuthMode.Login;
        private LobbyTab lobbyTab = LobbyTab.Rooms;
        private string lastSignature = "";
        private int builtRooms = -1, builtOnline = -1, builtSocial = -1;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            authPanel = root.Q<VisualElement>("auth-panel");
            recoveryPanel = root.Q<VisualElement>("recovery-panel");
            lobbyPanel = root.Q<VisualElement>("lobby-panel");
            onlinePanel = root.Q<VisualElement>("online-panel");
            roomsTab = root.Q<VisualElement>("rooms-tab");
            friendsTab = root.Q<VisualElement>("friends-tab");
            accountTab = root.Q<VisualElement>("account-tab");
            tabLogin = root.Q<Button>("tab-login");
            tabRegister = root.Q<Button>("tab-register");
            tabRecover = root.Q<Button>("tab-recover");
            tabRooms = root.Q<Button>("tab-rooms");
            tabFriends = root.Q<Button>("tab-friends");
            tabAccount = root.Q<Button>("tab-account");
            nameField = root.Q<TextField>("name-field");
            passwordField = root.Q<TextField>("password-field");
            recoveryField = root.Q<TextField>("recovery-field");
            recoveryCode = root.Q<TextField>("recovery-code");
            friendField = root.Q<TextField>("friend-field");
            oldPasswordField = root.Q<TextField>("old-password-field");
            newPasswordField = root.Q<TextField>("new-password-field");
            authButton = root.Q<Button>("auth-button");
            recoveryOk = root.Q<Button>("recovery-ok");
            refreshButton = root.Q<Button>("refresh-button");
            logoutButton = root.Q<Button>("logout-button");
            addFriendButton = root.Q<Button>("add-friend-button");
            changePasswordButton = root.Q<Button>("change-password-button");
            authHint = root.Q<Label>("auth-hint");
            welcome = root.Q<Label>("welcome");
            profileLabel = root.Q<Label>("profile-label");
            status = root.Q<Label>("status");
            emptyRooms = root.Q<Label>("empty-rooms");
            emptyFriends = root.Q<Label>("empty-friends");
            onlineTitle = root.Q<Label>("online-title");
            invitesTitle = root.Q<Label>("invites-title");
            incomingTitle = root.Q<Label>("incoming-title");
            roomList = root.Q<ScrollView>("room-list");
            friendList = root.Q<ScrollView>("friend-list");
            onlineList = root.Q<ScrollView>("online-list");
            inviteList = root.Q<VisualElement>("invite-list");
            incomingList = root.Q<VisualElement>("incoming-list");

            if (string.IsNullOrEmpty(nameField.value))
            {
                nameField.value = "ana";
            }

            tabLogin.clicked += () => SetAuthMode(AuthMode.Login);
            tabRegister.clicked += () => SetAuthMode(AuthMode.Register);
            tabRecover.clicked += () => SetAuthMode(AuthMode.Recover);
            tabRooms.clicked += () => SetLobbyTab(LobbyTab.Rooms);
            tabFriends.clicked += () => SetLobbyTab(LobbyTab.Friends);
            tabAccount.clicked += () => SetLobbyTab(LobbyTab.Account);

            authButton.clicked += Authenticate;
            recoveryOk.clicked += () => joiner?.AcknowledgeRecoveryCode();
            refreshButton.clicked += () => _ = joiner.RefreshRooms();
            logoutButton.clicked += () =>
            {
                passwordField.value = "";
                joiner.Logout();
            };
            addFriendButton.clicked += AddFriend;
            changePasswordButton.clicked += ChangePassword;

            nameField.RegisterCallback<KeyDownEvent>(SubmitOnEnter);
            passwordField.RegisterCallback<KeyDownEvent>(SubmitOnEnter);
            recoveryField.RegisterCallback<KeyDownEvent>(SubmitOnEnter);

            lastSignature = "";
            builtRooms = builtOnline = builtSocial = -1;
        }

        private void SetAuthMode(AuthMode mode)
        {
            authMode = mode;
            lastSignature = "";
        }

        private void SetLobbyTab(LobbyTab tab)
        {
            lobbyTab = tab;
            lastSignature = "";
        }

        private void SubmitOnEnter(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                Authenticate();
                evt.StopPropagation();
            }
        }

        private void Authenticate()
        {
            if (joiner == null || joiner.Busy || joiner.LoggedIn)
            {
                return;
            }

            switch (authMode)
            {
                case AuthMode.Register:
                    _ = joiner.Register(nameField.value, passwordField.value);
                    break;
                case AuthMode.Recover:
                    _ = joiner.Recover(nameField.value, recoveryField.value, passwordField.value);
                    break;
                default:
                    _ = joiner.Login(nameField.value, passwordField.value);
                    break;
            }
        }

        private void AddFriend()
        {
            string target = (friendField.value ?? "").Trim();
            if (target.Length > 0)
            {
                _ = joiner.FriendAction("request", target);
                friendField.value = "";
            }
        }

        private void ChangePassword()
        {
            _ = joiner.ChangePassword(oldPasswordField.value, newPasswordField.value);
            oldPasswordField.value = "";
            newPasswordField.value = "";
        }

        private void Update()
        {
            // RoomJoiner sống xuyên scene: sau khi quay về sảnh, tham chiếu trong scene trỏ tới bản trùng đã bị hủy.
            if (RoomJoiner.Instance != null)
            {
                joiner = RoomJoiner.Instance;
            }

            if (joiner == null)
            {
                return;
            }

            string signature = $"{joiner.LoggedIn}|{joiner.Busy}|{joiner.LoggedInAs}|{joiner.Status}|{joiner.IsError}|{joiner.RoomsVersion}|{joiner.OnlineVersion}|{joiner.SocialVersion}|{joiner.ProfileVersion}|{joiner.NewRecoveryCode}|{authMode}|{lobbyTab}";
            if (signature == lastSignature)
            {
                return;
            }

            lastSignature = signature;
            bool loggedIn = joiner.LoggedIn;
            bool showCode = loggedIn && !string.IsNullOrEmpty(joiner.NewRecoveryCode);

            Show(authPanel, !loggedIn);
            Show(recoveryPanel, showCode);
            Show(lobbyPanel, loggedIn && !showCode);
            Show(onlinePanel, loggedIn && !showCode);
            authPanel.SetEnabled(!joiner.Busy);
            lobbyPanel.SetEnabled(!joiner.Busy);

            DrawAuth();
            if (showCode)
            {
                recoveryCode.value = joiner.NewRecoveryCode;
            }

            if (loggedIn)
            {
                welcome.text = $"Xin chào, {joiner.LoggedInAs}";
                profileLabel.text = joiner.Profile == null
                    ? ""
                    : $"Kỷ lục: {joiner.Profile.BestSteps} bậc  ·  Số trận: {joiner.Profile.Matches}";
                SelectTab(tabRooms, lobbyTab == LobbyTab.Rooms);
                SelectTab(tabFriends, lobbyTab == LobbyTab.Friends);
                SelectTab(tabAccount, lobbyTab == LobbyTab.Account);
                Show(roomsTab, lobbyTab == LobbyTab.Rooms);
                Show(friendsTab, lobbyTab == LobbyTab.Friends);
                Show(accountTab, lobbyTab == LobbyTab.Account);
            }

            status.text = joiner.Status ?? "";
            status.EnableInClassList("error", joiner.IsError);

            if (builtRooms != joiner.RoomsVersion)
            {
                builtRooms = joiner.RoomsVersion;
                RebuildRooms();
            }

            if (builtOnline != joiner.OnlineVersion)
            {
                builtOnline = joiner.OnlineVersion;
                RebuildOnline();
            }

            if (builtSocial != joiner.SocialVersion)
            {
                builtSocial = joiner.SocialVersion;
                RebuildSocial();
            }
        }

        private void DrawAuth()
        {
            SelectTab(tabLogin, authMode == AuthMode.Login);
            SelectTab(tabRegister, authMode == AuthMode.Register);
            SelectTab(tabRecover, authMode == AuthMode.Recover);
            Show(recoveryField, authMode == AuthMode.Recover);
            passwordField.label = authMode == AuthMode.Recover ? "Mật khẩu mới" : "Mật khẩu";

            switch (authMode)
            {
                case AuthMode.Register:
                    authButton.text = "Tạo tài khoản";
                    authHint.text = "Tên 3-24 ký tự (chữ, số, _ hoặc -). Mật khẩu tối thiểu 8 ký tự. Bạn sẽ nhận một mã khôi phục - hãy lưu lại.";
                    break;
                case AuthMode.Recover:
                    authButton.text = "Đặt lại mật khẩu";
                    authHint.text = "Nhập tên, mã khôi phục đã nhận lúc đăng ký và mật khẩu mới.";
                    break;
                default:
                    authButton.text = "Đăng nhập";
                    authHint.text = "Chưa có tài khoản? Chọn thẻ Đăng ký.";
                    break;
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

        private void RebuildSocial()
        {
            inviteList.Clear();
            incomingList.Clear();
            friendList.Clear();

            SocialState social = joiner.Social;
            int invites = social?.Invites.Count ?? 0;
            int incoming = social?.Incoming.Count ?? 0;
            int friends = social?.Friends.Count ?? 0;
            Show(invitesTitle, invites > 0);
            Show(incomingTitle, incoming > 0);
            Show(emptyFriends, friends == 0);
            tabFriends.text = invites + incoming > 0 ? $"Bạn bè ({invites + incoming})" : "Bạn bè";
            if (social == null)
            {
                return;
            }

            foreach (InviteInfo invite in social.Invites)
            {
                string from = invite.From;
                string room = invite.Room;
                VisualElement banner = Banner($"{from} mời bạn vào {room}", false);
                banner.Add(SmallButton("Tham gia", () =>
                {
                    _ = joiner.FriendAction("dismiss-invite", from);
                    _ = joiner.JoinRoom(room);
                }));
                banner.Add(SmallButton("Bỏ qua", () => _ = joiner.FriendAction("dismiss-invite", from)));
                inviteList.Add(banner);
            }

            foreach (string who in social.Incoming)
            {
                string other = who;
                VisualElement banner = Banner($"{other} muốn kết bạn", true);
                banner.Add(SmallButton("Chấp nhận", () => _ = joiner.FriendAction("accept", other)));
                banner.Add(SmallButton("Từ chối", () => _ = joiner.FriendAction("decline", other)));
                incomingList.Add(banner);
            }

            foreach (FriendInfo friend in social.Friends)
            {
                string other = friend.Name;
                string room = friend.Room;
                var row = new VisualElement();
                row.AddToClassList("friend-row");

                var dot = new VisualElement();
                dot.AddToClassList("online-dot");
                dot.style.opacity = friend.Online ? 1f : 0.25f;
                row.Add(dot);

                var name = new Label($"{friend.Name}  ·  {friend.BestSteps} bậc");
                name.AddToClassList("friend-name");
                row.Add(name);

                string place = !friend.Online ? "offline" : string.IsNullOrEmpty(friend.Room) ? "ở sảnh" : $"trong {friend.Room}";
                var placeLabel = new Label(place);
                placeLabel.AddToClassList("friend-place");
                if (friend.Online)
                {
                    placeLabel.AddToClassList("online");
                }

                row.Add(placeLabel);

                if (friend.Online && !string.IsNullOrEmpty(room))
                {
                    row.Add(SmallButton("Tham gia", () => _ = joiner.JoinRoom(room)));
                }

                row.Add(SmallButton("Xoá", () => _ = joiner.FriendAction("remove", other)));
                friendList.Add(row);
            }
        }

        private static VisualElement Banner(string text, bool request)
        {
            var banner = new VisualElement();
            banner.AddToClassList("banner");
            if (request)
            {
                banner.AddToClassList("request");
            }

            var label = new Label(text);
            label.AddToClassList("friend-name");
            banner.Add(label);
            return banner;
        }

        private static Button SmallButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("button");
            button.AddToClassList("small");
            return button;
        }

        private void Join(string roomKey)
        {
            _ = joiner.JoinRoom(roomKey);
        }

        private static void SelectTab(Button tab, bool selected) => tab?.EnableInClassList("selected", selected);

        private static void Show(VisualElement element, bool visible)
        {
            if (element != null)
            {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
