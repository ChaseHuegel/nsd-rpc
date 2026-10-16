namespace NsdRpc;

// A transport that takes ownership of a pooled frame buffer. The peer rents
// the buffer from the shared pool, serializes into it, and hands it over. The
// transport sends it and returns it to the pool. A rejected send returns the
// buffer to the pool.
internal interface IOwnedFrameSink
{
    bool TrySendOwned(byte[] frame, int length);
}
