#pragma warning disable CS8618

using System.Runtime.CompilerServices;

namespace Atom.Algorithms.Text;

/// <summary>
/// Представляет базовую реализацию алгоритмов для работы с текстом.
/// </summary>
public abstract class TextAlgorithm : ITextAlgorithm
{
    /// <inheritdoc/>
    public static ITextAlgorithm Shared { get; set; }

    /// <inheritdoc/>
    public abstract int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison);

    /// <inheritdoc/>
    public int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target) => CountOf(source, target, default);

    /// <inheritdoc/>
    public int CountOf(string source, ReadOnlySpan<char> target, StringComparison comparison) => CountOf(source.AsSpan(), target, comparison);

    /// <inheritdoc/>
    public int CountOf(string source, ReadOnlySpan<char> target) => CountOf(source, target, default);

    /// <inheritdoc/>
    public int CountOf(ReadOnlySpan<char> source, string target, StringComparison comparison) => CountOf(source, target.AsSpan(), comparison);

    /// <inheritdoc/>
    public int CountOf(ReadOnlySpan<char> source, string target) => CountOf(source, target, default);

    /// <inheritdoc/>
    public int CountOf(string source, string target, StringComparison comparison) => CountOf(source.AsSpan(), target.AsSpan(), comparison);

    /// <inheritdoc/>
    public int CountOf(string source, string target) => CountOf(source, target, default);

    /// <inheritdoc/>
    public abstract bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison);

    /// <inheritdoc/>
    public bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target) => Contains(source, target, default);

    /// <inheritdoc/>
    public bool Contains(string source, ReadOnlySpan<char> target, StringComparison comparison) => Contains(source.AsSpan(), target, comparison);

    /// <inheritdoc/>
    public bool Contains(string source, ReadOnlySpan<char> target) => Contains(source, target, default);

    /// <inheritdoc/>
    public bool Contains(ReadOnlySpan<char> source, string target, StringComparison comparison) => Contains(source, target.AsSpan(), comparison);

    /// <inheritdoc/>
    public bool Contains(ReadOnlySpan<char> source, string target) => Contains(source, target, default);

    /// <inheritdoc/>
    public bool Contains(string source, string target, StringComparison comparison) => Contains(source.AsSpan(), target.AsSpan(), comparison);

    /// <inheritdoc/>
    public bool Contains(string source, string target) => Contains(source, target, default);
}