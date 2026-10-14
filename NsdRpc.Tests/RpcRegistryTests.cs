using System.Reflection;
using System.Reflection.Emit;
using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

// The one public marked handler in the test assembly. Every other marked
// class in this assembly must keep a unique, nonzero, conforming method id,
// because ScanAssemblyRegistersMarkedHandlers scans the whole assembly.
[RpcMethod(100)]
public sealed class ScannableHandler : IRpcHandler<Handshake, Handshake>
{
    public ValueTask<RpcResult<Handshake>> ExecuteAsync(
        Handshake request, RpcContext context, CancellationToken cancellationToken)
    {
        return new ValueTask<RpcResult<Handshake>>(
            RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
    }
}

public sealed class RpcRegistryTests
{
    private sealed class UnmarkedHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return default;
        }
    }

    [Fact]
    public void AddFailsFastOnMissingAttribute()
    {
        var registry = new RpcRegistry();

        var exception = Assert.Throws<ArgumentException>(() => registry.Add(new UnmarkedHandler()));

        Assert.Contains("RpcMethod", exception.Message);
    }

    [Fact]
    public void AddFailsFastOnZeroMethod()
    {
        var registry = new RpcRegistry();
        IRpcHandler<Handshake, Handshake> handler = EmitHandler(0UL);

        var exception = Assert.Throws<ArgumentException>(() => registry.Add(handler));

        Assert.Contains("zero", exception.Message);
    }

    [Fact]
    public void AddFailsFastOnDuplicateMethod()
    {
        var registry = new RpcRegistry();
        IRpcHandler<Handshake, Handshake> first = EmitHandler(77UL);
        IRpcHandler<Handshake, Handshake> second = EmitHandler(77UL);
        registry.Add(first);

        var exception = Assert.Throws<ArgumentException>(() => registry.Add(second));

        Assert.Contains("Duplicate", exception.Message);
    }

    [Fact]
    public void AddAfterFreezeThrows()
    {
        var registry = new RpcRegistry();
        IRpcHandler<Handshake, Handshake> handler = EmitHandler(50UL);
        registry.Add(handler);
        registry.Freeze();

        Assert.Throws<InvalidOperationException>(() => registry.Add(handler));
    }

    [Fact]
    public void AddRejectsNullHandler()
    {
        var registry = new RpcRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Add<Handshake, Handshake>(null!));
    }

    [Fact]
    public async Task ScanAssemblyRegistersMarkedHandlers()
    {
        var registry = new RpcRegistry();

        registry.ScanAssembly(typeof(ScannableHandler).Assembly);
        registry.Freeze();

        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, registry);

        RpcResult<Handshake> result = await RpcTestHelpers.RoundTrip(
            clientConn, serverConn, client, server, gate, 100, new Handshake(41));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.ProtocolVersion);
    }

    [Fact]
    public void ScanAssemblyFailsFastOnMarkedClassWithoutInterface()
    {
        Assembly fixture = EmitMarkedClass("NoInterface", _ => { });
        var registry = new RpcRegistry();

        var exception = Assert.Throws<ArgumentException>(() => registry.ScanAssembly(fixture));

        Assert.Contains("IRpcHandler", exception.Message);
    }

    [Fact]
    public void ScanAssemblyFailsFastOnMarkedClassWithoutParameterlessConstructor()
    {
        Assembly fixture = EmitMarkedClass("NoCtor", type =>
        {
            ImplementHandlerInterface(type);
            ConstructorBuilder constructor = type.DefineConstructor(
                MethodAttributes.Public, CallingConventions.Standard, [typeof(int)]);
            ILGenerator constructorIl = constructor.GetILGenerator();
            constructorIl.Emit(OpCodes.Ldarg_0);
            constructorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            constructorIl.Emit(OpCodes.Ret);
        });
        var registry = new RpcRegistry();

        var exception = Assert.Throws<ArgumentException>(() => registry.ScanAssembly(fixture));

        Assert.Contains("parameterless", exception.Message);
    }

    // Emits a handler class in a fresh dynamic assembly. The class carries the
    // RpcMethod attribute, implements the handler interface, and throws from
    // ExecuteAsync. The body never runs in these tests.
    private static IRpcHandler<Handshake, Handshake> EmitHandler(ulong method)
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("HandlerFixtures" + method), AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule("module");
        TypeBuilder type = module.DefineType("Handler", TypeAttributes.Public | TypeAttributes.Class);
        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(RpcMethodAttribute).GetConstructor([typeof(ulong), typeof(RpcDirection)])!,
            [method, RpcDirection.Both]));
        ImplementHandlerInterface(type);
        Type created = type.CreateType()!;
        return (IRpcHandler<Handshake, Handshake>)Activator.CreateInstance(created)!;
    }

    private static Assembly EmitMarkedClass(string name, Action<TypeBuilder> configure)
    {
        var assemblyName = new AssemblyName("ScanFixtures" + name);
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule("module");
        TypeBuilder type = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Class);
        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(RpcMethodAttribute).GetConstructor([typeof(ulong), typeof(RpcDirection)])!,
            [101UL, RpcDirection.Both]));
        configure(type);
        type.CreateType();
        return assembly;
    }

    private static void ImplementHandlerInterface(TypeBuilder type)
    {
        type.AddInterfaceImplementation(typeof(IRpcHandler<Handshake, Handshake>));
        MethodInfo execute = typeof(IRpcHandler<Handshake, Handshake>).GetMethod("ExecuteAsync")!;
        Type[] parameters = [typeof(Handshake), typeof(RpcContext), typeof(CancellationToken)];
        MethodBuilder method = type.DefineMethod(
            "ExecuteAsync",
            MethodAttributes.Public | MethodAttributes.Virtual,
            typeof(ValueTask<RpcResult<Handshake>>),
            parameters);
        method.SetSignature(typeof(ValueTask<RpcResult<Handshake>>), null, null, parameters, null, null);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, typeof(NotImplementedException).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Throw);
        type.DefineMethodOverride(method, execute);
    }
}
