using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyGame.Backend
{
    public sealed class NakamaException : Exception
    {
        public NakamaException(string message) : base(message)
        {
        }
    }

    public sealed class NakamaSession
    {
        public string UserId { get; init; } = "";

        public string Token { get; init; } = "";
    }

    public sealed class MatchState
    {
        public string Status { get; init; } = "";

        public string Room { get; init; } = "";

        public string Ticket { get; init; } = "";

        public long Exp { get; init; }
    }

    public sealed class NakamaTicket
    {
        public bool Ok { get; init; }

        public string Message { get; init; } = "";

        public string Room { get; init; } = "";

        public long Exp { get; init; }
    }

    public sealed class NakamaRoom
    {
        public string Room { get; init; } = "";

        public int Players { get; init; }

        public int Capacity { get; init; }

        public string State { get; init; } = "";
    }

    public sealed class NakamaLink : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private readonly string url;
        private readonly string serverKey;
        private readonly string httpKey;

        public NakamaLink(string url, string serverKey, string httpKey)
        {
            this.url = url.TrimEnd('/');
            this.serverKey = serverKey;
            this.httpKey = httpKey;
        }

        public async Task<NakamaSession> AuthenticateDevice(string deviceId, CancellationToken cancellation = default)
        {
            var body = JsonSerializer.Serialize(new { id = deviceId, create = true }, Json);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/v2/account/authenticate/device?create=true");
            request.Headers.Authorization = Basic(serverKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using JsonDocument document = await Send(request, cancellation).ConfigureAwait(false);
            string token = document.RootElement.GetProperty("token").GetString()
                ?? throw new NakamaException("Nakama trả về session không có token");

            return new NakamaSession { UserId = Subject(token), Token = token };
        }

        /// <summary>
        /// Đăng nhập bằng email giả (name@fomoxa.local) + mật khẩu. Lần đầu dùng một tên thì Nakama tạo tài khoản
        /// (create=true); các lần sau sai mật khẩu sẽ bị Nakama từ chối (401).
        /// </summary>
        public async Task<NakamaSession> AuthenticatePassword(string player, string password, CancellationToken cancellation = default)
        {
            string email = $"{player.ToLowerInvariant()}@fomoxa.local";
            var body = JsonSerializer.Serialize(new { email, password }, Json);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{url}/v2/account/authenticate/email?create=true&username={Uri.EscapeDataString(player)}");
            request.Headers.Authorization = Basic(serverKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using JsonDocument document = await Send(request, cancellation).ConfigureAwait(false);
            string token = document.RootElement.GetProperty("token").GetString()
                ?? throw new NakamaException("Nakama trả về session không có token");

            return new NakamaSession { UserId = Subject(token), Token = token };
        }

        public async Task<List<NakamaRoom>> ListRooms(CancellationToken cancellation = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/v2/rpc/list_rooms?unwrap");
            request.Headers.Authorization = Basic(httpKey);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using JsonDocument document = await Send(request, cancellation).ConfigureAwait(false);
            var rooms = new List<NakamaRoom>();
            if (document.RootElement.TryGetProperty("rooms", out JsonElement array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in array.EnumerateArray())
                {
                    rooms.Add(new NakamaRoom
                    {
                        Room = item.TryGetProperty("room", out JsonElement r) ? r.GetString() ?? "" : "",
                        Players = item.TryGetProperty("players", out JsonElement p) ? p.GetInt32() : 0,
                        Capacity = item.TryGetProperty("capacity", out JsonElement c) ? c.GetInt32() : 2,
                        State = item.TryGetProperty("state", out JsonElement st) ? st.GetString() ?? "" : "",
                    });
                }
            }

            return rooms;
        }

        public Task<MatchState> FindMatch(string token, string room, IReadOnlyList<string>? pool = null, CancellationToken cancellation = default)
        {
            return Rpc("find_match", token, new { room, pool = pool ?? Array.Empty<string>() }, cancellation);
        }

        public Task<MatchState> PollMatch(string token, string room, CancellationToken cancellation = default)
        {
            return Rpc("poll_match", token, new { room }, cancellation);
        }

        public async Task<NakamaTicket> VerifyTicket(string ticket, string room, string userId, CancellationToken cancellation = default)
        {
            var body = JsonSerializer.Serialize(new { ticket, room, user_id = userId }, Json);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/v2/rpc/verify_ticket?unwrap");
            request.Headers.Authorization = Basic(httpKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using JsonDocument document = await Send(request, cancellation).ConfigureAwait(false);
            NakamaTicket? granted = document.RootElement.Deserialize<NakamaTicket>(Json);
            if (granted == null)
            {
                throw new NakamaException("Nakama trả về verify_ticket rỗng");
            }

            return granted.Ok || !string.IsNullOrEmpty(granted.Message)
                ? granted
                : new NakamaTicket { Ok = false, Message = "Nakama từ chối ticket" };
        }

        public void Dispose()
        {
            http.Dispose();
        }

        private async Task<MatchState> Rpc(string name, string token, object payload, CancellationToken cancellation)
        {
            var body = JsonSerializer.Serialize(payload, Json);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/v2/rpc/{name}?unwrap");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using JsonDocument document = await Send(request, cancellation).ConfigureAwait(false);
            MatchState? state = document.RootElement.Deserialize<MatchState>(Json);
            return state ?? throw new NakamaException($"Nakama trả về {name} rỗng");
        }

        private async Task<JsonDocument> Send(HttpRequestMessage request, CancellationToken cancellation)
        {
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
            }
            catch (HttpRequestException problem)
            {
                throw new NakamaException($"không gọi được Nakama: {problem.Message}");
            }
            catch (TaskCanceledException)
            {
                throw new NakamaException("Nakama không trả lời kịp");
            }

            using (response)
            {
                string body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new NakamaException($"Nakama trả về {(int)response.StatusCode}: {Trim(body)}");
                }

                try
                {
                    return JsonDocument.Parse(body);
                }
                catch (JsonException problem)
                {
                    throw new NakamaException($"Nakama trả về dữ liệu lạ: {problem.Message}");
                }
            }
        }

        private static string Trim(string body)
        {
            body = body.Trim();
            return body.Length > 200 ? body.Substring(0, 200) : body;
        }

        private static AuthenticationHeaderValue Basic(string key)
        {
            return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{key}:")));
        }

        private static string Subject(string token)
        {
            string[] parts = token.Split('.');
            if (parts.Length < 2)
            {
                throw new NakamaException("session token không hợp lệ");
            }

            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2:
                    payload += "==";
                    break;
                case 3:
                    payload += "=";
                    break;
            }

            using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(payload));
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("uid", out JsonElement uid) && uid.ValueKind == JsonValueKind.String)
            {
                return uid.GetString() ?? throw new NakamaException("session token thiếu uid");
            }

            if (root.TryGetProperty("sub", out JsonElement sub) && sub.ValueKind == JsonValueKind.String)
            {
                return sub.GetString() ?? throw new NakamaException("session token thiếu sub");
            }

            throw new NakamaException("session token không có uid");
        }
    }
}
