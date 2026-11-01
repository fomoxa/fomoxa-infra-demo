using MyGame.Backend.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyGame.Network
{
    /// <summary>
    /// Cầu nối giữa scene sảnh (Lobby) và scene trận đấu (Game): sảnh đăng nhập, chọn phòng, lấy ticket rồi
    /// <see cref="Begin"/>; scene Game được tải SAU KHI có ticket, <see cref="Launcher"/> lấy ticket ra và nối vào pod.
    /// </summary>
    public static class MatchSession
    {
        public const string LobbyScene = "Lobby";
        public const string GameScene = "Game";

        private static RoomTicket pending;

        /// <summary>Phòng đang chơi (hoặc vừa chọn).</summary>
        public static string Room { get; private set; } = "";

        /// <summary>Đang trong trận do sảnh mở (khác với chạy Host / pod trực tiếp).</summary>
        public static bool FromLobby { get; private set; }

        public static void Begin(string room, RoomTicket ticket)
        {
            Room = room;
            pending = ticket;
            FromLobby = true;
            SceneManager.LoadSceneAsync(GameScene);
        }

        public static bool TryTake(out RoomTicket ticket)
        {
            ticket = pending;
            pending = null;
            return ticket != null;
        }

        /// <summary>Rời trận và tải lại scene sảnh; thông báo hiện ở sảnh.</summary>
        public static void ReturnToLobby(string notice)
        {
            if (!FromLobby)
            {
                return;
            }

            FromLobby = false;
            Room = "";
            pending = null;

            RoomJoiner joiner = RoomJoiner.Instance;
            if (joiner != null)
            {
                joiner.LeftRoom();
                joiner.SetNotice(notice);
                _ = joiner.RefreshRooms();
            }

            SceneManager.LoadSceneAsync(LobbyScene);
        }
    }
}
