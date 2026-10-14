using System.Diagnostics.CodeAnalysis;

namespace NsdRpc;

// The always-on assert helper for internal invariants. It throws in every
// build. Debug-only asserts do not qualify. See docs/specs/coding-style.md.
internal static class Invariant
{
    public static void That([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Invariant failed: " + message);
        }
    }

    public static void NotNull([NotNull] object? value, string message)
    {
        That(value != null, message);
    }

    public static void Fail(string message)
    {
        throw new InvalidOperationException("Invariant failed: " + message);
    }
}
