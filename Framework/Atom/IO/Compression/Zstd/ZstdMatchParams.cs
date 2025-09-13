using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Параметры матчера, зависящие от CompressionLevel.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal readonly struct ZstdMatchParams(int windowLog, int hashLog, int searchDepth, int targetLength, int minMatch = 3)
{
    public readonly int WindowLog = windowLog;    // log2(windowSize)
    public readonly int HashLog = hashLog;      // размер хеш-таблицы = 1<<HashLog
    public readonly int SearchDepth = searchDepth;  // сколько кандидатов проверять
    public readonly int TargetLength = targetLength; // ранняя остановка, если нашли ML >= TargetLength
    public readonly int MinMatch = minMatch;     // минимальная длина матча (zstd=3)
}