namespace NsdRpc;

/// <summary>
/// Serializes one payload type. The default implementation wraps the generated nsd methods.
/// </summary>
public interface IPayloadSerializer<T>
{
    int GetSize(in T value);

    void Serialize(in T value, ref byte[] buffer);

    bool TryDeserialize(ReadOnlySpan<byte> data, out T value);
}
