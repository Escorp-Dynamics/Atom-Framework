using System.Runtime.CompilerServices;

namespace Atom.Algorithms.Text;

/// <summary>
/// Представляет методы работы для алгоритма Рабина-Карпа.
/// </summary>
public class RabinKarpAlgorithm : TextAlgorithm
{
    private const int Prime = 101;

    /// <inheritdoc/>
    public override unsafe int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var count = 0;
        var targetHash = 0;
        var sourceHash = 0;
        var power = 1;

        for (var i = 0; i < target.Length; ++i)
        {
            targetHash = (targetHash * 256 + Extensions.GetUpperChar(target[i], comparison)) % Prime;
            sourceHash = (sourceHash * 256 + Extensions.GetUpperChar(source[i], comparison)) % Prime;
            if (i > 0) power = power * 256 % Prime;
        }

        fixed (char* sourcePtr = source)
        fixed (char* targetPtr = target)
        {
            for (var i = 0; i <= source.Length - target.Length; ++i)
            {
                if (sourceHash == targetHash && CompareStrings(sourcePtr + i, targetPtr, target.Length, comparison)) ++count;

                if (i < source.Length - target.Length)
                {
                    sourceHash = ((sourceHash - Extensions.GetUpperChar(source[i], comparison) * power) * 256 + Extensions.GetUpperChar(source[i + target.Length], comparison)) % Prime;
                    if (sourceHash < 0) sourceHash += Prime;
                }
            }
        }

        return count;
    }

    /// <inheritdoc/>
    public override unsafe bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        if (target.Length is 0 || source.Length < target.Length) return default;

        var isFound = false;
        var targetHash = 0;
        var sourceHash = 0;
        var power = 1;

        for (var i = 0; i < target.Length; ++i)
        {
            targetHash = (targetHash * 256 + Extensions.GetUpperChar(target[i], comparison)) % Prime;
            sourceHash = (sourceHash * 256 + Extensions.GetUpperChar(source[i], comparison)) % Prime;
            if (i > 0) power = power * 256 % Prime;
        }

        fixed (char* sourcePtr = source)
        fixed (char* targetPtr = target)
        {
            for (var i = 0; i <= source.Length - target.Length; ++i)
            {
                if (sourceHash == targetHash && CompareStrings(sourcePtr + i, targetPtr, target.Length, comparison))
                {
                    isFound = true;
                    break;
                }

                if (i < source.Length - target.Length)
                {
                    sourceHash = ((sourceHash - Extensions.GetUpperChar(source[i], comparison) * power) * 256 + Extensions.GetUpperChar(source[i + target.Length], comparison)) % Prime;
                    if (sourceHash < 0) sourceHash += Prime;
                }
            }
        }

        return isFound;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool CompareStrings(char* sourcePtr, char* targetPtr, int length, StringComparison comparison)
    {
        for (var i = 0; i < length; ++i)
            if (!(*(sourcePtr + i)).Equals(*(targetPtr + i), comparison))
                return default;
        
        return true;
    }
}