namespace NsdRpc;

/// <summary>
/// Serializes one payload type. The default implementation wraps the generated nsd methods.
/// </summary>
public interface IPayloadSerializer<T>
{
    int GetSize(in T value);

    void Serialize(in T value, ref byte[] buffer);

    /// <summary>Serializes into a buffer of the given size. The size comes from <see cref="GetSize"/>.</summary>
    void Serialize(in T value, ref byte[] buffer, int size);

    bool TryDeserialize(ReadOnlySpan<byte> data, out T value);
}
