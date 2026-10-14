using System.IO;
using System.Reflection;

namespace NsdRpc;

// The default payload serializer. It binds the generated nsd methods once per
// type and calls them through delegates. No reflection runs on the send path.
internal sealed class NsdPayloadSerializer<T> : IPayloadSerializer<T>
    where T : struct
{
    private readonly GetSizeDelegate<T> _getSize;
    private readonly SerializeIntoDelegate<T> _serializeInto;
    private readonly DeserializeDelegate<T> _deserialize;

    private NsdPayloadSerializer(
        GetSizeDelegate<T> getSize,
        SerializeIntoDelegate<T> serializeInto,
        DeserializeDelegate<T> deserialize)
    {
        _getSize = getSize;
        _serializeInto = serializeInto;
        _deserialize = deserialize;
    }

    // The generated methods are readonly instance methods. Their receiver is a
    // readonly reference, so the delegates bind the first parameter with `in`.
    internal delegate int GetSizeDelegate<TValue>(in TValue value);

    internal delegate int SerializeIntoDelegate<TValue>(in TValue value, Span<byte> buffer, int start);

    internal delegate TValue DeserializeDelegate<TValue>(ReadOnlySpan<byte> data);

    public static NsdPayloadSerializer<T> Build()
    {
        Type type = typeof(T);
        MethodInfo? getSize = type.GetMethod("GetSize", Type.EmptyTypes);
        MethodInfo? serializeInto = type.GetMethod(
            "SerializeInto", new[] { typeof(Span<byte>), typeof(int) });
        MethodInfo? deserialize = type.GetMethod(
            "Deserialize", new[] { typeof(ReadOnlySpan<byte>) });

        Invariant.That(
            getSize != null, "Payload type " + type.FullName + " has no GetSize method. Use an nsd message.");
        Invariant.That(
            serializeInto != null,
            "Payload type " + type.FullName + " has no SerializeInto method. Use an nsd message.");
        Invariant.That(
            deserialize != null,
            "Payload type " + type.FullName + " has no Deserialize method. Use an nsd message.");

        var getSizeDelegate = (GetSizeDelegate<T>)Delegate.CreateDelegate(typeof(GetSizeDelegate<T>), getSize!);
        var serializeDelegate = (SerializeIntoDelegate<T>)Delegate.CreateDelegate(
            typeof(SerializeIntoDelegate<T>), serializeInto!);
        var deserializeDelegate = (DeserializeDelegate<T>)Delegate.CreateDelegate(
            typeof(DeserializeDelegate<T>), deserialize!);

        return new NsdPayloadSerializer<T>(getSizeDelegate, serializeDelegate, deserializeDelegate);
    }

    public int GetSize(in T value)
    {
        return _getSize(in value);
    }

    public void Serialize(in T value, ref byte[] buffer)
    {
        int size = GetSize(in value);
        Invariant.That(buffer.Length >= size, "The buffer is smaller than the payload.");
        _serializeInto(in value, buffer.AsSpan(0, size), 0);
    }

    public bool TryDeserialize(ReadOnlySpan<byte> data, out T value)
    {
        try
        {
            value = _deserialize(data);
            return true;
        }
        catch (InvalidDataException)
        {
            value = default;
            return false;
        }
    }
}
