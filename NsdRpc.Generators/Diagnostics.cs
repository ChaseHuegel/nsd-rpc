using Microsoft.CodeAnalysis;

namespace NsdRpc.Generators;

internal static class Diagnostics
{
    private const string Category = "NsdRpc.Generators";

    public static readonly DiagnosticDescriptor NotAnInterface = new(
        id: "RSG1001",
        title: "RpcService must mark an interface",
        messageFormat: "The RpcService attribute may only be used on an interface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingMethodAttribute = new(
        id: "RSG1002",
        title: "Service method needs RpcMethod",
        messageFormat: "Service method '{0}' needs an RpcMethod attribute",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonConstantMethodId = new(
        id: "RSG1003",
        title: "Method id must be a constant",
        messageFormat: "The RpcMethod id on '{0}' must be a compile-time ulong constant",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequestParameter = new(
        id: "RSG1004",
        title: "Service method needs one request parameter",
        messageFormat: "Service method '{0}' must have exactly one request parameter passed by value or in",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequestMustBeStruct = new(
        id: "RSG1005",
        title: "Request parameter must be a struct",
        messageFormat: "The request parameter of '{0}' must be a struct",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedReturnType = new(
        id: "RSG1006",
        title: "Unsupported service method return type",
        messageFormat: "Service method '{0}' must return Task<RpcResult<T>> or RpcStreamSession<TChunk, T>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PayloadMustBeStruct = new(
        id: "RSG1007",
        title: "Response and chunk types must be structs",
        messageFormat: "The response and chunk types of '{0}' must be structs",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateMethodId = new(
        id: "RSG1008",
        title: "Duplicate method id",
        messageFormat: "Method id {0} is used more than once in the service",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
