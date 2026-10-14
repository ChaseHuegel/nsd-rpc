namespace NsdRpc;

/// <summary>
/// Receives one complete frame. The span is only valid during the call.
/// </summary>
public delegate void RpcFrameHandler(ReadOnlySpan<byte> frame);
