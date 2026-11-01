using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace MyGame.Backend
{
    public static class Program
    {
        public static int Main()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            BackendSettings settings;
            try
            {
                settings = BackendSettings.Read(configuration.GetSection("Fomoxa"));
            }
            catch (BackendSettingsException problem)
            {
                Console.Error.WriteLine($"backend: {problem.Message}");
                return 1;
            }

            using var stopping = new ManualResetEventSlim();
            using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
            using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

            Backend backend;
            try
            {
                backend = Backend.Start(settings);
            }
            catch (Exception problem)
            {
                Console.Error.WriteLine($"backend: khoi dong that bai ({problem.GetType().Name}): {problem.Message}");
                return 1;
            }

            using (backend)
            {
                Console.Error.WriteLine($"backend: players on tcp {backend.PlayersTcp?.ToString() ?? "off"}, websocket {backend.PlayersWebSocket?.ToString() ?? "off"}; pods on tcp {backend.PodsTcp?.ToString() ?? "off"}; directory at {settings.DirectoryAddress}");
                stopping.Wait();
            }
            return 0;

            void Stop(PosixSignalContext context)
            {
                context.Cancel = true;
                stopping.Set();
            }
        }
    }
}
