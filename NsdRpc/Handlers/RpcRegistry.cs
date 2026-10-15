using System.Linq;
using System.Reflection;

namespace NsdRpc;

/// <summary>
/// The handler registry. Build it at startup, then freeze it.
/// </summary>
public sealed class RpcRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<ulong, HandlerEntry> _entries = new();
    private Dictionary<ulong, HandlerEntry>? _snapshot;
    private bool _frozen;

    public void Add<TRequest, TResponse>(IRpcHandler<TRequest, TResponse> handler)
        where TRequest : struct
        where TResponse : struct
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        RpcMethodAttribute? attribute = handler.GetType().GetCustomAttribute<RpcMethodAttribute>(inherit: true);
        if (attribute == null)
        {
            throw new ArgumentException("The handler class needs an RpcMethod attribute.", nameof(handler));
        }

        if (attribute.Method == 0)
        {
            throw new ArgumentException("Method zero is not a valid method id.", nameof(handler));
        }

        lock (_lock)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("The registry is frozen.");
            }

            if (_entries.ContainsKey(attribute.Method))
            {
                throw new ArgumentException("Duplicate method id " + attribute.Method + ".", nameof(handler));
            }

            var entry = new HandlerEntry<TRequest, TResponse>(
                handler, PayloadSerializerCache<TRequest>.Value, attribute.Direction);
            _entries.Add(attribute.Method, entry);
        }
    }

    public void Add<TRequest, TResponse>(IRpcHandler<TRequest, TResponse> handler, ulong methodId, RpcDirection direction = RpcDirection.Both)
        where TRequest : struct
        where TResponse : struct
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        if (methodId == 0)
        {
            throw new ArgumentException("Method zero is not a valid method id.", nameof(methodId));
        }

        lock (_lock)
        {
            if (_frozen)
            {
                throw new InvalidOperationException("The registry is frozen.");
            }

            if (_entries.ContainsKey(methodId))
            {
                throw new ArgumentException("Duplicate method id " + methodId + ".", nameof(methodId));
            }

            var entry = new HandlerEntry<TRequest, TResponse>(
                handler, PayloadSerializerCache<TRequest>.Value, direction);
            _entries.Add(methodId, entry);
        }
    }

    public void AddService(IRpcServiceRegistrar service)
    {
        if (service == null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        service.RegisterHandlers(this);
    }

    public void ScanAssembly(Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        foreach (Type type in assembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract)
            {
                continue;
            }

            if (type.GetCustomAttribute<RpcMethodAttribute>(inherit: true) == null)
            {
                continue;
            }

            Type? handlerInterface = FindHandlerInterface(type);
            if (handlerInterface == null)
            {
                throw new ArgumentException(
                    "Class " + type.FullName + " has an RpcMethod attribute but no IRpcHandler<,> interface.",
                    nameof(assembly));
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new ArgumentException(
                    "Class " + type.FullName + " has an RpcMethod attribute but no parameterless constructor.",
                    nameof(assembly));
            }

            object instance = Activator.CreateInstance(type)!;
            Type[] arguments = handlerInterface.GetGenericArguments();
            MethodInfo add = typeof(RpcRegistry).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Single(m => m.Name == nameof(Add) && m.GetParameters().Length == 1);
            add.MakeGenericMethod(arguments[0], arguments[1]).Invoke(this, [instance]);
        }
    }

    public void Freeze()
    {
        lock (_lock)
        {
            _frozen = true;
            _snapshot = new Dictionary<ulong, HandlerEntry>(_entries);
        }
    }

    internal bool TryGet(ulong method, out HandlerEntry entry)
    {
        lock (_lock)
        {
            _snapshot ??= new Dictionary<ulong, HandlerEntry>(_entries);
            return _snapshot.TryGetValue(method, out entry!);
        }
    }

    private static Type? FindHandlerInterface(Type type)
    {
        foreach (Type iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IRpcHandler<,>))
            {
                return iface;
            }
        }

        return null;
    }
}
