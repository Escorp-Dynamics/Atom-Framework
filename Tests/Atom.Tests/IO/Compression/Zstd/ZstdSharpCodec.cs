using System.Buffers;
using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Tests;

internal sealed class ZstdSharpCodec : ICodec
{
    public string Name => "ZstdSharp";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] Compress(ReadOnlySpan<byte> src, int level)
    {
        using var msOut = new MemoryStream(capacity: Math.Max(64, src.Length / 2));
        using (var zs = new ZstdSharp.ZstdStream(msOut, level, leaveOpen: true))
        {
            // Пишем исходник малыми порциями, чтобы проверить стриминг.
            var offset = 0;
            const int chunk = 8192;
            var tmp = ArrayPool<byte>.Shared.Rent(chunk);

            try
            {
                while (offset < src.Length)
                {
                    var n = Math.Min(chunk, src.Length - offset);
                    src.Slice(offset, n).CopyTo(tmp);
                    zs.Write(tmp, 0, n);
                    offset += n;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(tmp);
            }

            zs.Flush();
        }

        return msOut.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        using var msIn = new MemoryStream(compressed.ToArray());
        using var zs = new ZstdSharp.ZstdStream(msIn, ZstdSharp.ZstdStreamMode.Decompress, leaveOpen: true);
        using var msOut = new MemoryStream(Math.Max(64, compressed.Length * 3));

        var buf = ArrayPool<byte>.Shared.Rent(8192);

        try
        {
            int read;

            while ((read = zs.Read(buf, 0, buf.Length)) > 0)
                msOut.Write(buf, 0, read);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }

        return msOut.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CompressStream(System.IO.Stream src, System.IO.Stream dst, int level, int ioChunk)
    {
        using var zs = new ZstdSharp.ZstdStream(dst, level, leaveOpen: true);
        var buf = ArrayPool<byte>.Shared.Rent(ioChunk);

        try
        {
            int read;

            while ((read = src.Read(buf, 0, buf.Length)) > 0) zs.Write(buf, 0, read);

            zs.Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DecompressStream(System.IO.Stream src, System.IO.Stream dst, int ioChunk)
    {
        using var zs = new ZstdSharp.ZstdStream(src, ZstdSharp.ZstdStreamMode.Decompress, leaveOpen: true);
        var buf = ArrayPool<byte>.Shared.Rent(ioChunk);

        try
        {
            int read;

            while ((read = zs.Read(buf, 0, buf.Length)) > 0)
                dst.Write(buf, 0, read);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}