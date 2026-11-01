using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyGame.Network
{
    /// <summary>
    /// Đặt trong scene sảnh (scene đầu tiên của bản build). Các chế độ dòng lệnh không cần sảnh
    /// (-pod, -host, -client) bỏ qua sảnh và vào thẳng scene Game như trước.
    /// </summary>
    public sealed class BootRouter : MonoBehaviour
    {
        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool autojoin = Array.IndexOf(args, "-autojoin") >= 0;
            bool direct = Array.IndexOf(args, "-pod") >= 0
                || Array.IndexOf(args, "-host") >= 0
                || (!autojoin && Array.IndexOf(args, "-client") >= 0);

            if (direct)
            {
                SceneManager.LoadScene(MatchSession.GameScene);
            }
        }
    }
}
