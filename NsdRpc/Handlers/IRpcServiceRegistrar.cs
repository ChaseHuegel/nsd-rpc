namespace NsdRpc;

/// <summary>
/// A service that registers its handlers with a registry. Generated service
/// bases implement it.
/// </summary>
public interface IRpcServiceRegistrar
{
    /// <summary>Registers every handler of the service with the registry.</summary>
    void RegisterHandlers(RpcRegistry registry);
}
