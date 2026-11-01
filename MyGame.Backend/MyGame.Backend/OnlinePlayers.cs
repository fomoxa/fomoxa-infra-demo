using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MyGame.Backend.Models;

namespace MyGame.Backend
{
    /// <summary>
    /// Ai đang online: client gọi Lobby.Presence định kỳ (cả khi đang trong phòng, vì kết nối backend vẫn mở).
    /// Quá <see cref="Timeout"/> không thấy tín hiệu thì coi là offline.
    /// </summary>
    public sealed class OnlinePlayers
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        private readonly ConcurrentDictionary<string, (string Name, string Room, DateTime Seen)> players =
            new ConcurrentDictionary<string, (string, string, DateTime)>();

        public void Touch(string principal, string name, string room) =>
            players[principal] = (name, room ?? "", DateTime.UtcNow);

        public OnlineList Snapshot()
        {
            DateTime now = DateTime.UtcNow;
            var list = new OnlineList();
            foreach (var pair in players.ToArray())
            {
                if (now - pair.Value.Seen > Timeout)
                {
                    players.TryRemove(pair.Key, out _);
                    continue;
                }

                list.Players.Add(new OnlinePlayer { Name = pair.Value.Name, Room = pair.Value.Room });
            }

            list.Players = list.Players.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return list;
        }
    }
}
