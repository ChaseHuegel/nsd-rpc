using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NsdRpc.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class RpcServiceGenerator : IIncrementalGenerator
{
    private const string AttributeResourceName = "NsdRpc.RpcServiceAttribute.cs";

    // The marker attribute source is embedded in the generator assembly. It is
    // the same file the generator project compiles (guarded away), so a
    // consumer compilation gets it by emission, not by an additional file.
    private static readonly Lazy<string> AttributeSource = new(
        () =>
        {
            using Stream? stream = typeof(RpcServiceGenerator).Assembly.GetManifestResourceStream(AttributeResourceName);
            if (stream == null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        });

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ServiceModel> services = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is BaseTypeDeclarationSyntax decl && HasServiceAttribute(decl),
                static (ctx, ct) => BuildService(ctx, ct));

        context.RegisterSourceOutput(services.Collect(), static (spc, models) =>
        {
            foreach (ServiceModel model in models)
            {
                foreach (Diagnostic diagnostic in model.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }
            }

            if (models.IsEmpty)
            {
                return;
            }

            // The marker attribute always resolves, even for a malformed service,
            // so only the RSG diagnostics point at the real problem.
            string attributeSource = AttributeSource.Value;
            if (attributeSource.Length > 0)
            {
                spc.AddSource("NsdRpc.RpcServiceAttribute.g.cs", attributeSource);
            }

            foreach (ServiceModel model in models)
            {
                if (model.Methods.Count > 0 && model.Diagnostics.Count == 0)
                {
                    spc.AddSource(model.ClientHintName, GenerateClient(model));
                    EmitHandlers(spc, model);
                }
            }
        });
    }

    private static bool HasServiceAttribute(BaseTypeDeclarationSyntax decl)
    {
        foreach (AttributeListSyntax list in decl.AttributeLists)
        {
            foreach (AttributeSyntax attribute in list.Attributes)
            {
                string name = attribute.Name.ToString();
                if (name is "RpcService" or "RpcServiceAttribute")
                {
                    return true;
                }

                if (name.EndsWith(".RpcService", StringComparison.Ordinal)
                    || name.EndsWith(".RpcServiceAttribute", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ServiceModel BuildService(GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
    {
        var decl = (BaseTypeDeclarationSyntax)ctx.Node;
        var model = new ServiceModel();

        if (ctx.SemanticModel.GetDeclaredSymbol(decl, cancellationToken) is not INamedTypeSymbol symbol
            || symbol.TypeKind != TypeKind.Interface)
        {
            model.Diagnostics.Add(Diagnostic.Create(Diagnostics.NotAnInterface, decl.GetLocation()));
            return model;
        }

        model.Namespace = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        model.InterfaceName = symbol.Name;
        model.ClientClassName = ClientName(symbol.Name);
        model.InterfaceFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        model.Accessibility = symbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        foreach (IMethodSymbol method in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind == Microsoft.CodeAnalysis.MethodKind.Ordinary)
            {
                BuildMethod(method, model);
            }
        }

        Dictionary<ulong, string> idOwners = [];
        foreach (MethodModel method in model.Methods)
        {
            if (idOwners.TryGetValue(method.MethodId, out string? owner) && owner != method.Name)
            {
                model.Diagnostics.Add(
                    Diagnostic.Create(Diagnostics.DuplicateMethodId, decl.GetLocation(), method.MethodId));
            }
            else
            {
                idOwners[method.MethodId] = method.Name;
            }
        }

        return model;
    }

    private static void BuildMethod(IMethodSymbol method, ServiceModel model)
    {
        Location location = method.Locations.FirstOrDefault() ?? Location.None;

        AttributeData? attribute = null;
        foreach (AttributeData candidate in method.GetAttributes())
        {
            string? name = candidate.AttributeClass?.Name;
            if (name is "RpcMethod" or "RpcMethodAttribute")
            {
                attribute = candidate;
                break;
            }
        }

        if (attribute == null)
        {
            model.Diagnostics.Add(Diagnostic.Create(Diagnostics.MissingMethodAttribute, location, method.Name));
            return;
        }

        if (attribute.ConstructorArguments.Length == 0
            || attribute.ConstructorArguments[0].Kind != TypedConstantKind.Primitive
            || attribute.ConstructorArguments[0].Value == null)
        {
            model.Diagnostics.Add(Diagnostic.Create(Diagnostics.NonConstantMethodId, location, method.Name));
            return;
        }

        ulong methodId;
        try
        {
            methodId = Convert.ToUInt64(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            model.Diagnostics.Add(Diagnostic.Create(Diagnostics.NonConstantMethodId, location, method.Name));
            return;
        }

        string direction = "Both";
        if (attribute.ConstructorArguments.Length > 1 && attribute.ConstructorArguments[1].Value != null)
        {
            int value = Convert.ToInt32(attribute.ConstructorArguments[1].Value, CultureInfo.InvariantCulture);
            direction = value switch
            {
                1 => "ClientToServer",
                2 => "ServerToClient",
                _ => "Both",
            };
        }

        if (!TryGetReturnShape(
                method.ReturnType, out CallKind kind, out INamedTypeSymbol? responseType, out INamedTypeSymbol? chunkType))
        {
            model.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedReturnType, location, method.Name));
            return;
        }

        if (responseType == null || !responseType.IsValueType || (chunkType != null && !chunkType.IsValueType))
        {
            model.Diagnostics.Add(Diagnostic.Create(Diagnostics.PayloadMustBeStruct, location, method.Name));
            return;
        }

        var methodModel = new MethodModel
        {
            Name = method.Name,
            MethodId = methodId,
            Kind = kind,
            Direction = direction,
            ResponseType = responseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ChunkType = chunkType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        };

        IParameterSymbol? request = null;
        IParameterSymbol? timeout = null;
        IParameterSymbol? sink = null;
        IParameterSymbol? token = null;
        bool requestByRef = false;

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (IsCancellationToken(parameter.Type))
            {
                token = parameter;
                continue;
            }

            if (IsNullableTimeSpan(parameter.Type))
            {
                timeout = parameter;
                continue;
            }

            if (parameter.Type is INamedTypeSymbol { Name: "IChunkSink", TypeArguments.Length: 1 })
            {
                sink = parameter;
                continue;
            }

            if (request != null || parameter.RefKind is RefKind.Ref or RefKind.Out)
            {
                model.Diagnostics.Add(Diagnostic.Create(Diagnostics.RequestParameter, location, method.Name));
                return;
            }

            request = parameter;
            requestByRef = parameter.RefKind == RefKind.In;
        }

        if (request == null || !request.Type.IsValueType)
        {
            if (request == null)
            {
                model.Diagnostics.Add(Diagnostic.Create(Diagnostics.RequestParameter, location, method.Name));
            }
            else
            {
                model.Diagnostics.Add(Diagnostic.Create(Diagnostics.RequestMustBeStruct, location, method.Name));
            }

            return;
        }

        methodModel.RequestName = request.Name;
        methodModel.RequestType = request.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        methodModel.RequestRef = requestByRef ? "in " : string.Empty;
        methodModel.TimeoutName = timeout?.Name;
        methodModel.TokenName = token?.Name;
        methodModel.SinkName = sink?.Name;

        if (sink != null && kind != CallKind.Plain)
        {
            model.Diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedReturnType, location, method.Name));
            return;
        }

        if (sink != null)
        {
            methodModel.Kind = CallKind.Sink;
            var sinkType = (INamedTypeSymbol)sink.Type;
            methodModel.ChunkType = sinkType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        // Rebuild the parameter list in declaration order so the implementation
        // mirrors the interface signature exactly.
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            methodModel.ParameterDeclarations.Add(ParameterDeclaration(parameter));
        }

        model.Methods.Add(methodModel);
    }

    private static bool TryGetReturnShape(
        ITypeSymbol returnType,
        out CallKind kind,
        out INamedTypeSymbol? responseType,
        out INamedTypeSymbol? chunkType)
    {
        kind = CallKind.Plain;
        responseType = null;
        chunkType = null;

        if (returnType is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return false;
        }

        ImmutableArray<ITypeSymbol> args = named.TypeArguments;

        if (named.Name == "Task" && args.Length == 1
            && args[0] is INamedTypeSymbol { Name: "RpcResult", TypeArguments.Length: 1 } result)
        {
            kind = CallKind.Plain;
            responseType = (INamedTypeSymbol)result.TypeArguments[0];
            return true;
        }

        if (named.Name == "RpcStreamSession" && args.Length == 2)
        {
            kind = CallKind.StreamSession;
            chunkType = (INamedTypeSymbol)args[0];
            responseType = (INamedTypeSymbol)args[1];
            return true;
        }

        return false;
    }

    private static string ParameterDeclaration(IParameterSymbol parameter)
    {
        string type = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string refKind = parameter.RefKind switch
        {
            RefKind.In => "in ",
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            _ => string.Empty,
        };
        string defaultSuffix = parameter.HasExplicitDefaultValue ? " = default" : string.Empty;
        return refKind + type + " " + parameter.Name + defaultSuffix;
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { Name: "CancellationToken" } token
            && token.ContainingNamespace.ToDisplayString() == "System.Threading";
    }

    private static bool IsNullableTimeSpan(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { Name: "Nullable", TypeArguments.Length: 1 } nullable
            && nullable.TypeArguments[0] is INamedTypeSymbol { Name: "TimeSpan" }
            && nullable.TypeArguments[0].ContainingNamespace.Name == "System";
    }

    private static string ClientName(string interfaceName)
    {
        string baseName = interfaceName.StartsWith("I", StringComparison.Ordinal) && interfaceName.Length > 1
            ? interfaceName.Substring(1)
            : interfaceName;
        return baseName + "Client";
    }

    private static string GenerateClient(ServiceModel model)
    {
        bool namespaced = model.Namespace.Length > 0;
        string indent = namespaced ? "    " : string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1591 // missing XML comment");
        if (namespaced)
        {
            sb.Append("namespace ").Append(model.Namespace).AppendLine();
            sb.AppendLine("{");
        }

        sb.Append(indent).Append(model.Accessibility).Append(" sealed class ").Append(model.ClientClassName)
            .Append(" : ").Append(model.InterfaceFullName).AppendLine();
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    private readonly global::NsdRpc.IRpcCaller _caller;");
        sb.AppendLine();
        sb.Append(indent).Append("    public ").Append(model.ClientClassName)
            .AppendLine("(global::NsdRpc.RpcClient client)");
        sb.Append(indent).AppendLine("    {");
        sb.Append(indent).AppendLine("        _caller = client;");
        sb.Append(indent).AppendLine("    }");
        sb.AppendLine();
        sb.Append(indent).Append("    public ").Append(model.ClientClassName)
            .AppendLine("(global::NsdRpc.RpcPeer peer)");
        sb.Append(indent).AppendLine("    {");
        sb.Append(indent).AppendLine("        _caller = peer;");
        sb.Append(indent).AppendLine("    }");
        foreach (MethodModel method in model.Methods)
        {
            sb.AppendLine();
            EmitMethod(sb, indent, method);
        }

        sb.Append(indent).AppendLine("}");
        if (namespaced)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static void EmitMethod(StringBuilder sb, string indent, MethodModel method)
    {
        string returnType = method.Kind == CallKind.StreamSession
            ? "global::NsdRpc.RpcStreamSession<" + method.ChunkType + ", " + method.ResponseType + ">"
            : "global::System.Threading.Tasks.Task<global::NsdRpc.RpcResult<" + method.ResponseType + ">>";

        sb.Append(indent).Append("    public ").Append(returnType).Append(' ').Append(method.Name).Append('(');
        sb.Append(string.Join(", ", method.ParameterDeclarations));
        sb.AppendLine(")");
        sb.Append(indent).AppendLine("    {");

        string timeoutArg = method.TimeoutName ?? "null";
        string tokenArg = method.TokenName ?? "default";
        string generics = "<" + method.RequestType + ", "
            + (method.ChunkType != null ? method.ChunkType + ", " : string.Empty) + method.ResponseType + ">";
        string arguments = method.MethodId + "UL, " + method.RequestRef + method.RequestName;
        if (method.Kind == CallKind.Sink)
        {
            arguments += ", " + method.SinkName;
        }

        arguments += ", " + timeoutArg + ", " + tokenArg;

        string call = method.Kind == CallKind.Plain
            ? "_caller.InvokeAsync"
            : "_caller.InvokeStreamAsync";

        sb.Append(indent).Append("        return ").Append(call).Append(generics).Append('(')
            .Append(arguments).AppendLine(");");
        sb.Append(indent).AppendLine("    }");
    }

    private static void EmitHandlers(SourceProductionContext spc, ServiceModel model)
    {
        HashSet<string> emitted = [];
        foreach (MethodModel method in model.Methods)
        {
            if (!emitted.Add(method.Name))
            {
                continue;
            }

            string hint = (model.Namespace.Length > 0 ? model.Namespace + "." : string.Empty)
                + method.Name + "Handler.g.cs";
            spc.AddSource(hint, GenerateHandler(model, method));
        }
    }

    private static string GenerateHandler(ServiceModel model, MethodModel method)
    {
        bool namespaced = model.Namespace.Length > 0;
        string indent = namespaced ? "    " : string.Empty;
        string returnType = "global::System.Threading.Tasks.ValueTask<global::NsdRpc.RpcResult<"
            + method.ResponseType + ">>";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1591 // missing XML comment");
        if (namespaced)
        {
            sb.Append("namespace ").Append(model.Namespace).AppendLine();
            sb.AppendLine("{");
        }

        sb.Append(indent).Append("[global::NsdRpc.RpcMethod(").Append(method.MethodId).Append("UL");
        if (method.Direction != "Both")
        {
            sb.Append(", global::NsdRpc.RpcDirection.").Append(method.Direction);
        }

        sb.AppendLine(")]");
        sb.Append(indent).Append(model.Accessibility).Append(" abstract class ").Append(method.Name)
            .Append("Handler : global::NsdRpc.IRpcHandler<")
            .Append(method.RequestType).Append(", ").Append(method.ResponseType).AppendLine(">");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).Append("    public ").Append(returnType).AppendLine(" ExecuteAsync(");
        sb.Append(indent).Append("        ").Append(method.RequestType).AppendLine(" request,");
        sb.Append(indent).AppendLine("        global::NsdRpc.RpcContext context,");
        sb.Append(indent).AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        sb.Append(indent).Append("        => ").Append(method.Name).AppendLine("(request, context, cancellationToken);");
        sb.AppendLine();
        sb.Append(indent).Append("    protected abstract ").Append(returnType).Append(' ').Append(method.Name)
            .AppendLine("(");
        sb.Append(indent).Append("        ").Append(method.RequestType).AppendLine(" request,");
        sb.Append(indent).AppendLine("        global::NsdRpc.RpcContext context,");
        sb.Append(indent).AppendLine("        global::System.Threading.CancellationToken cancellationToken);");
        sb.Append(indent).AppendLine("}");
        if (namespaced)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private enum CallKind
    {
        Plain,
        StreamSession,
        Sink,
    }

    private sealed class ServiceModel
    {
        public string Namespace { get; set; } = string.Empty;

        public string InterfaceName { get; set; } = string.Empty;

        public string ClientClassName { get; set; } = string.Empty;

        public string InterfaceFullName { get; set; } = string.Empty;

        public string Accessibility { get; set; } = "public";

        public string ClientHintName => (Namespace.Length > 0 ? Namespace + "." : string.Empty) + ClientClassName + ".g.cs";

        public List<MethodModel> Methods { get; } = [];

        public List<Diagnostic> Diagnostics { get; } = [];
    }

    private sealed class MethodModel
    {
        public string Name { get; set; } = string.Empty;

        public ulong MethodId { get; set; }

        public CallKind Kind { get; set; }

        public string RequestType { get; set; } = string.Empty;

        public string RequestName { get; set; } = string.Empty;

        public string RequestRef { get; set; } = string.Empty;

        public string ResponseType { get; set; } = string.Empty;

        public string? ChunkType { get; set; }

        public string? TimeoutName { get; set; }

        public string? TokenName { get; set; }

        public string? SinkName { get; set; }

        public string Direction { get; set; } = "Both";

        public List<string> ParameterDeclarations { get; } = [];
    }
}
