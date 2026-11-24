namespace NsdRpc.Tcp;

// Validates the cross-key constraints on TcpOptions.
internal static class TcpOptionValidation
{
    public static void Validate(TcpOptions options)
    {
        if (options.BindAddress == null)
        {
            throw new ArgumentNullException(nameof(options), "TcpOptions.BindAddress must not be null.");
        }

        if (options.ReceiveQueueBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "ReceiveQueueBytes must be positive.");
        }

        if (options.SendBacklogDisconnectBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "SendBacklogDisconnectBytes must not be negative.");
        }

        if (options.SendBacklogDisconnectBytes > options.SendQueueBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "SendBacklogDisconnectBytes must not exceed SendQueueBytes.");
        }

        if (options.SendBacklogDisconnectMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "SendBacklogDisconnectMs must not be negative.");
        }

        if (options.SendBatchInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "SendBatchInterval must not be negative.");
        }

        if (options.SendBatchMaxBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "SendBatchMaxBytes must be positive.");
        }

        if ((options.SendBacklogDisconnectBytes == 0) != (options.SendBacklogDisconnectMs == 0))
        {
            throw new ArgumentException(
                "SendBacklogDisconnectBytes and SendBacklogDisconnectMs must both be zero or both be positive.",
                nameof(options));
        }
    }
}
