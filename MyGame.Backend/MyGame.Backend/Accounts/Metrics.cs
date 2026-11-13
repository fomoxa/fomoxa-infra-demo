using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyGame.Backend
{
    /// <summary>
    /// Metrics kiểu Prometheus (văn bản /metrics) không cần thư viện ngoài. Counter có nhãn + gauge tính khi được scrape.
    /// Dùng chung cho backend thật và cụm giả.
    /// </summary>
    public sealed class Metrics : IDisposable
    {
        private readonly ConcurrentDictionary<string, long> counters = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentDictionary<string, (string Help, Func<double> Value)> gauges = new ConcurrentDictionary<string, (string, Func<double>)>();
        private readonly ConcurrentDictionary<string, string> help = new ConcurrentDictionary<string, string>();
        private readonly string prefix;
        private HttpListener? listener;
        private CancellationTokenSource? stop;

        public Metrics(string prefix = "fomoxa")
        {
            this.prefix = prefix;
        }

        /// <summary>Tăng counter, ví dụ Count("logins_total", "result=\"ok\",mode=\"login\"").</summary>
        public void Count(string name, string labels = "", long by = 1) =>
            counters.AddOrUpdate($"{name}|{labels}", by, (_, old) => old + by);

        public void Describe(string name, string text) => help[name] = text;

        public void Gauge(string name, string text, Func<double> value) => gauges[name] = (text, value);

        public string Render()
        {
            var builder = new StringBuilder();
            var written = new System.Collections.Generic.HashSet<string>();
            foreach (var pair in counters)
            {
                string[] parts = pair.Key.Split('|', 2);
                if (written.Add(parts[0]))
                {
                    if (help.TryGetValue(parts[0], out string? text))
                    {
                        builder.Append($"# HELP {prefix}_{parts[0]} {text}\n");
                    }

                    builder.Append($"# TYPE {prefix}_{parts[0]} counter\n");
                }

                string labels = parts[1].Length == 0 ? "" : "{" + parts[1] + "}";
                builder.Append($"{prefix}_{parts[0]}{labels} {pair.Value}\n");
            }

            foreach (var pair in gauges)
            {
                double value;
                try
                {
                    value = pair.Value.Value();
                }
                catch (Exception)
                {
                    continue;
                }

                builder.Append($"# HELP {prefix}_{pair.Key} {pair.Value.Help}\n# TYPE {prefix}_{pair.Key} gauge\n");
                builder.Append($"{prefix}_{pair.Key} {value.ToString(CultureInfo.InvariantCulture)}\n");
            }

            return builder.ToString();
        }

        /// <summary>Mở HTTP /metrics ở "host:port" (vd 0.0.0.0:9610). Lỗi (thiếu quyền, cổng bận) chỉ ghi log, không làm hỏng dịch vụ.</summary>
        public void Serve(string listen)
        {
            if (string.IsNullOrEmpty(listen))
            {
                return;
            }

            try
            {
                int colon = listen.LastIndexOf(':');
                string host = listen.Substring(0, colon);
                string port = listen.Substring(colon + 1);
                string prefixUrl = host == "0.0.0.0" || host == "*" || host == "+" ? $"http://+:{port}/" : $"http://{host}:{port}/";
                listener = new HttpListener();
                listener.Prefixes.Add(prefixUrl);
                listener.Start();
                stop = new CancellationTokenSource();
                _ = Task.Run(Loop);
                Console.WriteLine($"[Metrics] http://{listen}/metrics");
            }
            catch (Exception problem)
            {
                Console.Error.WriteLine($"[Metrics] could not open {listen}: {problem.Message}");
                listener = null;
            }
        }

        private async Task Loop()
        {
            while (listener != null && listener.IsListening && stop != null && !stop.IsCancellationRequested)
            {
                try
                {
                    HttpListenerContext context = await listener.GetContextAsync();
                    byte[] body = Encoding.UTF8.GetBytes(context.Request.Url?.AbsolutePath == "/metrics" ? Render() : "fomoxa metrics: xem /metrics\n");
                    context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                    context.Response.ContentLength64 = body.Length;
                    await context.Response.OutputStream.WriteAsync(body);
                    context.Response.Close();
                }
                catch (Exception)
                {
                    if (stop == null || stop.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }

        public void Dispose()
        {
            stop?.Cancel();
            try
            {
                listener?.Stop();
            }
            catch (Exception)
            {
            }
        }
    }
}
