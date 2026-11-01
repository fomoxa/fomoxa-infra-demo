using Microsoft.CodeAnalysis;

namespace Fomoxa.Rpc.Generator
{
    internal static class Diagnostics
    {
        private const string Category = "fRPC";

        public static readonly DiagnosticDescriptor SharedRequest = Error("FRPC001", "Two methods share a request model");

        public static readonly DiagnosticDescriptor InvalidSignature = Error("FRPC002", "Invalid method signature");

        public static readonly DiagnosticDescriptor NotAModel = Error("FRPC003", "A type is not a model");

        public static readonly DiagnosticDescriptor PolicyNotApplicable = Error("FRPC004", "An option does not apply to the kind of call");

        public static readonly DiagnosticDescriptor InvalidOption = Error("FRPC005", "An option has an invalid value");

        private static DiagnosticDescriptor Error(string id, string title) =>
            new DiagnosticDescriptor(id, title, "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    }
}
