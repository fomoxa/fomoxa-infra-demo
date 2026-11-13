using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MyGame.Backend.Accounts
{
    public enum AccountError
    {
        InvalidName,
        WeakPassword,
        NameTaken,
        NotFound,
        BadCredentials,
        BadRecoveryCode,
        Locked,
        Conflict,
        NotFriends,
        Invalid,
    }

    public sealed class AccountException : Exception
    {
        public AccountException(AccountError error, string message) : base(message)
        {
            Error = error;
        }

        public AccountError Error { get; }
    }

    public sealed class AccountRecord
    {
        public string Name { get; set; } = "";

        public string Salt { get; set; } = "";

        public string Hash { get; set; } = "";

        public string RecoverySalt { get; set; } = "";

        public string RecoveryHash { get; set; } = "";

        public long CreatedUnix { get; set; }
    }

    public sealed class ProfileRecord
    {
        public uint BestSteps { get; set; }

        public uint Matches { get; set; }

        public long LastPlayedUnix { get; set; }
    }

    public sealed class SocialRecord
    {
        public List<string> Friends { get; set; } = new List<string>();

        public List<string> Incoming { get; set; } = new List<string>();

        public List<string> Outgoing { get; set; } = new List<string>();
    }

    public sealed class ProfileView
    {
        public string Name { get; init; } = "";

        public uint BestSteps { get; init; }

        public uint Matches { get; init; }

        public long CreatedUnix { get; init; }
    }

    /// <summary>
    /// Tài khoản, mật khẩu (PBKDF2-SHA256), mã khôi phục, hồ sơ và bạn bè. Toàn bộ trạng thái nằm trong <see cref="IKeyValueStore"/>
    /// nên dùng chung được cho backend thật (Nakama storage) và cụm giả (bộ nhớ / file).
    /// </summary>
    public sealed class AccountService
    {
        public const int MinPassword = 8;
        public const int MaxPassword = 128;
        private const int Iterations = 120_000;
        private const int MaxFailures = 5;
        private static readonly TimeSpan FailureWindow = TimeSpan.FromSeconds(60);
        private const string Accounts = "account";
        private const string Profiles = "profile";
        private const string Socials = "social";
        private static readonly Regex ValidName = new Regex("^[A-Za-z0-9_-]{3,24}$");
        private static readonly char[] RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNamingPolicy = null };

        private readonly IKeyValueStore store;
        private readonly object failureGate = new object();
        private readonly Dictionary<string, (int Count, DateTime Until)> failures = new Dictionary<string, (int, DateTime)>();
        private DateTime nextSweep;

        public AccountService(IKeyValueStore store)
        {
            this.store = store;
        }

        /// <summary>Tên chuẩn hoá (chữ thường) dùng làm khoá và làm principal.</summary>
        public static string Canonical(string name) => (name ?? "").Trim().ToLowerInvariant();

        public static bool IsValidName(string name) => ValidName.IsMatch(name ?? "");

        // ---------------------------------------------------------------- tài khoản

        /// <summary>Tạo tài khoản. Trả về mã khôi phục - chỉ hiện đúng một lần, hãy cho người chơi lưu lại.</summary>
        public async Task<string> RegisterAsync(string name, string password)
        {
            name = (name ?? "").Trim();
            if (!IsValidName(name))
            {
                throw new AccountException(AccountError.InvalidName, "tên chỉ gồm chữ, số, '_' hoặc '-', dài 3-24 ký tự");
            }

            CheckPassword(password);
            string recovery = NewRecoveryCode();
            var record = NewRecord(name, password, recovery);
            bool created = await store.PutAsync(Accounts, Canonical(name), JsonSerializer.Serialize(record, Json), "*");
            if (!created)
            {
                throw new AccountException(AccountError.NameTaken, "tên này đã có người dùng");
            }

            return recovery;
        }

        /// <summary>Kiểm tra mật khẩu; trả về tên hiển thị.</summary>
        public async Task<string> LoginAsync(string name, string password)
        {
            string key = Canonical(name);
            if (!IsValidName(key))
            {
                throw new AccountException(AccountError.BadCredentials, "sai tên hoặc mật khẩu");
            }

            string failureKey = LoginFailures(key);
            ThrowIfLocked(failureKey);
            KvRecord? row = await store.GetAsync(Accounts, key);
            AccountRecord? account = row == null ? null : JsonSerializer.Deserialize<AccountRecord>(row.Json, Json);
            if (account == null)
            {
                // Vẫn tốn thời gian băm để không lộ tên nào tồn tại qua thời gian phản hồi.
                Hash(password ?? "", new byte[16]);
                RegisterFailure(failureKey);
                throw new AccountException(AccountError.BadCredentials, "sai tên hoặc mật khẩu");
            }

            if (!Matches(password ?? "", account.Salt, account.Hash))
            {
                RegisterFailure(failureKey);
                throw new AccountException(AccountError.BadCredentials, "sai tên hoặc mật khẩu");
            }

            ClearFailures(failureKey);
            return account.Name;
        }

        public async Task<bool> ExistsAsync(string name) => await store.GetAsync(Accounts, Canonical(name)) != null;

        public async Task ChangePasswordAsync(string name, string oldPassword, string newPassword)
        {
            CheckPassword(newPassword);
            await LoginAsync(name, oldPassword);
            string key = Canonical(name);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                KvRecord row = await store.GetAsync(Accounts, key) ?? throw new AccountException(AccountError.NotFound, "không có tài khoản");
                AccountRecord account = JsonSerializer.Deserialize<AccountRecord>(row.Json, Json)!;
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                account.Salt = Convert.ToBase64String(salt);
                account.Hash = Convert.ToBase64String(Hash(newPassword, salt));
                if (await store.PutAsync(Accounts, key, JsonSerializer.Serialize(account, Json), row.Version))
                {
                    return;
                }
            }

            throw new AccountException(AccountError.Conflict, "tài khoản đang được cập nhật, thử lại");
        }

        /// <summary>Đặt lại mật khẩu bằng mã khôi phục. Trả về mã khôi phục MỚI (mã cũ hết hiệu lực).</summary>
        public async Task<string> RecoverAsync(string name, string recoveryCode, string newPassword)
        {
            CheckPassword(newPassword);
            string key = Canonical(name);
            if (!IsValidName(key))
            {
                throw new AccountException(AccountError.BadRecoveryCode, "tên hoặc mã khôi phục không đúng");
            }

            string failureKey = RecoveryFailures(key);
            ThrowIfLocked(failureKey);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                KvRecord? row = await store.GetAsync(Accounts, key);
                AccountRecord? account = row == null ? null : JsonSerializer.Deserialize<AccountRecord>(row.Json, Json);
                string normalized = NormalizeCode(recoveryCode);
                if (account == null)
                {
                    Hash(normalized, new byte[16]);
                }

                if (account == null || !Matches(normalized, account.RecoverySalt, account.RecoveryHash))
                {
                    RegisterFailure(failureKey);
                    throw new AccountException(AccountError.BadRecoveryCode, "tên hoặc mã khôi phục không đúng");
                }

                string fresh = NewRecoveryCode();
                AccountRecord replaced = NewRecord(account.Name, newPassword, fresh);
                replaced.CreatedUnix = account.CreatedUnix;
                if (await store.PutAsync(Accounts, key, JsonSerializer.Serialize(replaced, Json), row!.Version))
                {
                    ClearFailures(failureKey);
                    ClearFailures(LoginFailures(key));
                    return fresh;
                }
            }

            throw new AccountException(AccountError.Conflict, "tài khoản đang được cập nhật, thử lại");
        }

        // ---------------------------------------------------------------- hồ sơ & kỷ lục

        public async Task<ProfileView> GetProfileAsync(string name)
        {
            string key = Canonical(name);
            KvRecord? row = await store.GetAsync(Accounts, key);
            if (row == null)
            {
                throw new AccountException(AccountError.NotFound, "không có người chơi này");
            }

            AccountRecord account = JsonSerializer.Deserialize<AccountRecord>(row.Json, Json)!;
            ProfileRecord profile = await ReadProfile(key) ?? new ProfileRecord();
            return new ProfileView { Name = account.Name, BestSteps = profile.BestSteps, Matches = profile.Matches, CreatedUnix = account.CreatedUnix };
        }

        /// <summary>Cập nhật kỷ lục số bậc nhảy cao nhất; trả về true nếu phá kỷ lục. Người chơi chưa có tài khoản bị bỏ qua.</summary>
        public async Task<bool> RecordStepsAsync(string name, uint steps)
        {
            string key = Canonical(name);
            if (await store.GetAsync(Accounts, key) == null)
            {
                return false;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                KvRecord? row = await store.GetAsync(Profiles, key);
                ProfileRecord profile = row == null ? new ProfileRecord() : JsonSerializer.Deserialize<ProfileRecord>(row.Json, Json)!;
                if (steps <= profile.BestSteps)
                {
                    return false;
                }

                profile.BestSteps = steps;
                profile.LastPlayedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (await store.PutAsync(Profiles, key, JsonSerializer.Serialize(profile, Json), row?.Version ?? "*"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Đếm một trận đã chơi (gọi khi người chơi xuất hiện lần đầu trong một phòng).</summary>
        public async Task RecordMatchAsync(string name)
        {
            string key = Canonical(name);
            if (await store.GetAsync(Accounts, key) == null)
            {
                return;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                KvRecord? row = await store.GetAsync(Profiles, key);
                ProfileRecord profile = row == null ? new ProfileRecord() : JsonSerializer.Deserialize<ProfileRecord>(row.Json, Json)!;
                profile.Matches++;
                profile.LastPlayedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (await store.PutAsync(Profiles, key, JsonSerializer.Serialize(profile, Json), row?.Version ?? "*"))
                {
                    return;
                }
            }
        }

        public async Task<uint> BestStepsAsync(string name) => (await ReadProfile(Canonical(name)))?.BestSteps ?? 0;

        // ---------------------------------------------------------------- bạn bè

        public async Task<SocialRecord> GetSocialAsync(string name) => (await ReadSocial(Canonical(name))).Record;

        /// <summary>Gửi lời mời kết bạn; nếu đối phương đã mời mình thì thành bạn luôn.</summary>
        public async Task RequestFriendAsync(string from, string to)
        {
            string a = Canonical(from);
            string b = Canonical(to);
            if (a == b)
            {
                throw new AccountException(AccountError.Invalid, "không thể kết bạn với chính mình");
            }

            if (await store.GetAsync(Accounts, b) == null)
            {
                throw new AccountException(AccountError.NotFound, "không có người chơi này");
            }

            SocialRecord mine = (await ReadSocial(a)).Record;
            if (mine.Friends.Contains(b))
            {
                return;
            }

            if (mine.Incoming.Contains(b))
            {
                await AcceptFriendAsync(from, to);
                return;
            }

            await Mutate(a, s => AddUnique(s.Outgoing, b));
            await Mutate(b, s => AddUnique(s.Incoming, a));
        }

        public async Task AcceptFriendAsync(string me, string other)
        {
            string a = Canonical(me);
            string b = Canonical(other);
            SocialRecord mine = (await ReadSocial(a)).Record;
            if (!mine.Incoming.Contains(b))
            {
                throw new AccountException(AccountError.NotFound, "không có lời mời từ người này");
            }

            await Mutate(a, s => { s.Incoming.Remove(b); s.Outgoing.Remove(b); AddUnique(s.Friends, b); });
            await Mutate(b, s => { s.Outgoing.Remove(a); s.Incoming.Remove(a); AddUnique(s.Friends, a); });
        }

        /// <summary>Từ chối lời mời (mình nhận) hoặc rút lại lời mời (mình gửi).</summary>
        public async Task DeclineFriendAsync(string me, string other)
        {
            string a = Canonical(me);
            string b = Canonical(other);
            await Mutate(a, s => { s.Incoming.Remove(b); s.Outgoing.Remove(b); });
            await Mutate(b, s => { s.Incoming.Remove(a); s.Outgoing.Remove(a); });
        }

        public async Task RemoveFriendAsync(string me, string other)
        {
            string a = Canonical(me);
            string b = Canonical(other);
            await Mutate(a, s => s.Friends.Remove(b));
            await Mutate(b, s => s.Friends.Remove(a));
        }

        public async Task<bool> AreFriendsAsync(string a, string b) => (await ReadSocial(Canonical(a))).Record.Friends.Contains(Canonical(b));

        // ---------------------------------------------------------------- nội bộ

        private async Task<ProfileRecord?> ReadProfile(string key)
        {
            KvRecord? row = await store.GetAsync(Profiles, key);
            return row == null ? null : JsonSerializer.Deserialize<ProfileRecord>(row.Json, Json);
        }

        private async Task<(SocialRecord Record, string? Version)> ReadSocial(string key)
        {
            KvRecord? row = await store.GetAsync(Socials, key);
            return row == null ? (new SocialRecord(), null) : (JsonSerializer.Deserialize<SocialRecord>(row.Json, Json)!, row.Version);
        }

        private async Task Mutate(string key, Action<SocialRecord> change)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var (record, version) = await ReadSocial(key);
                change(record);
                if (await store.PutAsync(Socials, key, JsonSerializer.Serialize(record, Json), version ?? "*"))
                {
                    return;
                }
            }

            throw new AccountException(AccountError.Conflict, "đang cập nhật danh sách bạn, thử lại");
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (!list.Contains(value))
            {
                list.Add(value);
            }
        }

        private static void CheckPassword(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinPassword)
            {
                throw new AccountException(AccountError.WeakPassword, $"mật khẩu phải có ít nhất {MinPassword} ký tự");
            }

            if (password.Length > MaxPassword)
            {
                throw new AccountException(AccountError.WeakPassword, $"mật khẩu tối đa {MaxPassword} ký tự");
            }
        }

        private static AccountRecord NewRecord(string name, string password, string recovery)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] recoverySalt = RandomNumberGenerator.GetBytes(16);
            return new AccountRecord
            {
                Name = name,
                Salt = Convert.ToBase64String(salt),
                Hash = Convert.ToBase64String(Hash(password, salt)),
                RecoverySalt = Convert.ToBase64String(recoverySalt),
                RecoveryHash = Convert.ToBase64String(Hash(NormalizeCode(recovery), recoverySalt)),
                CreatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
        }

        private static byte[] Hash(string secret, byte[] salt) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, Iterations, HashAlgorithmName.SHA256, 32);

        private static bool Matches(string secret, string saltBase64, string hashBase64)
        {
            byte[] expected = Convert.FromBase64String(hashBase64);
            byte[] actual = Hash(secret, Convert.FromBase64String(saltBase64));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static string NormalizeCode(string? code) => (code ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant();

        /// <summary>Mã khôi phục dạng XXXX-XXXX-XXXX-XXXX (16 ký tự, ~80 bit).</summary>
        private static string NewRecoveryCode()
        {
            var builder = new StringBuilder(19);
            for (int i = 0; i < 16; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    builder.Append('-');
                }

                builder.Append(RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)]);
            }

            return builder.ToString();
        }

        private static string LoginFailures(string key) => "login:" + key;

        private static string RecoveryFailures(string key) => "recover:" + key;

        private void ThrowIfLocked(string key)
        {
            lock (failureGate)
            {
                if (failures.TryGetValue(key, out var entry) && entry.Count >= MaxFailures && DateTime.UtcNow < entry.Until)
                {
                    throw new AccountException(AccountError.Locked, "thử sai quá nhiều lần, đợi một phút rồi thử lại");
                }
            }
        }

        private void RegisterFailure(string key)
        {
            lock (failureGate)
            {
                DateTime now = DateTime.UtcNow;
                SweepFailures(now);
                if (!failures.TryGetValue(key, out var entry) || now >= entry.Until)
                {
                    failures[key] = (1, now + FailureWindow);
                    return;
                }

                int count = entry.Count + 1;
                failures[key] = (count, count == MaxFailures ? now + FailureWindow : entry.Until);
            }
        }

        private void SweepFailures(DateTime now)
        {
            if (now < nextSweep)
            {
                return;
            }

            nextSweep = now + FailureWindow;
            foreach (string expired in failures.Where(pair => now >= pair.Value.Until).Select(pair => pair.Key).ToList())
            {
                failures.Remove(expired);
            }
        }

        private void ClearFailures(string key)
        {
            lock (failureGate)
            {
                failures.Remove(key);
            }
        }
    }
}
