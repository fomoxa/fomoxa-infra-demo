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

            leaveButton.clicked += () => launcher.LeaveRoom();
            root.Q<Button>("host-button").clicked += () => launcher.BeginHost();
            root.Q<Button>("pod-button").clicked += () => launcher.BeginPod();
            lobbyButton.clicked += () =>
            {
                // Scene Game mở trực tiếp: nếu đã từng đăng nhập thì về sảnh, không thì tải scene sảnh để đăng nhập.
                UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(MatchSession.LobbyScene);
            };
            lastSignature = "";
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
            string signature = $"{running}|{MatchSession.FromLobby}|{MatchSession.Room}|{summary}|{notice}|{error}";
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
