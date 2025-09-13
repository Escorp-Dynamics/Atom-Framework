#pragma warning disable CA2213
#pragma warning disable IDE0290 // FIXME
#pragma warning disable IDE0052 // FIXME

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.IO.Compression;

/// <summary>
/// Внутренний декодер Zstd кадров/блоков (RAW/RLE + пропуск skippable), стриминговая модель.
/// </summary>
internal sealed class ZstdDecoder : IDisposable
{
    private readonly System.IO.Stream baseStream;

    private bool isDisposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdDecoder([NotNull] System.IO.Stream output) => baseStream = output;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> dst) => throw new NotImplementedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReadAsync(Memory<byte> dst, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        // TODO: Высвобождать ресурсы
    }
}