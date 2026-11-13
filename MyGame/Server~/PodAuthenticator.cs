using System;
using System.Collections.Concurrent;
using System.Text;
using Fomoxa.Networking;

namespace MyGame.Server
{
    /// <summary>
    /// Ghi nhớ ai là ai trên pod: ticket do backend cấp mang theo tên người chơi (ApplicationData) và được
    /// chuyển cho pod dưới dạng ConnectionData. Pod nhận ngay mọi kết nối đã qua kiểm tra ticket; chỉ thêm việc
    /// gắn peer id với tên để báo bảng điểm.
    /// </summary>
    internal sealed class PodAuthenticator : IServerAuthenticator
    {
        private readonly ConcurrentDictionary<ulong, string> players = new ConcurrentDictionary<ulong, string>();

        public void RegisterServer(ServerAuthentication server)
        {
        }

        public void OnServerAuthenticating(ServerAuthenticationContext context)
        {
            string name = context.ConnectionData.IsEmpty ? "" : Encoding.UTF8.GetString(context.ConnectionData.Span);
            players[context.PeerId] = name;
            Console.WriteLine($"[Pod] peer {context.PeerId} is '{(name.Length == 0 ? "(guest)" : name)}'");
            context.Accept();
        }

        public string NameOf(ulong peerId) => players.TryGetValue(peerId, out string name) ? name : "";

        public void Forget(ulong peerId) => players.TryRemove(peerId, out _);
    }
}
