; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RSG1001 | NsdRpc.Generators | Error | RpcService must mark an interface
RSG1002 | NsdRpc.Generators | Error | Service method needs RpcMethod
RSG1003 | NsdRpc.Generators | Error | Method id must be a compile-time ulong constant
RSG1004 | NsdRpc.Generators | Error | Service method needs one request parameter
RSG1005 | NsdRpc.Generators | Error | Request parameter must be a struct
RSG1006 | NsdRpc.Generators | Error | Unsupported service method return type
RSG1007 | NsdRpc.Generators | Error | Response and chunk types must be structs
RSG1008 | NsdRpc.Generators | Error | Duplicate method id
