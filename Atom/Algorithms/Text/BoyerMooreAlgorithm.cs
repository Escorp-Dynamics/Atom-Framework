using Atom.Buffers;

namespace Atom.Algorithms.Text;

/// <summary>
/// Представляет методы работы для алгоритма Бойера-Мура.
/// </summary>
public class BoyerMooreAlgorithm : TextAlgorithm
{
    /// <summary>
    /// Строит таблицу плохих символов для алгоритма Бойера-Мура.
    /// Таблица используется для определения, насколько нужно сдвинуть подстроку при несовпадении символов.
    /// </summary>
    /// <param name="target">Вхождение подстроки.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns>Таблица плохих символов для алгоритма Бойера-Мура.</returns>
    protected virtual unsafe ReadOnlySpan<int> BuildBadCharShift(ReadOnlySpan<char> target, StringComparison comparison)
    {
        var badCharShift = SpanPool<int>.Shared.Rent(ushort.MaxValue + 1);
        badCharShift.Fill(-1);

        fixed (char* targetPtr = target)
            for (var i = 0; i < target.Length; ++i)
            {
                badCharShift[Extensions.GetUpperChar(targetPtr[i], comparison)] = i;
                badCharShift[Extensions.GetLowerChar(targetPtr[i], comparison)] = i;
            }

        return badCharShift;
    }

    /// <summary>
    /// Строит таблицу плохих символов для алгоритма Бойера-Мура.
    /// Таблица используется для определения, насколько нужно сдвинуть подстроку при несовпадении символов.
    /// </summary>
    /// <param name="target">Вхождение подстроки.</param>
    /// <returns>Таблица плохих символов для алгоритма Бойера-Мура.</returns>
    protected ReadOnlySpan<int> BuildBadCharShift(ReadOnlySpan<char> target) => BuildBadCharShift(target, default);

    /// <summary>
    /// Строит таблицу плохих символов для алгоритма Бойера-Мура.
    /// Таблица используется для определения, насколько нужно сдвинуть подстроку при несовпадении символов.
    /// </summary>
    /// <param name="target">Вхождение подстроки.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns>Таблица плохих символов для алгоритма Бойера-Мура.</returns>
    protected ReadOnlySpan<int> BuildBadCharShift(string target, StringComparison comparison) => BuildBadCharShift(target.AsSpan(), comparison);

    /// <summary>
    /// Строит таблицу плохих символов для алгоритма Бойера-Мура.
    /// Таблица используется для определения, насколько нужно сдвинуть подстроку при несовпадении символов.
    /// </summary>
    /// <param name="target">Вхождение подстроки.</param>
    /// <returns>Таблица плохих символов для алгоритма Бойера-Мура.</returns>
    protected ReadOnlySpan<int> BuildBadCharShift(string target) => BuildBadCharShift(target, default);

    /// <inheritdoc/>
    public override unsafe int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var badCharShift = BuildBadCharShift(target, comparison);
        var count = 0;
        var shift = 0;

        fixed (char* sourcePtr = source)
        fixed (char* targetPtr = target)
        {
            while (shift <= source.Length - target.Length)
            {
                var j = target.Length - 1;
                while (j >= 0 && (*(sourcePtr + shift + j)).Equals(*(targetPtr + j), comparison)) --j;

                if (j < 0)
                {
                    ++count;
                    shift += (shift + target.Length < source.Length) ? target.Length - badCharShift[sourcePtr[shift + target.Length]] : 1;
                    continue;
                }

                shift += Math.Max(1, j - badCharShift[sourcePtr[shift + j]]);
            }
        }

        SpanPool<int>.Shared.Return(badCharShift);
        return count;
    }

    /// <inheritdoc/>
    public override unsafe bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var badCharShift = BuildBadCharShift(target, comparison);
        var isFound = false;
        var shift = 0;

        fixed (char* sourcePtr = source)
        fixed (char* targetPtr = target)
        {
            while (shift <= source.Length - target.Length)
            {
                var j = target.Length - 1;
                while (j >= 0 && (*(sourcePtr + shift + j)).Equals(*(targetPtr + j), comparison)) --j;

                if (j < 0)
                {
                    isFound = true;
                    break;
                }

                shift += Math.Max(1, j - badCharShift[sourcePtr[shift + j]]);
            }
        }

        SpanPool<int>.Shared.Return(badCharShift);
        return isFound;
    }
}