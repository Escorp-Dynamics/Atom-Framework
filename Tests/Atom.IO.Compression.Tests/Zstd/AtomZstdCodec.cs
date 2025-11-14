using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Tests;

/// <summary>
/// Адаптер вашего кодека на базе ZstdStream.
/// Предполагаем API: new ZstdStream(Stream, CompressionMode, bool leaveOpen = true, bool checkChecksum = true)
/// и буферизованное чтение/запись.
/// </summary>
internal sealed class AtomZstdCodec : ICodec
{
    public string Name => "Atom.ZstdStream";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] Compress(ReadOnlySpan<byte> src, int level)
    {
        using var msOut = new MemoryStream(capacity: Math.Max(64, src.Length / 2));
        using (var zs = new ZstdStream(msOut, level, leaveOpen: true))
        {
            // Пишем исходник малыми порциями, чтобы проверить стриминг.
            var offset = 0;
            const int chunk = 8192;
            var tmp = ArrayPool<byte>.Shared.Rent(chunk);
            while (offset < src.Length)
            {
                var n = Math.Min(chunk, src.Length - offset);
                src.Slice(offset, n).CopyTo(tmp);
                zs.Write(tmp, 0, n);
                offset += n;
            }
            ArrayPool<byte>.Shared.Return(tmp);

            zs.Flush();
        }

        return msOut.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        using var msIn = new MemoryStream(compressed.ToArray());
        using var zs = new ZstdStream(msIn, CompressionMode.Decompress, leaveOpen: true);
        using var msOut = new MemoryStream(Math.Max(64, compressed.Length * 3));

        var buf = ArrayPool<byte>.Shared.Rent(8192);
        int read;
        while ((read = zs.Read(buf, 0, buf.Length)) > 0)
            msOut.Write(buf, 0, read);
        ArrayPool<byte>.Shared.Return(buf);

        return msOut.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CompressStream(System.IO.Stream src, System.IO.Stream dst, int level, int ioChunk)
    {
        using var zs = new ZstdStream(dst, level, leaveOpen: true);
        var buf = ArrayPool<byte>.Shared.Rent(ioChunk);
        int read;
        while ((read = src.Read(buf, 0, buf.Length)) > 0)
            zs.Write(buf, 0, read);
        zs.Flush();
        ArrayPool<byte>.Shared.Return(buf);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DecompressStream(System.IO.Stream src, System.IO.Stream dst, int ioChunk)
    {
        using var zs = new ZstdStream(src, CompressionMode.Decompress, leaveOpen: true);
        var buf = ArrayPool<byte>.Shared.Rent(ioChunk);
        int read;
        while ((read = zs.Read(buf, 0, buf.Length)) > 0)
            dst.Write(buf, 0, read);
        ArrayPool<byte>.Shared.Return(buf);
    }
}
