using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace MyGame.Backend
{
    public sealed class BackendSettingsException : Exception
    {
        public BackendSettingsException(string message) : base(message)
        {
        }
    }

    public sealed class BackendSettings
    {
        public const int MinimumKeyLength = 16;

        public string DirectoryAddress { get; set; } = "127.0.0.1:9500";

        public byte[] DirectoryKey { get; set; } = Array.Empty<byte>();

        public string PlayersTcp { get; set; } = "0.0.0.0:9600";

        public string PlayersWebSocket { get; set; } = "0.0.0.0:9601";

        public string PodsTcp { get; set; } = "0.0.0.0:9602";

        public byte[] PodKey { get; set; } = Array.Empty<byte>();

        /// <summary>File JSON lưu tài khoản khi không có Nakama (rỗng = chỉ trong bộ nhớ).</summary>
        public string DataFile { get; set; } = "";

        public string NakamaUrl { get; set; } = "";

        public string NakamaServerKey { get; set; } = "defaultkey";

        public string NakamaHttpKey { get; set; } = "defaulthttpkey";

        public static BackendSettings Read(IConfiguration fomoxa) => Read(fomoxa, Environment.GetEnvironmentVariable);

        public static BackendSettings Read(IConfiguration fomoxa, Func<string, string?> environment)
        {
            var settings = new BackendSettings
            {
                DirectoryAddress = fomoxa["Directory:Address"] ?? "127.0.0.1:9500",
                DirectoryKey = Key("Directory:Key", fomoxa.GetSection("Directory:Key"), environment),
                PlayersTcp = fomoxa["Players:Tcp"] ?? "0.0.0.0:9600",
                PlayersWebSocket = fomoxa["Players:WebSocket"] ?? "0.0.0.0:9601",
                PodsTcp = fomoxa["Pods:Tcp"] ?? "0.0.0.0:9602",
                DataFile = fomoxa["Data:File"] ?? "",
                NakamaUrl = fomoxa["Nakama:Url"] ?? "",
                NakamaServerKey = fomoxa["Nakama:ServerKey"] ?? "defaultkey",
                NakamaHttpKey = fomoxa["Nakama:HttpKey"] ?? "defaulthttpkey",
            };
            if (string.IsNullOrEmpty(settings.DirectoryAddress))
            {
                throw new BackendSettingsException("Directory:Address is empty");
            }

            if (!string.IsNullOrEmpty(settings.PodsTcp))
            {
                settings.PodKey = Key("Pods:Key", fomoxa.GetSection("Pods:Key"), environment);
            }

            return settings;
        }

        private static byte[] Key(string name, IConfigurationSection source, Func<string, string?> environment)
        {
            string? file = source["File"];
            string? variable = source["Env"];
            if (string.IsNullOrEmpty(file) == string.IsNullOrEmpty(variable))
            {
                throw new BackendSettingsException($"{name} needs exactly one of File and Env");
            }

            byte[] key;
            if (!string.IsNullOrEmpty(file))
            {
                try
                {
                    key = File.ReadAllBytes(file);
                }
                catch (IOException problem)
                {
                    throw new BackendSettingsException($"{name}: cannot read {file}: {problem.Message}");
                }
                catch (UnauthorizedAccessException problem)
                {
                    throw new BackendSettingsException($"{name}: cannot read {file}: {problem.Message}");
                }
            }
            else
            {
                string value = environment(variable!) ?? throw new BackendSettingsException($"{name}: the environment variable {variable} is not set");
                key = Encoding.UTF8.GetBytes(value);
            }

            key = TrimNewline(key);
            if (key.Length < MinimumKeyLength)
            {
                throw new BackendSettingsException($"{name} is shorter than {MinimumKeyLength} bytes");
            }

            return key;
        }

        private static byte[] TrimNewline(byte[] key)
        {
            int length = key.Length;
            if (length > 0 && key[length - 1] == (byte)'\n')
            {
                length--;
                if (length > 0 && key[length - 1] == (byte)'\r')
                {
                    length--;
                }
            }

            return key.AsSpan(0, length).ToArray();
        }
    }
}
