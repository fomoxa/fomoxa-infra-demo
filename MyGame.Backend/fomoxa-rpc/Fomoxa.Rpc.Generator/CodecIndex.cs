using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Fomoxa.Rpc.Generator
{
    internal sealed class Codec
    {
        public Codec(INamedTypeSymbol type, ITypeSymbol model, ITypeSymbol writer, ITypeSymbol reader)
        {
            Type = type;
            Model = model;
            Writer = writer;
            Reader = reader;
        }

        public INamedTypeSymbol Type { get; }

        public ITypeSymbol Model { get; }

        public ITypeSymbol Writer { get; }

        public ITypeSymbol Reader { get; }
    }

    internal sealed class CodecIndex
    {
        public const string RpcSuffix = ".rpc";

        private readonly Dictionary<ITypeSymbol, Codec> byModel = new Dictionary<ITypeSymbol, Codec>(SymbolEqualityComparer.Default);
        private readonly Dictionary<string, Codec> byName = new Dictionary<string, Codec>();

        public CodecIndex(Compilation compilation)
        {
            Visit(compilation.Assembly.GlobalNamespace);
        }

        public Codec For(ITypeSymbol model) => byModel.TryGetValue(model, out Codec codec) ? codec : null;

        public Codec Named(string model) => byName.TryGetValue(model, out Codec codec) ? codec : null;

        private void Visit(INamespaceSymbol space)
        {
            foreach (INamespaceOrTypeSymbol member in space.GetMembers())
            {
                if (member is INamespaceSymbol child)
                {
                    Visit(child);
                }
                else if (member is INamedTypeSymbol type)
                {
                    Visit(type);
                }
            }
        }

        private void Visit(INamedTypeSymbol type)
        {
            if (type.IsStatic && IsRpc(type))
            {
                IMethodSymbol encode = type.GetMembers("Encode").OfType<IMethodSymbol>()
                    .FirstOrDefault(method => method.IsStatic && method.Parameters.Length == 2);
                IMethodSymbol decode = type.GetMembers("Decode").OfType<IMethodSymbol>()
                    .FirstOrDefault(method => method.IsStatic && method.Parameters.Length == 2 && method.Parameters[0].RefKind == RefKind.Ref);
                if (encode != null && decode != null)
                {
                    var codec = new Codec(type, encode.Parameters[1].Type, encode.Parameters[0].Type, decode.Parameters[0].Type);
                    byModel[codec.Model] = codec;
                    byName[codec.Model.Name] = codec;
                }
            }

            foreach (INamedTypeSymbol nested in type.GetTypeMembers())
            {
                Visit(nested);
            }
        }

        private static bool IsRpc(INamedTypeSymbol type) =>
            type.GetMembers("MessageName").OfType<IFieldSymbol>().Any(field =>
                field.IsConst && field.ConstantValue is string name && name.EndsWith(RpcSuffix, System.StringComparison.Ordinal));
    }
}
