using MyGame.Backend.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace MyGame.Network
{
    /// <summary>
    /// HUD trong scene trận đấu (Assets/UI/MatchHud.uxml): tên phòng, trạng thái kết nối, nút thoát về sảnh,
    /// và vài nút dev (Host / pod) khi mở scene Game trực tiếp mà không đi qua sảnh.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudScreen : MonoBehaviour
    {
        public Launcher launcher;

        private VisualElement matchPanel;
        private VisualElement devPanel;
        private Label matchTitle;
        private Label matchState;
        private Label status;
        private Button leaveButton;
        private Button lobbyButton;
        private ScrollView scoreList;
        private Label scoreEmpty;
        private Label inviteTitle;
        private VisualElement inviteFriends;
        private int builtBoard = -1;
        private int builtSocial = -1;
        private string lastSignature = "";

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            matchPanel = root.Q<VisualElement>("match-panel");
            devPanel = root.Q<VisualElement>("dev-panel");
            matchTitle = root.Q<Label>("match-title");
            matchState = root.Q<Label>("match-state");
            status = root.Q<Label>("status");
            leaveButton = root.Q<Button>("leave-button");
            lobbyButton = root.Q<Button>("lobby-button");
            scoreList = root.Q<ScrollView>("score-list");
            scoreEmpty = root.Q<Label>("score-empty");
            inviteTitle = root.Q<Label>("invite-title");
            inviteFriends = root.Q<VisualElement>("invite-friends");

            leaveButton.clicked += () => launcher.LeaveRoom();
            root.Q<Button>("host-button").clicked += () => launcher.BeginHost();
            root.Q<Button>("pod-button").clicked += () => launcher.BeginPod();
            lobbyButton.clicked += () =>
            {
                // Scene Game mở trực tiếp: nếu đã từng đăng nhập thì về sảnh, không thì tải scene sảnh để đăng nhập.
                UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(MatchSession.LobbyScene);
            };
            lastSignature = "";
            builtBoard = builtSocial = -1;
        }

        private void Update()
        {
            if (launcher == null)
            {
                return;
            }

            bool running = launcher.ClientRunning || launcher.ServerRunning;
            RoomJoiner joiner = RoomJoiner.Instance;
            string notice = joiner != null ? joiner.Status ?? "" : "";
            bool error = joiner != null && joiner.IsError;

            string summary = running ? launcher.ConnectionSummary + "|" + launcher.PodSummary : "";
            int boardVersion = joiner != null ? joiner.BoardVersion : 0;
            int socialVersion = joiner != null ? joiner.SocialVersion : 0;
            string signature = $"{running}|{MatchSession.FromLobby}|{MatchSession.Room}|{summary}|{notice}|{error}|{boardVersion}|{socialVersion}";
            if (signature == lastSignature)
            {
                return;
            }

            lastSignature = signature;

            Show(matchPanel, running);
            Show(devPanel, !running && !MatchSession.FromLobby);

            matchTitle.text = string.IsNullOrEmpty(MatchSession.Room) ? "Đang trong game" : $"Phòng {MatchSession.Room}";
            matchState.text = launcher.PodRunning ? launcher.PodSummary + "\n" + launcher.ConnectionSummary : (running ? launcher.ConnectionSummary : "");
            leaveButton.text = MatchSession.FromLobby ? "Thoát phòng (về sảnh)" : "Dừng";

            status.text = notice;
            status.EnableInClassList("error", error);

            if (joiner != null && builtBoard != joiner.BoardVersion)
            {
                builtBoard = joiner.BoardVersion;
                RebuildBoard(joiner);
            }

            if (joiner != null && builtSocial != joiner.SocialVersion)
            {
                builtSocial = joiner.SocialVersion;
                RebuildInvites(joiner);
            }
        }

        private void RebuildBoard(RoomJoiner joiner)
        {
            scoreList.Clear();
            int rank = 0;
            foreach (ScoreEntry entry in joiner.Board)
            {
                rank++;
                var row = new VisualElement();
                row.AddToClassList("score-row");
                if (string.Equals(entry.Name, joiner.LoggedInAs, System.StringComparison.OrdinalIgnoreCase))
                {
                    row.AddToClassList("me");
                }

                var rankLabel = new Label(rank.ToString());
                rankLabel.AddToClassList("score-rank");
                row.Add(rankLabel);

                var name = new Label(entry.Name);
                name.AddToClassList("score-name");
                row.Add(name);

                var steps = new Label($"{entry.Steps} bậc");
                steps.AddToClassList("score-steps");
                row.Add(steps);

                var best = new Label($"kỷ lục {entry.Best}");
                best.AddToClassList("score-best");
                row.Add(best);
                scoreList.Add(row);
            }

            Show(scoreEmpty, joiner.Board.Count == 0);
        }

        /// <summary>Bạn đang online mà chưa ở phòng này: nút Mời.</summary>
        private void RebuildInvites(RoomJoiner joiner)
        {
            inviteFriends.Clear();
            int count = 0;
            if (joiner.Social != null)
            {
                foreach (FriendInfo friend in joiner.Social.Friends)
                {
                    if (!friend.Online || friend.Room == MatchSession.Room)
                    {
                        continue;
                    }

                    string who = friend.Name;
                    var row = new VisualElement();
                    row.AddToClassList("friend-row");
                    var name = new Label(friend.Name);
                    name.AddToClassList("friend-name");
                    row.Add(name);
                    var invite = new Button(() => _ = joiner.Invite(who)) { text = "Mời" };
                    invite.AddToClassList("button");
                    invite.AddToClassList("small");
                    row.Add(invite);
                    inviteFriends.Add(row);
                    count++;
                }
            }

            Show(inviteTitle, count > 0);
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
