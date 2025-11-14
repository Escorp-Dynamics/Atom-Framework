#pragma warning disable CA1812

using Atom.Buffers;
using Atom.Collections;

namespace Atom.Algorithms.Text;

/// <summary>
/// Представляет методы работы для алгоритма Ахо-Корасик.
/// </summary>
public class AhoCorasickAlgorithm : TextAlgorithm
{
    private sealed class TrieNode
    {
        public SparseArray<TrieNode> Children = new(ushort.MaxValue + 1);
        public TrieNode? Fail;
        public SparseArray<int>? Output;
        public int OutputCount;
        public SparseArray<int>? OutputRef;

        public void Release() => Release(this);

        public static void Release(TrieNode node)
        {
            foreach (var child in node.Children) Release(child);

            ObjectPool<TrieNode>.Shared.Return(node, x =>
            {
                if (x.OutputRef is null) x.Output?.Release(clearArray: true);
                x.Children.Release(clearArray: true);

                x.Fail = default;
                x.OutputCount = default;
                x.OutputRef = default;
                x.Output = default;
            });
        }
    }

    /// <inheritdoc/>
    public override unsafe int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var root = BuildFailLinks(target, comparison);
        var count = 0;
        var current = root;

        fixed (char* sourcePtr = source)
        {
            for (var i = 0; i < source.Length; ++i)
            {
                var c = Extensions.GetUpperChar(sourcePtr[i], comparison);
                while (current is not null && current.Children[c] is null) current = current.Fail;

                if (current is null)
                {
                    current = root;
                    continue;
                }

                current = current.Children[c];
                if (current.OutputCount > 0) count += current.OutputCount;
            }
        }

        root.Release();
        return count;
    }

    /// <inheritdoc/>
    public override unsafe bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var root = BuildFailLinks(target, comparison);
        var current = root;
        var isFound = false;

        fixed (char* sourcePtr = source)
        {
            for (var i = 0; i < source.Length; ++i)
            {
                var c = Extensions.GetUpperChar(sourcePtr[i], comparison);
                while (current is not null && current.Children[c] is null) current = current.Fail;

                if (current is null)
                {
                    current = root;
                    continue;
                }

                current = current.Children[c];

                if (current.OutputCount > 0)
                {
                    isFound = true;
                    break;
                }
            }
        }

        root.Release();
        return isFound;
    }

    private static TrieNode GetNode()
    {
        var node = ObjectPool<TrieNode>.Shared.Rent();
        node.Children = new(ushort.MaxValue + 1);
        node.Output = new(1024);
        return node;
    }

    private static TrieNode AddPattern(ReadOnlySpan<char> pattern, StringComparison comparison)
    {
        var root = GetNode();
        var current = root;

        foreach (var c in pattern)
        {
            var charValue = Extensions.GetUpperChar(c, comparison);
            if (current.Children[charValue] is null) current.Children[charValue] = GetNode();
            current = current.Children[charValue];
        }

        if (current.OutputCount < current.Output!.Length) current.Output[current.OutputCount++] = pattern.Length;
        return root;
    }

    private static TrieNode BuildFailLinks(ReadOnlySpan<char> pattern, StringComparison comparison)
    {
        var root = AddPattern(pattern, comparison);
        var queue = new Queue<TrieNode>();
        var indexes = root.Children.GetIndexes();

        for (var i = 0; i < indexes.Length; ++i)
        {
            var idx = indexes[i];
            queue.Enqueue(root.Children[idx]);
            root.Children[idx].Fail = root;
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            indexes = current.Children.GetIndexes();

            for (var i = 0; i < indexes.Length; ++i)
            {
                var idx = indexes[i];
                var child = current.Children[idx];

                queue.Enqueue(child);
                var fail = current.Fail;

                while (fail is not null && fail.Children[idx] is null) fail = fail.Fail;

                child.Fail = fail?.Children[idx] ?? root;

                if (fail is not null)
                {
                    child.OutputRef = fail.OutputRef ?? fail.Output;
                    child.OutputCount = fail.OutputCount;
                }
            }
        }

        return root;
    }
}