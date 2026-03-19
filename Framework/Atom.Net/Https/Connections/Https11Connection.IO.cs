using System.Buffers;
using System.Text;

namespace Atom.Net.Https.Connections;

internal sealed partial class Https11Connection
{
    private async ValueTask<byte[]> ReadFixedLengthBodyAsync(long contentLength, CancellationToken cancellationToken)
    {
        if (contentLength is 0) return [];
        if (contentLength > int.MaxValue) throw new NotSupportedException("Минимальный H1 slice пока не поддерживает body > 2GB.");

        var buffer = new byte[(int)contentLength];
        await ReadExactAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    private async ValueTask<byte[]> ReadChunkedBodyAsync(CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();

        while (true)
        {
            var sizeLine = await ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Сервер закрыл соединение внутри chunked body.");

            var separator = sizeLine.AsSpan().IndexOfAny(tokenSeparators);
            var sizeToken = separator >= 0 ? sizeLine[..separator] : sizeLine;

            if (!int.TryParse(sizeToken, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var chunkSize) || chunkSize < 0)
                throw new InvalidOperationException($"Некорректный chunk size: '{sizeLine}'.");

            if (chunkSize is 0)
            {
                while (true)
                {
                    var trailer = await ReadLineAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Сервер закрыл соединение внутри trailer section.");
                    if (trailer.Length is 0) break;
                }

                break;
            }

            var chunk = new byte[chunkSize];
            await ReadExactAsync(chunk, cancellationToken).ConfigureAwait(false);
            await body.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            await ExpectEmptyLineAsync(cancellationToken).ConfigureAwait(false);
        }

        return body.ToArray();
    }

    private async ValueTask<byte[]> ReadToEndBodyAsync(CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();

        if (receiveCount > 0)
        {
            await body.WriteAsync(receiveBuffer.AsMemory(receiveOffset, receiveCount), cancellationToken).ConfigureAwait(false);
            receiveOffset = 0;
            receiveCount = 0;
        }

        var current = transport ?? throw new InvalidOperationException("Соединение не открыто.");
        var buffer = ArrayPool<byte>.Shared.Rent(4096);

        try
        {
            while (true)
            {
                var read = await current.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read is 0) break;

                TrackReceived(read);
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        Volatile.Write(ref isConnected, 0);
        Volatile.Write(ref isDraining, 1);
        return body.ToArray();
    }

    private void ApplyConnectionDisposition(ResponseHeadersState state)
    {
        if (state.ConnectionClose || state.BodyKind is ResponseBodyKind.CloseDelimited)
        {
            Volatile.Write(ref isDraining, 1);
            return;
        }

        Volatile.Write(ref isDraining, 0);
    }

    private async ValueTask ExpectEmptyLineAsync(CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Сервер закрыл соединение внутри chunked framing.");

        if (line.Length is not 0)
            throw new InvalidOperationException("Некорректное завершение chunk framing.");
    }

    private async ValueTask ReadExactAsync(Memory<byte> target, CancellationToken cancellationToken)
    {
        var written = 0;

        while (written < target.Length)
        {
            if (receiveCount > 0)
            {
                var copy = Math.Min(receiveCount, target.Length - written);
                receiveBuffer.AsMemory(receiveOffset, copy).CopyTo(target[written..]);
                receiveOffset += copy;
                receiveCount -= copy;
                written += copy;
                continue;
            }

            var read = await ReadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            if (read is 0)
                throw new InvalidOperationException("Сервер закрыл соединение до получения полного body.");
        }
    }

    private async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();

        while (true)
        {
            var value = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (value < 0)
                return line.Length is 0 ? null : throw new InvalidOperationException("Сервер закрыл соединение внутри header line.");

            if (value == '\r')
            {
                var next = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
                if (next != '\n')
                    throw new InvalidOperationException("Некорректный CRLF в HTTP header block.");

                return Encoding.ASCII.GetString(line.GetBuffer(), 0, (int)line.Length);
            }

            line.WriteByte((byte)value);
        }
    }

    private async ValueTask<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        if (receiveCount is 0)
        {
            var read = await ReadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            if (read is 0) return -1;
        }

        var value = receiveBuffer[receiveOffset];
        receiveOffset++;
        receiveCount--;
        return value;
    }

    private async ValueTask<int> ReadIntoBufferAsync(CancellationToken cancellationToken)
    {
        var current = transport ?? throw new InvalidOperationException("Соединение не открыто.");
        receiveOffset = 0;
        receiveCount = await current.ReadAsync(receiveBuffer.AsMemory(0, receiveBuffer.Length), cancellationToken).ConfigureAwait(false);

        if (receiveCount > 0)
            TrackReceived(receiveCount);

        return receiveCount;
    }
}