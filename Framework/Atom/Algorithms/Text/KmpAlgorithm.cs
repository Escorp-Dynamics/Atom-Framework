using Atom.Buffers;

namespace Atom.Algorithms.Text;

/// <summary>
/// Представляет методы работы для алгоритма Кнута-Морриса-Пратта (KMP).
/// </summary>
public class KmpAlgorithm : TextAlgorithm
{
    /// <summary>
    /// Вычисляет массив длинных совпадающих префиксов и суффиксов (LPS) для подстроки.
    /// Этот массив помогает избежать ненужных сравнений при поиске подстроки.
    /// </summary>
    /// <param name="target">Входная подстрока.</param>
    /// <param name="comparison">Параметр сравнения строк.</param>
    /// <returns>Массив длинных совпадающих префиксов и суффиксов (LPS) для подстроки.</returns>
    protected virtual unsafe ReadOnlySpan<int> BuildPrefixFunction(ReadOnlySpan<char> target, StringComparison comparison)
    {
        var prefixFunction = SpanPool<int>.Shared.Rent(target.Length);

        var length = 0;
        var i = 1;
        prefixFunction[0] = 0;

        fixed (char* targetPtr = target)
        {
            while (i < target.Length)
            {
                if ((*(targetPtr + i)).Equals(*(targetPtr + length), comparison))
                {
                    ++length;
                    prefixFunction[i] = length;
                    ++i;
                    continue;
                }

                if (length is not 0)
                {
                    length = prefixFunction[length - 1];
                    continue;
                }

                prefixFunction[i] = 0;
                ++i;
            }
        }

        return prefixFunction;
    }

    /// <summary>
    /// Вычисляет массив длинных совпадающих префиксов и суффиксов (LPS) для подстроки.
    /// Этот массив помогает избежать ненужных сравнений при поиске подстроки.
    /// </summary>
    /// <param name="target">Входная подстрока.</param>
    /// <returns>Массив длинных совпадающих префиксов и суффиксов (LPS) для подстроки.</returns>
    protected ReadOnlySpan<int> BuildPrefixFunction(ReadOnlySpan<char> target) => BuildPrefixFunction(target, default);

    /// <summary>
    /// Вычисляет массив длинных совпадающих префиксов и суффиксов (LPS) для подстроки.
    /// Этот массив помогает избежать ненужных сравнений при поиске подстроки.
    /// </summary>
    /// <param name="target">Входная подстрока.</param>
    /// <param name="comparison">Параметр сравнения строк.</param>
    /// <returns>Массив длинных совпадающих префиксов и суффиксов (LPS) для подстроки.</returns>
    protected ReadOnlySpan<int> BuildPrefixFunction(string target, StringComparison comparison) => BuildPrefixFunction(target.AsSpan(), comparison);

    /// <summary>
    /// Вычисляет массив длинных совпадающих префиксов и суффиксов (LPS) для подстроки.
    /// Этот массив помогает избежать ненужных сравнений при поиске подстроки.
    /// </summary>
    /// <param name="target">Входная подстрока.</param>
    /// <returns>Массив длинных совпадающих префиксов и суффиксов (LPS) для подстроки.</returns>
    protected ReadOnlySpan<int> BuildPrefixFunction(string target) => BuildPrefixFunction(target, default);

    /// <inheritdoc/>
    public override unsafe int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var prefixFunction = BuildPrefixFunction(target, comparison);
        var count = 0;
        var sourceIndex = 0;
        var targetIndex = 0;

        fixed (char* sourcePtr = source)
        fixed (char* targetPtr = target)
        {
            while (sourceIndex < source.Length)
            {
                if ((*(sourcePtr + sourceIndex)).Equals(*(targetPtr + targetIndex), comparison))
                {
                    ++sourceIndex;
                    ++targetIndex;
                }

                if (targetIndex == target.Length)
                {
                    ++count;
                    targetIndex = prefixFunction[targetIndex - 1];
                    continue;
                }

                if (sourceIndex < source.Length && !(*(sourcePtr + sourceIndex)).Equals(*(targetPtr + targetIndex), comparison))
                {
                    if (targetIndex is not 0)
                    {
                        targetIndex = prefixFunction[targetIndex - 1];
                        continue;
                    }

                    ++sourceIndex;
                }
            }
        }

        SpanPool<int>.Shared.Return(prefixFunction);
        return count;
    }

    /// <inheritdoc/>
    public override unsafe bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var prefixFunction = BuildPrefixFunction(target, comparison);
        var isFound = false;
        var sourceIndex = 0;
        var targetIndex = 0;

        fixed (char* sourcePtr = source)
        fixed (char* targetPtr = target)
        {
            while (sourceIndex < source.Length)
            {
                if ((*(sourcePtr + sourceIndex)).Equals(*(targetPtr + targetIndex), comparison))
                {
                    ++sourceIndex;
                    ++targetIndex;
                }

                if (targetIndex == target.Length)
                {
                    isFound = true;
                    break;
                }

                if (sourceIndex < source.Length && !(*(sourcePtr + sourceIndex)).Equals(*(targetPtr + targetIndex), comparison))
                {
                    if (targetIndex is not 0)
                    {
                        targetIndex = prefixFunction[targetIndex - 1];
                        continue;
                    }

                    ++sourceIndex;
                }
            }
        }

        SpanPool<int>.Shared.Return(prefixFunction);
        return isFound;
    }
}