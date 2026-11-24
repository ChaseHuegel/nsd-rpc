using Xunit;

// The RpcLog trace switch and its listener list are process-global. Some tests
// change them. Run every test collection in sequence so a change cannot race
// an allocation test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
