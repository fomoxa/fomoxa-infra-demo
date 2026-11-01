using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Fomoxa.Rpc.Generator
{
    internal enum Kind
    {
        Unary,
        ServerStream,
    }

    internal enum Form
    {
        Now,
        NowVoid,
        Later,
        Task,
        Stream,
    }

    internal sealed class Rpc
    {
        public string Name;
        public Kind Kind;
        public Form Form;
        public bool Context;
        public bool NamedRequest;
        public Codec Request;
        public Codec Reply;
        public bool Idempotent;
        public long? Deadline;
        public string Requires;
        public long? Idle;
        public bool Parallel;
        public List<Codec> Details = new List<Codec>();
    }

    internal sealed class Plan
    {
        public string Service;
        public INamedTypeSymbol Interface;
        public List<Rpc> Rpcs = new List<Rpc>();
        public Codec Void;
        public Codec Error;
        public Codec HealthRequest;
        public Codec HealthResponse;
    }

    internal static class Analysis
    {
        public const string ServiceAttribute = "Fomoxa.Rpc.ServiceAttribute";
        public const string UnaryAttribute = "Fomoxa.Rpc.UnaryAttribute";
        public const string ServerStreamAttribute = "Fomoxa.Rpc.ServerStreamAttribute";
        private const string CallContext = "Fomoxa.Rpc.CallContext";
        private const string Reply = "Fomoxa.Rpc.Reply`1";
        private const string ItemStream = "Fomoxa.Rpc.ItemStream`1";
        private const string Task = "System.Threading.Tasks.Task`1";
        private const string Network = "Fomoxa.NetworkAttribute";

        private const string Signatures =
            "\n  valid unary signatures:\n" +
            "    TResponse M([CallContext context,] [TRequest request])\n" +
            "    void      M([CallContext context,] [TRequest request])\n" +
            "    void      M(Reply<TResponse> reply, [TRequest request])\n" +
            "    Task<TResponse> M([CallContext context,] [TRequest request])\n" +
            "  valid server stream signatures:\n" +
            "    void M(ItemStream<TItem> stream, [TRequest request])\n" +
            "  where TRequest, TResponse and TItem are [Network] models with an rpc codec";

        public static Plan Analyze(INamedTypeSymbol declared, AttributeData service, CodecIndex index, List<Diagnostic> diagnostics)
        {
            var plan = new Plan { Interface = declared };
            int before = diagnostics.Count;
            Location serviceLocation = Where(service, declared);
            plan.Service = service.ConstructorArguments.Length == 1 ? service.ConstructorArguments[0].Value as string : null;
            if (string.IsNullOrEmpty(plan.Service) || !plan.Service.All(letter => char.IsLetterOrDigit(letter) || letter == '_'))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidOption, serviceLocation,
                    "a service name is letters, digits and _, e.g. [Service(\"Echo\")]"));
                return null;
            }

            plan.Void = System("FRpcVoid", index, plan.Service, serviceLocation, diagnostics);
            plan.Error = System("FRpcError", index, plan.Service, serviceLocation, diagnostics);
            plan.HealthRequest = System("FRpcHealthRequest", index, plan.Service, serviceLocation, diagnostics);
            plan.HealthResponse = System("FRpcHealthResponse", index, plan.Service, serviceLocation, diagnostics);

            var seen = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);
            foreach (IMethodSymbol method in declared.GetMembers().OfType<IMethodSymbol>().Where(method => method.MethodKind == MethodKind.Ordinary))
            {
                Rpc rpc = Method(plan, method, index, diagnostics);
                if (rpc == null)
                {
                    continue;
                }
                if (seen.TryGetValue(rpc.Request.Model, out string first))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.SharedRequest, Where(method),
                        $"{plan.Service}.{first} and {plan.Service}.{rpc.Name} share the request model {rpc.Request.Model.Name}\n" +
                        "  a method is identified by the message id of its request model (01-design §3.3)\n" +
                        "  give one of them its own request model"));
                    continue;
                }
                seen.Add(rpc.Request.Model, rpc.Name);
                plan.Rpcs.Add(rpc);
            }
            return diagnostics.Count == before ? plan : null;
        }

        private static Codec System(string name, CodecIndex index, string service, Location location, List<Diagnostic> diagnostics)
        {
            Codec codec = index.Named(name);
            if (codec == null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.NotAModel, location,
                    $"the system model {name} has no rpc codec in the tree of fomoxac (service {service})\n" +
                    "  the prepare step adds the system models of Fomoxa.Rpc; import build/Fomoxa.Rpc.targets"));
            }
            return codec;
        }

        private static Rpc Method(Plan plan, IMethodSymbol method, CodecIndex index, List<Diagnostic> diagnostics)
        {
            string full = $"{plan.Service}.{method.Name}";
            AttributeData[] kinds = method.GetAttributes()
                .Where(attribute => Is(attribute.AttributeClass, UnaryAttribute) || Is(attribute.AttributeClass, ServerStreamAttribute))
                .ToArray();
            if (kinds.Length != 1)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidSignature, Where(method),
                    kinds.Length == 0 ? $"{full} has no [Unary] or [ServerStream]{Signatures}" : $"{full} declares its kind twice"));
                return null;
            }
            AttributeData attribute = kinds[0];
            var rpc = new Rpc { Name = method.Name, Kind = Is(attribute.AttributeClass, UnaryAttribute) ? Kind.Unary : Kind.ServerStream };
            string model = null;
            if (!Options(full, rpc, attribute, method, index, diagnostics, ref model))
            {
                return null;
            }
            if (!Signature(full, rpc, method, index, diagnostics))
            {
                return null;
            }
            if (rpc.NamedRequest && model != null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.PolicyNotApplicable, Where(attribute, method),
                    $"Model applies to methods without a model parameter ({full} takes one)"));
                return null;
            }
            if (!rpc.NamedRequest)
            {
                string empty = model ?? plan.Service + method.Name;
                rpc.Request = index.Named(empty);
                if (rpc.Request == null)
                {
                    string space = plan.Interface.ContainingNamespace.IsGlobalNamespace ? "" : plan.Interface.ContainingNamespace.ToDisplayString() + ".";
                    diagnostics.Add(Diagnostic.Create(Diagnostics.NotAModel, Where(method),
                        $"{full} needs the empty model {space}{empty}, which has no rpc codec yet\n" +
                        "  the prepare step writes it: build/Fomoxa.Rpc.targets in a .NET project, the Editor step of com.fomoxa.rpc in Unity"));
                    return null;
                }
            }
            if (rpc.Reply == null)
            {
                rpc.Reply = plan.Void;
            }
            return rpc.Reply == null ? null : rpc;
        }

        private static bool Options(string full, Rpc rpc, AttributeData attribute, IMethodSymbol method, CodecIndex index, List<Diagnostic> diagnostics, ref string model)
        {
            Location location = Where(attribute, method);
            bool valid = true;
            foreach (KeyValuePair<string, TypedConstant> option in attribute.NamedArguments)
            {
                switch (option.Key)
                {
                    case "Idempotent":
                        rpc.Idempotent = option.Value.Value is true;
                        valid &= Only(full, rpc, Kind.Unary, option.Key, rpc.Idempotent, location, diagnostics);
                        break;
                    case "Parallel":
                        rpc.Parallel = option.Value.Value is true;
                        break;
                    case "Deadline":
                        rpc.Deadline = Duration(option.Value.Value as string, location, diagnostics, ref valid);
                        break;
                    case "Idle":
                        valid &= Only(full, rpc, Kind.ServerStream, option.Key, option.Value.Value != null, location, diagnostics);
                        rpc.Idle = Duration(option.Value.Value as string, location, diagnostics, ref valid);
                        break;
                    case "Requires":
                        rpc.Requires = option.Value.Value as string;
                        break;
                    case "Model":
                        model = option.Value.Value as string;
                        break;
                    case "Details":
                        if (!option.Value.IsNull)
                        {
                            foreach (TypedConstant detail in option.Value.Values)
                            {
                                if (detail.Value is ITypeSymbol type)
                                {
                                    Codec codec = index.For(type);
                                    if (codec == null)
                                    {
                                        diagnostics.Add(Diagnostic.Create(Diagnostics.NotAModel, location,
                                            $"{full} uses {type.Name}, which is not a [Network] model with an rpc codec"));
                                        valid = false;
                                    }
                                    else
                                    {
                                        rpc.Details.Add(codec);
                                    }
                                }
                            }
                        }
                        break;
                }
            }
            return valid;
        }

        private static bool Only(string full, Rpc rpc, Kind applies, string option, bool set, Location location, List<Diagnostic> diagnostics)
        {
            if (!set || rpc.Kind == applies)
            {
                return true;
            }
            diagnostics.Add(Diagnostic.Create(Diagnostics.PolicyNotApplicable, location,
                $"{option} applies to {Label(applies)} methods only ({full} is {Label(rpc.Kind)})"));
            return false;
        }

        private static string Label(Kind kind) => kind == Kind.Unary ? "unary" : "server stream";

        private static long? Duration(string text, Location location, List<Diagnostic> diagnostics, ref bool valid)
        {
            if (text == null)
            {
                return null;
            }
            string digits = new string(text.TakeWhile(char.IsDigit).ToArray());
            string unit = text.Substring(digits.Length);
            long scale = unit == "ms" ? 1 : unit == "s" ? 1000 : unit == "m" ? 60000 : unit == "h" ? 3600000 : 0;
            if (scale > 0 && long.TryParse(digits, out long value) && value > 0)
            {
                return value * scale;
            }
            diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidOption, location,
                $"\"{text}\" is not a duration; write a positive whole number and ms, s, m or h"));
            valid = false;
            return null;
        }

        private enum Parameter
        {
            Context,
            Reply,
            Stream,
            Model,
            Other,
        }

        private static bool Signature(string full, Rpc rpc, IMethodSymbol method, CodecIndex index, List<Diagnostic> diagnostics)
        {
            bool Invalid(string extra)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidSignature, Where(method), $"{full} has an invalid signature{Signatures}{extra}"));
                return false;
            }

            if (method.IsGenericMethod || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsParams))
            {
                return Invalid("");
            }
            var kinds = new List<Parameter>();
            ITypeSymbol inner = null;
            ITypeSymbol request = null;
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                ITypeSymbol type = parameter.Type;
                if (Is(type, CallContext))
                {
                    kinds.Add(Parameter.Context);
                }
                else if (Is(type.OriginalDefinition, Reply) || Is(type.OriginalDefinition, ItemStream))
                {
                    kinds.Add(Is(type.OriginalDefinition, Reply) ? Parameter.Reply : Parameter.Stream);
                    inner = ((INamedTypeSymbol)type).TypeArguments[0];
                }
                else if (IsModel(type, index))
                {
                    kinds.Add(Parameter.Model);
                    request = type;
                }
                else
                {
                    kinds.Add(Parameter.Other);
                }
            }
            int models = kinds.Count(kind => kind == Parameter.Model);
            if (kinds.Contains(Parameter.Other) || models > 1)
            {
                IEnumerable<string> fields = method.Parameters.Where((parameter, position) => kinds[position] == Parameter.Model || kinds[position] == Parameter.Other)
                    .Select(parameter => char.ToUpperInvariant(parameter.Name[0]) + parameter.Name.Substring(1));
                return Invalid($"\n  declare a model with {string.Join(" and ", fields)} and take it as the request");
            }
            if (models == 1 && kinds[kinds.Count - 1] != Parameter.Model)
            {
                return Invalid("");
            }
            List<Parameter> shape = kinds.Where(kind => kind != Parameter.Model).ToList();
            ITypeSymbol returned = method.ReturnType;
            bool returnsVoid = method.ReturnsVoid;
            bool returnsTask = Is(returned.OriginalDefinition, Task);
            ITypeSymbol reply = null;
            if (rpc.Kind == Kind.Unary && shape.Count == 1 && shape[0] == Parameter.Reply && returnsVoid)
            {
                rpc.Form = Form.Later;
                reply = inner;
            }
            else if (rpc.Kind == Kind.ServerStream && shape.Count == 1 && shape[0] == Parameter.Stream && returnsVoid)
            {
                rpc.Form = Form.Stream;
                reply = inner;
            }
            else if (rpc.Kind == Kind.Unary && (shape.Count == 0 || (shape.Count == 1 && shape[0] == Parameter.Context)))
            {
                rpc.Context = shape.Count == 1;
                if (returnsVoid)
                {
                    rpc.Form = Form.NowVoid;
                }
                else if (returnsTask)
                {
                    rpc.Form = Form.Task;
                    reply = ((INamedTypeSymbol)returned).TypeArguments[0];
                }
                else if (IsModel(returned, index) || HasNetwork(returned))
                {
                    rpc.Form = Form.Now;
                    reply = returned;
                }
                else
                {
                    return Invalid("");
                }
            }
            else
            {
                return Invalid("");
            }

            if (request != null)
            {
                rpc.NamedRequest = true;
                rpc.Request = index.For(request);
                if (rpc.Request == null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.NotAModel, Where(method),
                        $"{full} uses {request.Name}, which is not a [Network] model with an rpc codec\n" +
                        "  declare it with [Network] and [Codec(\"rpc\")] in a folder listed in fomoxa.toml src"));
                    return false;
                }
            }
            if (reply != null)
            {
                rpc.Reply = index.For(reply);
                if (rpc.Reply == null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.NotAModel, Where(method),
                        $"{full} uses {reply.Name}, which is not a [Network] model with an rpc codec\n" +
                        "  declare it with [Network] and [Codec(\"rpc\")] in a folder listed in fomoxa.toml src"));
                    return false;
                }
            }
            return true;
        }

        private static bool IsModel(ITypeSymbol type, CodecIndex index) => index.For(type) != null || HasNetwork(type);

        private static bool HasNetwork(ITypeSymbol type) =>
            type.TypeKind == TypeKind.Class && type.GetAttributes().Any(attribute => Is(attribute.AttributeClass, Network));

        public static bool Is(ITypeSymbol type, string metadataName)
        {
            if (type == null)
            {
                return false;
            }
            string name = type.ContainingNamespace == null || type.ContainingNamespace.IsGlobalNamespace
                ? type.MetadataName
                : type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;
            return name == metadataName;
        }

        private static Location Where(IMethodSymbol method) => method.Locations.FirstOrDefault() ?? Location.None;

        private static Location Where(AttributeData attribute, ISymbol fallback) =>
            attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallback.Locations.FirstOrDefault() ?? Location.None;
    }
}
