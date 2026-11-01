using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Fomoxa.Rpc.Generator
{
    [Generator(LanguageNames.CSharp)]
    public sealed class ServiceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValueProvider<ImmutableArray<INamedTypeSymbol>> services = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    Analysis.ServiceAttribute,
                    (node, cancellationToken) => node is InterfaceDeclarationSyntax,
                    (attributeContext, cancellationToken) => (INamedTypeSymbol)attributeContext.TargetSymbol)
                .Collect();
            context.RegisterSourceOutput(services.Combine(context.CompilationProvider), (output, input) => Generate(output, input.Right, input.Left));
        }

        private static void Generate(SourceProductionContext output, Compilation compilation, ImmutableArray<INamedTypeSymbol> services)
        {
            if (services.IsDefaultOrEmpty)
            {
                return;
            }
            var index = new CodecIndex(compilation);
            foreach (INamedTypeSymbol service in services.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).OrderBy(service => service.ToDisplayString(), System.StringComparer.Ordinal))
            {
                AttributeData attribute = service.GetAttributes().First(data => Analysis.Is(data.AttributeClass, Analysis.ServiceAttribute));
                var diagnostics = new List<Diagnostic>();
                Plan plan = Analysis.Analyze(service, attribute, index, diagnostics);
                foreach (Diagnostic diagnostic in diagnostics)
                {
                    output.ReportDiagnostic(diagnostic);
                }
                if (plan != null)
                {
                    output.AddSource(Emitter.HintName(plan), SourceText.From(Emitter.Emit(plan), System.Text.Encoding.UTF8));
                }
            }
        }
    }
}
