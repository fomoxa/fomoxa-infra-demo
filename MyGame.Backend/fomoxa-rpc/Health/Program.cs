using System;
using System.Linq;
using Fomoxa.Net;
using Fomoxa.Rpc;
using Fomoxa.Rpc.Net;
using Generated;

namespace Fomoxa.Rpc.Health
{
    public static class Program
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("usage: fomoxa-rpc-health <host:port> [service]");
                return 2;
            }
            string address = args[0];
            string service = args.Length > 1 ? args[1] : "";
            try
            {
                uint status = Ask(address, service);
                Console.WriteLine(HealthStatus.NameOf(status) ?? status.ToString());
                return status == HealthStatus.Serving ? 0 : 1;
            }
            catch (HealthException problem)
            {
                Console.Error.WriteLine(problem.Message);
                return 2;
            }
        }

        private static uint Ask(string address, string service)
        {
            var schema = new Schema(
                Handshake.FomoxaSchemaFingerprint,
                Handshake.FomoxaMessages
                    .Where(message => message.Id != FRpcCtxRpcCodec.MessageId)
                    .Select(message => new MessageSchema(message.Id, message.Fingerprint, message.Prefixes)));
            var registry = Registry.Builder(FRpcVoidRpcCodec.MessageId, FRpcErrorRpcCodec.MessageId)
                .Declare(Method.Unary(HealthStatus.Method, FRpcHealthRequestRpcCodec.MessageId, FRpcHealthResponseRpcCodec.MessageId))
                .Build();
            RpcConnection connection;
            try
            {
                connection = RpcConnection.Connect(address, schema, registry).GetAwaiter().GetResult();
            }
            catch (Exception failed)
            {
                throw new HealthException($"{address}: {failed.Message}");
            }
            using (connection)
            {
                var writer = new Writer();
                FRpcHealthRequestRpcCodec.Encode(writer, new FRpcHealthRequest { Service = service });
                var next = connection.Call(FRpcHealthRequestRpcCodec.MessageId, writer.ToArray(), CallOptions.WithoutDeadline).Next(Patience)
                    ?? throw new HealthException($"no answer within {Patience.TotalSeconds} s");
                var outcome = next.Outcome
                    ?? throw new HealthException($"the call was refused before sending: {next.Refusal}");
                switch (outcome.Kind)
                {
                    case OutcomeKind.Response:
                        var response = new FRpcHealthResponse();
                        try
                        {
                            var reader = new Reader(outcome.Body);
                            FRpcHealthResponseRpcCodec.Decode(ref reader, ref response);
                        }
                        catch (Exception broken)
                        {
                            throw new HealthException($"{HealthStatus.Method} response does not decode: {broken.Message}");
                        }
                        return response.Status;
                    case OutcomeKind.Failed:
                        throw new HealthException(outcome.Status!.ToString());
                    default:
                        throw new HealthException($"{HealthStatus.Method} received {outcome}");
                }
            }
        }

        private sealed class HealthException : Exception
        {
            public HealthException(string message) : base(message)
            {
            }
        }
    }
}
