using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace MockCluster
{
    /// <summary>
    /// Đầu cuối TLS cho backend giả: nhận kết nối TLS ở cổng riêng (mặc định 9643) rồi chuyển byte thô tới backend ở 9600.
    /// Giống vai trò của proxy TLS (nginx / stunnel) đặt trước backend thật. Chứng chỉ tự ký được tạo lần đầu và lưu lại
    /// để vân tay (pin) không đổi giữa các lần chạy.
    /// </summary>
    internal sealed class TlsTerminator : IDisposable
    {
        private const string PfxPassword = "mock";
        private readonly TcpListener listener;
        private readonly X509Certificate2 certificate;
        private readonly string upstreamHost;
        private readonly int upstreamPort;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();

        public TlsTerminator(string listen, string upstreamHost, int upstreamPort, string pfxPath)
        {
            this.upstreamHost = upstreamHost;
            this.upstreamPort = upstreamPort;
            certificate = LoadOrCreate(pfxPath);
            PinSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData));

            int colon = listen.LastIndexOf(':');
            listener = new TcpListener(IPAddress.Parse(listen.Substring(0, colon)), int.Parse(listen.Substring(colon + 1)));
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        public int Port { get; }

        /// <summary>Vân tay SHA-256 (hex) của chứng chỉ - dán vào Tls Pin Sha256 của RoomJoiner hoặc tham số -tlspin:.</summary>
        public string PinSha256 { get; }

        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
            certificate.Dispose();
        }

        private async Task AcceptLoop()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stop.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => Serve(client));
            }
        }

        private async Task Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    using var tls = new SslStream(client.GetStream(), false);
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        ClientCertificateRequired = false,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    }, stop.Token);

                    using var upstream = new TcpClient { NoDelay = true };
                    await upstream.ConnectAsync(upstreamHost, upstreamPort, stop.Token);
                    NetworkStream plain = upstream.GetStream();

                    Task up = tls.CopyToAsync(plain, stop.Token);
                    Task down = plain.CopyToAsync(tls, stop.Token);
                    await Task.WhenAny(up, down);
                }
                catch (Exception)
                {
                    // Client rớt kết nối / handshake lỗi: bỏ qua, mỗi kết nối độc lập.
                }
            }
        }

        private static X509Certificate2 LoadOrCreate(string path)
        {
            if (File.Exists(path))
            {
                return new X509Certificate2(path, PfxPassword, X509KeyStorageFlags.Exportable);
            }

            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest("CN=fomoxa-mock", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
            byte[] pfx = created.Export(X509ContentType.Pfx, PfxPassword);

            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, pfx);
            return new X509Certificate2(pfx, PfxPassword, X509KeyStorageFlags.Exportable);
        }
    }
}
