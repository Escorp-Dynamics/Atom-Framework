using System.Buffers;
using System.Runtime.CompilerServices;
using ZstdNet;

namespace Atom.IO.Compression.Tests;

internal sealed class ZstdNetCodec : ICodec
{
    public string Name => "ZstdNet";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] Compress(ReadOnlySpan<byte> src, int level)
    {
        using var c = new Compressor(new CompressionOptions(level));
        return c.Wrap(src.ToArray());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        using var d = new Decompressor();
        return d.Unwrap(compressed.ToArray());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CompressStream(System.IO.Stream src, System.IO.Stream dst, int level, int ioChunk)
    {
        // Аналогично ZstdSharp: буферизуем
        using var ms = new MemoryStream();
        var buf = ArrayPool<byte>.Shared.Rent(ioChunk);

        try
        {
            int read;

            while ((read = src.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, read);

            using var c = new Compressor(new CompressionOptions(level));
            var compressed = c.Wrap(ms.ToArray());
            dst.Write(compressed, 0, compressed.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DecompressStream(System.IO.Stream src, System.IO.Stream dst, int ioChunk)
    {
        using var d = new Decompressor();
        using var ms = new MemoryStream();

        src.CopyTo(ms);

        var plain = d.Unwrap(ms.ToArray());
        dst.Write(plain, 0, plain.Length);
    }
}