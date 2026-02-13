using System.Buffers;

namespace Atom.Algorithms.Text;

/// <summary>
/// Представляет методы работы для Z-алгоритма.
/// </summary>
public class ZAlgorithm : TextAlgorithm
{
    /// <summary>
    /// Создает Z-массив для объединенной строки, которая состоит из искомой подстроки, разделителя и исходной строки.
    /// </summary>
    /// <param name="combined">Объединённая строка.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns>Z-массив для объединенной строки.</returns>
    protected virtual unsafe int[] BuildZArray(ReadOnlySpan<char> combined, StringComparison comparison)
    {
        var zArray = ArrayPool<int>.Shared.Rent(combined.Length);

        var n = combined.Length;
        var l = 0;
        var r = 0;

        fixed (char* combinedPtr = combined)
        {
            for (var i = 1; i < n; ++i)
            {
                if (i > r)
                {
                    l = r = i;
                    while (r < n && (*(combinedPtr + r)).Equals(*(combinedPtr + r - l), comparison)) ++r;
                    zArray[i] = r - l;
                    --r;
                    continue;
                }

                var k = i - l;

                if (zArray[k] < r - i + 1)
                {
                    zArray[i] = zArray[k];
                    continue;
                }

                l = i;
                while (r < n && (*(combinedPtr + r)).Equals(*(combinedPtr + r - l), comparison)) ++r;
                zArray[i] = r - l;
                --r;
            }
        }

        return zArray;
    }

    /// <summary>
    /// Создает Z-массив для объединенной строки, которая состоит из искомой подстроки, разделителя и исходной строки.
    /// </summary>
    /// <param name="combined">Объединённая строка.</param>
    /// <returns>Z-массив для объединенной строки.</returns>
    protected ReadOnlySpan<int> BuildZArray(ReadOnlySpan<char> combined) => BuildZArray(combined, default);

    /// <summary>
    /// Создает Z-массив для объединенной строки, которая состоит из искомой подстроки, разделителя и исходной строки.
    /// </summary>
    /// <param name="combined">Объединённая строка.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    protected ReadOnlySpan<int> BuildZArray(string combined, StringComparison comparison) => BuildZArray(combined.AsSpan(), comparison);

    /// <summary>
    /// Создает Z-массив для объединенной строки, которая состоит из искомой подстроки, разделителя и исходной строки.
    /// </summary>
    /// <param name="combined">Объединённая строка.</param>
    /// <returns>Z-массив для объединенной строки.</returns>
    protected ReadOnlySpan<int> BuildZArray(string combined) => BuildZArray(combined, default);

    /// <inheritdoc/>
    public override int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var combinedLength = source.Length + target.Length + 1;
        var tmp = ArrayPool<char>.Shared.Rent(combinedLength);
        var combined = tmp.AsSpan(0, combinedLength);

        target.CopyTo(combined);
        combined[target.Length] = char.MinValue;
        source.CopyTo(combined[(target.Length + 1)..]);

        var zArray = BuildZArray(combined, comparison);
        var count = 0;

        for (var i = target.Length + 1; i < combinedLength; ++i)
        {
            if (zArray[i] == target.Length) ++count;
        }

        ArrayPool<int>.Shared.Return(zArray);
        ArrayPool<char>.Shared.Return(tmp);

        return count;
    }

    /// <inheritdoc/>
    public override bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var combinedLength = source.Length + target.Length + 1;
        var tmp = ArrayPool<char>.Shared.Rent(combinedLength);
        var combined = tmp.AsSpan(0, combinedLength);

        target.CopyTo(combined);
        combined[target.Length] = char.MinValue;
        source.CopyTo(combined[(target.Length + 1)..]);

        var zArray = BuildZArray(combined, comparison);
        var isFound = false;

        for (var i = target.Length + 1; i < combinedLength; ++i)
        {
            if (zArray[i] == target.Length)
            {
                isFound = true;
                break;
            }
        }

        ArrayPool<int>.Shared.Return(zArray);
        ArrayPool<char>.Shared.Return(tmp);

        return isFound;
    }
}