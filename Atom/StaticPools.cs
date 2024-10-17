using Atom.Buffers;

namespace Atom;

internal static class StaticPools
{
    private static readonly Lazy<SpanPool<int>> sparseSpanIndexPool = new(() => SpanPool<int>.Create(1024), true);

    public static SpanPool<int> SparseSpanIndexPool => sparseSpanIndexPool.Value;
}