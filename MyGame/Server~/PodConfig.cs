using System;
using System.Collections.Generic;

namespace MyGame.Server
{
    internal sealed class PodConfigException : Exception
    {
        public PodConfigException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Cấu hình của một pod. Thứ tự ưu tiên: tham số dòng lệnh (`--port=7778`),
    /// rồi biến môi trường, rồi mặc định - để cùng một image chạy được ở local
    /// (nhiều pod, mỗi pod một tham số) và trong K8s (Agones đặt POD_IP/PORT).
    /// </summary>
    internal sealed class PodConfig
    {
        private readonly Dictionary<string, string> arguments = new Dictionary<string, string>(StringComparer.Ordinal);

        public string DirectoryHost { get; private set; } = "127.0.0.1";

        public ushort DirectoryPort { get; private set; } = 9500;

        public string PodName { get; private set; } = "pod-1";

        public string PodAddress { get; private set; } = "127.0.0.1";

        public ushort Port { get; private set; } = 7778;

        public string Version { get; private set; } = "1.0";

        public string Region { get; private set; } = "local";

        public uint MaxPlayers { get; private set; } = 16;

        public IReadOnlyList<string> Rooms { get; private set; } = new[] { "arena:1" };

        public bool BehindGateway { get; private set; } = true;

        public bool Trace { get; private set; }

        public bool Agones { get; private set; }

        public bool ClaimOne { get; private set; }

        public string KeyFile { get; private set; } = "";

        public static PodConfig Read(string[] argv)
        {
            var config = new PodConfig();
            config.ParseArguments(argv);

            string directory = config.Value("directory", "FOMOXA_DIRECTORY", "127.0.0.1:9500");
            int colon = directory.LastIndexOf(':');
            if (colon <= 0 || colon == directory.Length - 1)
            {
                throw new PodConfigException($"--directory phai la host:port, nhan duoc '{directory}'");
            }

            config.DirectoryHost = directory.Substring(0, colon);
            config.DirectoryPort = (ushort)Number(directory.Substring(colon + 1), "--directory port", 1, ushort.MaxValue);

            config.PodName = config.Value("name", "FOMOXA_POD_NAME", "pod-1");
            config.PodAddress = config.Value("address", "FOMOXA_POD_ADDRESS", DefaultAddress());
            config.Port = (ushort)Number(config.Value("port", "FOMOXA_POD_PORT", "7778"), "--port", 1, ushort.MaxValue);
            config.Version = config.Value("version", "FOMOXA_VERSION", "1.0");
            config.Region = config.Value("region", "FOMOXA_REGION", "local");
            config.MaxPlayers = (uint)Number(config.Value("max-players", "FOMOXA_MAX_PLAYERS", "16"), "--max-players", 1, 10000);
            config.BehindGateway = config.Flag("behind-gateway", "FOMOXA_BEHIND_GATEWAY", true);
            config.Trace = config.Flag("trace", "FOMOXA_TRACE", false);
            bool agonesDefault = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AGONES_SDK_GRPC_PORT"));
            config.Agones = config.Flag("agones", "FOMOXA_AGONES", agonesDefault);
            config.ClaimOne = config.Flag("claim-one", "FOMOXA_CLAIM_ONE", false);

            var rooms = new List<string>();
            foreach (string room in config.Value("room", "FOMOXA_ROOM", "arena:1").Split(','))
            {
                string trimmed = room.Trim();
                if (trimmed.Length > 0 && !rooms.Contains(trimmed))
                {
                    rooms.Add(trimmed);
                }
            }

            if (rooms.Count == 0)
            {
                throw new PodConfigException("--room khong co room nao");
            }

            config.Rooms = rooms;

            config.KeyFile = config.Value("key", "POD_KEY_FILE", "");
            if (config.KeyFile.Length == 0)
            {
                throw new PodConfigException("POD_KEY_FILE chua duoc dat (file key pod do gen-keys.ps1 sinh)");
            }

            return config;
        }

        private void ParseArguments(string[] argv)
        {
            if (argv == null)
            {
                return;
            }

            foreach (string argument in argv)
            {
                if (!argument.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string body = argument.Substring(2);
                int equals = body.IndexOf('=');
                if (equals < 0)
                {
                    arguments[body] = "true";
                    continue;
                }

                arguments[body.Substring(0, equals)] = body.Substring(equals + 1);
            }
        }

        private string Value(string argument, string environment, string fallback)
        {
            if (arguments.TryGetValue(argument, out string given))
            {
                return given;
            }

            string value = Environment.GetEnvironmentVariable(environment);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private bool Flag(string argument, string environment, bool fallback)
        {
            if (arguments.TryGetValue(argument, out string given))
            {
                return ParseFlag(given, "--" + argument);
            }

            string value = Environment.GetEnvironmentVariable(environment);
            return string.IsNullOrWhiteSpace(value) ? fallback : ParseFlag(value, environment);
        }

        private static bool ParseFlag(string value, string name)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    throw new PodConfigException($"{name} phai la true/false, nhan duoc '{value}'");
            }
        }

        private static int Number(string value, string name, int min, int max)
        {
            if (!int.TryParse(value, out int number) || number < min || number > max)
            {
                throw new PodConfigException($"{name} phai la so trong [{min}, {max}], nhan duoc '{value}'");
            }

            return number;
        }

        /// <summary>
        /// Agones đặt POD_IP; ngoài cluster thì mặc định 127.0.0.1.
        /// </summary>
        private static string DefaultAddress()
        {
            string podIp = Environment.GetEnvironmentVariable("POD_IP");
            return string.IsNullOrWhiteSpace(podIp) ? "127.0.0.1" : podIp.Trim();
        }
    }
}
