namespace NsdRpc;

// One serializer per payload type, built once on first use. No dictionary
// lookups on the send path.
internal static class PayloadSerializerCache<T>
    where T : struct
{
    public static readonly IPayloadSerializer<T> Value = NsdPayloadSerializer<T>.Build();
}
