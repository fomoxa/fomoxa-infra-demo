using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fomoxa.Rpc;
using MyGame.Backend.Accounts;
using MyGame.Backend.Models;

namespace MyGame.Backend
{
    /// <summary>
    /// Phần "sảnh" dùng chung giữa backend thật và cụm giả: đổi mật khẩu, hồ sơ, bạn bè, lời mời vào phòng và bảng điểm.
    /// Principal của người chơi có tài khoản chính là tên chuẩn hoá (chữ thường).
    /// </summary>
    public sealed class LobbyCore
    {
        private static readonly TimeSpan InviteLifetime = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan BoardLifetime = TimeSpan.FromSeconds(15);

        private readonly AccountService accounts;
        private readonly OnlinePlayers online;
        private readonly ConcurrentDictionary<string, List<(string From, string Room, DateTime Expires)>> invites =
            new ConcurrentDictionary<string, List<(string, string, DateTime)>>();
        private readonly object boardGate = new object();
        private readonly Dictionary<string, Board> boards = new Dictionary<string, Board>();

        private sealed class Board
        {
            public DateTime Seen;
            public readonly Dictionary<string, ScoreEntry> Rows = new Dictionary<string, ScoreEntry>();
        }

        private readonly AccountAuth auth;
        public Metrics? Metrics { get; set; }

        public LobbyCore(AccountService accounts, OnlinePlayers online, AccountAuth auth)
        {
            this.accounts = accounts;
            this.online = online;
            this.auth = auth;
        }

        public ActionResult TakeRecoveryCode(string principal)
        {
            string code = auth.TakeRecoveryCode(principal);
            return code.Length == 0
                ? new ActionResult { Ok = false, Message = "" }
                : new ActionResult { Ok = true, Message = code };
        }

        public AccountService Accounts => accounts;

        // ------------------------------------------------------------ tài khoản

        public async Task<ActionResult> ChangePassword(string principal, ChangePasswordRequest request)
        {
            await Guard(() => accounts.ChangePasswordAsync(principal, request.OldPassword, request.NewPassword));
            Metrics?.Count("password_changes_total");
            return new ActionResult { Ok = true, Message = "đã đổi mật khẩu" };
        }

        public async Task<PlayerProfile> GetProfile(string principal, ProfileRequest request)
        {
            string target = string.IsNullOrWhiteSpace(request.Player) ? principal : request.Player;
            ProfileView view = await Guard(() => accounts.GetProfileAsync(target));
            return new PlayerProfile { Name = view.Name, BestSteps = view.BestSteps, Matches = view.Matches, CreatedUnix = view.CreatedUnix };
        }

        // ------------------------------------------------------------ bạn bè & lời mời

        public async Task<SocialState> Social(string principal)
        {
            SocialRecord record = await Guard(() => accounts.GetSocialAsync(principal));
            var state = new SocialState();
            foreach (string friend in record.Friends)
            {
                ProfileView? profile = null;
                try
                {
                    profile = await accounts.GetProfileAsync(friend);
                }
                catch (AccountException)
                {
                }

                bool isOnline = online.TryFind(friend, out string room);
                state.Friends.Add(new FriendInfo
                {
                    Name = profile?.Name ?? friend,
                    Online = isOnline,
                    Room = isOnline ? room : "",
                    BestSteps = profile?.BestSteps ?? 0,
                });
            }

            state.Friends = state.Friends.OrderByDescending(f => f.Online).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            state.Incoming.AddRange(record.Incoming);
            state.Outgoing.AddRange(record.Outgoing);

            string me = AccountService.Canonical(principal);
            if (invites.TryGetValue(me, out var pending))
            {
                lock (pending)
                {
                    pending.RemoveAll(i => i.Expires <= DateTime.UtcNow);
                    foreach (var invite in pending)
                    {
                        state.Invites.Add(new InviteInfo { From = invite.From, Room = invite.Room });
                    }
                }
            }

            return state;
        }

        public async Task<ActionResult> FriendAction(string principal, FriendActionRequest request)
        {
            string target = request.Target ?? "";
            switch ((request.Action ?? "").ToLowerInvariant())
            {
                case "request":
                    await Guard(() => accounts.RequestFriendAsync(principal, target));
                    Metrics?.Count("friend_requests_total");
                    return new ActionResult { Ok = true, Message = "đã gửi lời mời kết bạn" };
                case "accept":
                    await Guard(() => accounts.AcceptFriendAsync(principal, target));
                    return new ActionResult { Ok = true, Message = "đã thành bạn" };
                case "decline":
                    await Guard(() => accounts.DeclineFriendAsync(principal, target));
                    return new ActionResult { Ok = true, Message = "đã từ chối" };
                case "remove":
                    await Guard(() => accounts.RemoveFriendAsync(principal, target));
                    return new ActionResult { Ok = true, Message = "đã xoá bạn" };
                case "dismiss-invite":
                    DismissInvite(principal, target);
                    return new ActionResult { Ok = true };
                default:
                    throw new RpcException(Status.InvalidArgument("hành động bạn bè không hợp lệ"));
            }
        }

        public async Task<ActionResult> Invite(string principal, InviteRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Room))
            {
                throw new RpcException(Status.InvalidArgument("hãy vào một phòng trước khi mời bạn"));
            }

            bool friends = await Guard(() => accounts.AreFriendsAsync(principal, request.To));
            if (!friends)
            {
                throw new RpcException(Status.FailedPrecondition("chỉ mời được bạn bè"));
            }

            string to = AccountService.Canonical(request.To);
            var list = invites.GetOrAdd(to, _ => new List<(string, string, DateTime)>());
            lock (list)
            {
                list.RemoveAll(i => i.Expires <= DateTime.UtcNow || string.Equals(i.From, principal, StringComparison.OrdinalIgnoreCase));
                list.Add((principal, request.Room, DateTime.UtcNow + InviteLifetime));
            }

            Metrics?.Count("room_invites_total");
            return new ActionResult { Ok = true, Message = "đã gửi lời mời" };
        }

        private void DismissInvite(string principal, string from)
        {
            if (invites.TryGetValue(AccountService.Canonical(principal), out var list))
            {
                lock (list)
                {
                    list.RemoveAll(i => string.Equals(i.From, from, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        // ------------------------------------------------------------ bảng điểm

        public ScoreboardState Scoreboard(ScoreboardRequest request)
        {
            var state = new ScoreboardState();
            lock (boardGate)
            {
                if (boards.TryGetValue(request.Room ?? "", out Board? board) && DateTime.UtcNow - board.Seen <= BoardLifetime)
                {
                    state.Entries.AddRange(board.Rows.Values.Select(r => new ScoreEntry { Name = r.Name, Steps = r.Steps, Best = r.Best }));
                }
            }

            state.Entries = state.Entries
                .OrderByDescending(e => e.Steps).ThenByDescending(e => e.Best).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return state;
        }

        /// <summary>Pod báo bảng điểm hiện tại của một phòng (mỗi vài giây). Cập nhật bảng, kỷ lục và số trận.</summary>
        public void ReportScores(ScoreReport report)
        {
            Metrics?.Count("score_reports_total");
            _ = ProcessReport(report);
        }

        private async Task ProcessReport(ScoreReport report)
        {
            try
            {
                var previous = new Dictionary<string, ScoreEntry>();
                lock (boardGate)
                {
                    if (boards.TryGetValue(report.Room, out Board? old))
                    {
                        foreach (var pair in old.Rows)
                        {
                            previous[pair.Key] = pair.Value;
                        }
                    }
                }

                var rows = new Dictionary<string, ScoreEntry>();
                foreach (PodScore entry in report.Entries)
                {
                    string key = AccountService.Canonical(entry.Player);
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    ScoreEntry row;
                    if (previous.TryGetValue(key, out ScoreEntry? known))
                    {
                        row = new ScoreEntry { Name = known.Name, Steps = entry.Steps, Best = known.Best };
                    }
                    else
                    {
                        // Lần đầu thấy người chơi trong phòng: lấy tên hiển thị + kỷ lục, và tính một trận.
                        string name = entry.Player;
                        uint best = 0;
                        try
                        {
                            ProfileView profile = await accounts.GetProfileAsync(entry.Player);
                            name = profile.Name;
                            best = profile.BestSteps;
                            await accounts.RecordMatchAsync(entry.Player);
                        }
                        catch (AccountException)
                        {
                            // khách (chưa có tài khoản): vẫn lên bảng điểm, không lưu kỷ lục
                        }

                        row = new ScoreEntry { Name = name, Steps = entry.Steps, Best = best };
                    }

                    if (entry.Steps > row.Best && await accounts.RecordStepsAsync(entry.Player, entry.Steps))
                    {
                        row.Best = entry.Steps;
                    }

                    rows[key] = row;
                }

                lock (boardGate)
                {
                    var board = new Board { Seen = DateTime.UtcNow };
                    foreach (var pair in rows)
                    {
                        board.Rows[pair.Key] = pair.Value;
                    }

                    boards[report.Room] = board;
                }
            }
            catch (Exception problem)
            {
                Console.Error.WriteLine($"[Scores] {report.Room}: {problem.Message}");
            }
        }

        // ------------------------------------------------------------ ánh xạ lỗi

        private static async Task Guard(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (AccountException problem)
            {
                throw Translate(problem);
            }
        }

        private static async Task<T> Guard<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (AccountException problem)
            {
                throw Translate(problem);
            }
        }

        public static RpcException Translate(AccountException problem)
        {
            switch (problem.Error)
            {
                case AccountError.InvalidName:
                case AccountError.WeakPassword:
                case AccountError.Invalid:
                    return new RpcException(Status.InvalidArgument(problem.Message));
                case AccountError.BadCredentials:
                case AccountError.BadRecoveryCode:
                    return new RpcException(Status.Unauthenticated(problem.Message));
                case AccountError.Locked:
                    return new RpcException(Status.ResourceExhausted(problem.Message));
                default:
                    return new RpcException(Status.FailedPrecondition(problem.Message));
            }
        }
    }
}
