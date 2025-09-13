#pragma warning disable CA2000, CA2215

using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;
using Atom.IO.Compression;

namespace Atom.Net.Https;

/// <summary>
/// Представляет контент HTTPS.
/// </summary>
public sealed class HttpsContent : HttpContent
{
    private readonly HttpContent content;
    private readonly string[] encodings;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsContent"/>.
    /// </summary>
    /// <param name="content">Исходный контент.</param>
    /// <param name="encodings">Кодировки, использованные в запросе (deflate, br, gzip, zstd).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HttpsContent(HttpContent content, string[] encodings)
    {
        this.content = content;
        this.encodings = encodings;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
    {
        var raw = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (encodings is null || encodings.Length is 0) return raw;

        var s = raw;

        for (var idx = encodings.Length - 1; idx >= 0; idx--)
        {
            var t = encodings[idx];
            if (!string.IsNullOrWhiteSpace(t)) s = WrapDecoder(s, t.AsSpan(), leaveOpen: idx is not 0);
        }

        return s;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override Stream CreateContentReadStream(CancellationToken cancellationToken) => CreateContentReadStreamAsync(cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) => SerializeToStreamAsync(stream, context, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => content.CopyToAsync(stream, context, cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override bool TryComputeLength(out long length)
    {
        var h = content.Headers.ContentLength;

        if (h.HasValue)
        {
            length = h.Value;
            return true;
        }

        length = 0;
        return default;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        if (disposing) content.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Stream WrapDecoder(Stream current, ReadOnlySpan<char> token, bool leaveOpen)
    {
        token = token.Trim();
        if (token.Length is 0) return current;

        // identity — как есть
        if (token.Equals("identity".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return current;

        // gzip (и исторический x-gzip)
        if (token.Equals("gzip".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            token.Equals("x-gzip".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return new GZipStream(current, CompressionMode.Decompress, leaveOpen);
        }

        // deflate — браузерная трактовка = zlib-обёртка
        if (token.Equals("deflate".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new DeflateStream(current, CompressionMode.Decompress, leaveOpen);

        // brotli
        if (token.Equals("br".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return new BrotliStream(current, CompressionMode.Decompress, leaveOpen);

        // zstd (RFC 8878)
        if (token.Equals("zstd".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            token.Equals("zst".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return new ZstdStream(current, CompressionMode.Decompress, leaveOpen);
        }

        // Неизвестные кодеки — игнорируем
        return current;
    }
}