namespace NsdRpc;

/// <summary>
/// Marks a handler class with its method id. The id is a wire contract.
/// Never change it between builds.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RpcMethodAttribute : Attribute
{
    public RpcMethodAttribute(ulong method, RpcDirection direction = RpcDirection.Both)
    {
        Method = method;
        Direction = direction;
    }

    /// <summary>The method id. Zero is never valid.</summary>
    public ulong Method { get; }

    /// <summary>Which side may handle the method.</summary>
    public RpcDirection Direction { get; }
}
