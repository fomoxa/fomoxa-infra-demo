using System;
using System.Text;
using System.Threading.Tasks;
using Fomoxa.Rpc;
using MyGame.Backend.Models;

namespace MyGame.Backend.Accounts
{
    public sealed class AuthOutcome
    {
        public string Principal { get; init; } = "";

        public string DisplayName { get; init; } = "";

        /// <summary>Có giá trị khi vừa đăng ký hoặc vừa khôi phục: mã khôi phục mới (hiện đúng một lần).</summary>
        public string RecoveryCode { get; init; } = "";
    }

    /// <summary>Xử lý đăng nhập có tài khoản: login / register / recover. Dùng chung cho backend thật và cụm giả.</summary>
    public sealed class AccountAuth
    {
        private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

        private readonly AccountService accounts;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Code, DateTime Expires)> pendingCodes =
            new System.Collections.Concurrent.ConcurrentDictionary<string, (string, DateTime)>();

        public AccountAuth(AccountService accounts)
        {
            this.accounts = accounts;
        }

        /// <summary>
        /// Mã khôi phục vừa cấp. Thư viện RPC không trả nội dung phản hồi của lệnh đăng nhập cho client nên mã được giữ tạm
        /// trong bộ nhớ và client lấy bằng Lobby.TakeRecoveryCode (một lần, 10 phút).
        /// </summary>
        public string TakeRecoveryCode(string principal)
        {
            if (pendingCodes.TryRemove(principal, out var entry) && entry.Expires > DateTime.UtcNow)
            {
                return entry.Code;
            }

            return "";
        }

        public static bool UsesAccounts(PlayerLogin request) =>
            !string.IsNullOrEmpty(request.Mode) || (request.Secret != null && request.Secret.Length > 0);

        public AuthOutcome Authenticate(PlayerLogin request)
        {
            Task<AuthOutcome> work = Task.Run(() => AuthenticateAsync(request));
            try
            {
                if (!work.Wait(TimeSpan.FromSeconds(20)))
                {
                    throw new RpcException(new Status(StatusCode.Unavailable, "kho tài khoản không trả lời"));
                }
            }
            catch (AggregateException wrapped) when (wrapped.InnerException is AccountException problem)
            {
                throw LobbyCore.Translate(problem);
            }

            try
            {
                AuthOutcome outcome = work.GetAwaiter().GetResult();
                if (outcome.RecoveryCode.Length > 0)
                {
                    pendingCodes[outcome.Principal] = (outcome.RecoveryCode, DateTime.UtcNow + CodeLifetime);
                }

                return outcome;
            }
            catch (AccountException problem)
            {
                throw LobbyCore.Translate(problem);
            }
        }

        private async Task<AuthOutcome> AuthenticateAsync(PlayerLogin request)
        {
            string name = (request.Player ?? "").Trim();
            string password = request.Secret == null ? "" : Encoding.UTF8.GetString(request.Secret);
            switch ((request.Mode ?? "").ToLowerInvariant())
            {
                case "register":
                {
                    string recovery = await accounts.RegisterAsync(name, password);
                    return new AuthOutcome { Principal = AccountService.Canonical(name), DisplayName = name, RecoveryCode = recovery };
                }

                case "recover":
                {
                    string code = request.Recovery == null ? "" : Encoding.UTF8.GetString(request.Recovery);
                    string fresh = await accounts.RecoverAsync(name, code, password);
                    string display = (await accounts.GetProfileAsync(name)).Name;
                    return new AuthOutcome { Principal = AccountService.Canonical(name), DisplayName = display, RecoveryCode = fresh };
                }

                default:
                {
                    string display = await accounts.LoginAsync(name, password);
                    return new AuthOutcome { Principal = AccountService.Canonical(name), DisplayName = display };
                }
            }
        }
    }
}
