using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyGame.Backend.Accounts
{
    public sealed class KvRecord
    {
        public string Json { get; init; } = "";

        public string Version { get; init; } = "";
    }

    /// <summary>
    /// Kho key-value tối thiểu cho tài khoản / hồ sơ / bạn bè. Ghi có điều kiện theo version (optimistic concurrency):
    /// version null = ghi đè; "*" = chỉ ghi nếu chưa tồn tại; còn lại = chỉ ghi nếu version khớp.
    /// </summary>
    public interface IKeyValueStore
    {
        Task<KvRecord?> GetAsync(string collection, string key);

        Task<bool> PutAsync(string collection, string key, string json, string? version);
    }

    public class MemoryKv : IKeyValueStore
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, (string Json, long Version)> data = new Dictionary<string, (string, long)>();
        private long counter;

        public Task<KvRecord?> GetAsync(string collection, string key)
        {
            lock (gate)
            {
                return Task.FromResult<KvRecord?>(data.TryGetValue(Id(collection, key), out var entry)
                    ? new KvRecord { Json = entry.Json, Version = entry.Version.ToString() }
                    : null);
            }
        }

        public Task<bool> PutAsync(string collection, string key, string json, string? version)
        {
            lock (gate)
            {
                string id = Id(collection, key);
                bool exists = data.TryGetValue(id, out var entry);
                if (version == "*" && exists)
                {
                    return Task.FromResult(false);
                }

                if (!string.IsNullOrEmpty(version) && version != "*" && (!exists || entry.Version.ToString() != version))
                {
                    return Task.FromResult(false);
                }

                data[id] = (json, ++counter);
                OnChanged(Snapshot());
                return Task.FromResult(true);
            }
        }

        protected virtual void OnChanged(Dictionary<string, string> snapshot)
        {
        }

        protected void Load(Dictionary<string, string> loaded)
        {
            lock (gate)
            {
                foreach (var pair in loaded)
                {
                    data[pair.Key] = (pair.Value, ++counter);
                }
            }
        }

        private Dictionary<string, string> Snapshot()
        {
            var copy = new Dictionary<string, string>();
            foreach (var pair in data)
            {
                copy[pair.Key] = pair.Value.Json;
            }

            return copy;
        }

        private static string Id(string collection, string key) => collection + "\u001f" + key;
    }

    /// <summary>MemoryKv ghi ra một file JSON sau mỗi lần thay đổi, để dữ liệu còn sau khi khởi động lại (dùng cho cụm giả).</summary>
    public sealed class FileKv : MemoryKv
    {
        private readonly string path;

        public FileKv(string path)
        {
            this.path = path;
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded != null)
                {
                    Load(loaded);
                }
            }
        }

        protected override void OnChanged(Dictionary<string, string> snapshot)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            File.Move(temporary, path, true);
        }
    }

    /// <summary>Lưu trong Nakama (bảng storage) qua hai RPC Lua kv_get / kv_put, gọi bằng http_key của backend.</summary>
    public sealed class NakamaKv : IKeyValueStore, IDisposable
    {
        private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly string url;
        private readonly string httpKey;

        public NakamaKv(string url, string httpKey)
        {
            this.url = url.TrimEnd('/');
            this.httpKey = httpKey;
        }

        public async Task<KvRecord?> GetAsync(string collection, string key)
        {
            using JsonDocument reply = await Call("kv_get", new { collection, key });
            JsonElement root = reply.RootElement;
            if (!root.TryGetProperty("found", out JsonElement found) || !found.GetBoolean())
            {
                return null;
            }

            return new KvRecord
            {
                Json = root.GetProperty("value").GetString() ?? "",
                Version = root.GetProperty("version").GetString() ?? "",
            };
        }

        public async Task<bool> PutAsync(string collection, string key, string json, string? version)
        {
            using JsonDocument reply = await Call("kv_put", new { collection, key, value = json, version = version ?? "" });
            return reply.RootElement.TryGetProperty("ok", out JsonElement ok) && ok.GetBoolean();
        }

        public void Dispose() => http.Dispose();

        private async Task<JsonDocument> Call(string rpc, object payload)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/v2/rpc/{rpc}?unwrap");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{httpKey}:")));
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Nakama {rpc} trả về {(int)response.StatusCode}: {(body.Length > 200 ? body.Substring(0, 200) : body)}");
            }

            return JsonDocument.Parse(body);
        }
    }
}
