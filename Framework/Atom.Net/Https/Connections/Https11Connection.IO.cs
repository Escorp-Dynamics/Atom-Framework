using System.Buffers;
using System.Text;
using Atom.Net.Tls;

namespace Atom.Net.Https.Connections;

internal sealed partial class Https11Connection
{
    /// <summary>
    /// Наибольший размер, который выделяется под тело СРАЗУ, по заявленной длине.
    /// </summary>
    /// <remarks>
    /// ★ Раньше массив выделялся ровно по <c>Content-Length</c>, каким бы тот ни был. Заявленной
    /// длине верить нельзя: она приходит от чужой стороны, и одного узла из тысячи с
    /// <c>Content-Length: 1500000000</c> — зеркала, сломанного CDN или намеренной ловушки —
    /// хватало, чтобы отобрать полтора гигабайта ДО получения хотя бы одного байта тела. При
    /// нескольких таких запросах разом процесс падал целиком, унося все остальные запросы.
    ///
    /// Теперь большие тела дочитываются по мере поступления: врущий сервер не получает от нас
    /// памяти вперёд, а честный отдаёт данные и буфер дорастает естественным образом.
    /// </remarks>
    private const int MaxEagerBodyAllocation = 4 * 1024 * 1024;

    private async ValueTask<byte[]> ReadFixedLengthBodyAsync(long contentLength, CancellationToken cancellationToken)
    {
        if (contentLength is 0) return [];
        if (contentLength > int.MaxValue) throw new NotSupportedException("Минимальный H1 slice пока не поддерживает body > 2GB.");

        EnsureBodyWithinLimit(contentLength);

        if (contentLength <= MaxEagerBodyAllocation)
        {
            var buffer = new byte[(int)contentLength];
            await ReadExactAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer;
        }

        var writer = new ArrayBufferWriter<byte>(MaxEagerBodyAllocation);
        var remaining = contentLength;

        while (remaining > 0)
        {
            var take = (int)Math.Min(remaining, 65536);
            var slice = writer.GetMemory(take)[..take];

            await ReadExactAsync(slice, cancellationToken).ConfigureAwait(false);

            writer.Advance(take);
            remaining -= take;
        }

        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Проверяет, укладывается ли тело в заданный предел.
    /// </summary>
    /// <param name="length">Длина тела: заявленная или уже накопленная.</param>
    private void EnsureBodyWithinLimit(long length)
    {
        var limit = options.MaxResponseContentBytes;

        if (limit > 0 && length > limit)
            throw new InvalidOperationException($"Размер тела ответа ({length} Б) превысил предел {limit} Б.");
    }

    private async ValueTask<byte[]> ReadChunkedBodyAsync(CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();

        try
        {
            return await ReadChunkedBodyCoreAsync(body, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (body.Length > 0)
        {
            // Срок вышел на середине потока кусков: отдаём то, что успели получить, — см.
            // пояснение в ReadToEndBodyAsync.
            return body.ToArray();
        }
    }

    private async ValueTask<byte[]> ReadChunkedBodyCoreAsync(MemoryStream body, CancellationToken cancellationToken)
    {
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
                var read = await ReadTransportAsync(current, buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read is 0) break;

                TrackReceived(read);
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (body.Length > 0)
        {
            // ★ Срок вышел, но данные УЖЕ есть. Тело здесь ограничено закрытием соединения, а
            // значит его «правильная» длина никому не известна — и узлы, которые соединение не
            // закрывают вовсе, встречаются в сети регулярно. Выбросить полученное значило бы
            // потерять весь ответ там, где браузер давно показал бы страницу.
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

                // ★ Latin1, а не ASCII: она сохраняет байты 1:1. Разбор в ASCII превращал любой
                // байт старше 0x7F в «?», и заголовок с сырым UTF-8 — например, Location с
                // непроцентированным путём — приходил необратимо испорченным, а автоматическое
                // перенаправление уходило по адресу из вопросительных знаков.
                return Encoding.Latin1.GetString(line.GetBuffer(), 0, (int)line.Length);
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
        receiveCount = await ReadTransportAsync(current, receiveBuffer.AsMemory(0, receiveBuffer.Length), cancellationToken).ConfigureAwait(false);

        if (receiveCount > 0)
            TrackReceived(receiveCount);

        return receiveCount;
    }

    /// <summary>
    /// Читает из транспорта, считая закрытие партнёром концом потока, а не отказом.
    /// </summary>
    /// <param name="transport">Транспорт соединения.</param>
    /// <param name="buffer">Приёмник.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число прочитанных байт; ноль означает конец потока.</returns>
    /// <remarks>
    /// ★ Ответ, тело которого ограничено ЗАКРЫТИЕМ соединения, — законная и распространённая
    /// форма HTTP/1.1: ни <c>Content-Length</c>, ни кусочной передачи в нём нет, и конец тела
    /// объявляется разрывом. Так отвечают, например, корневые адреса craigslist.org и cisco.com —
    /// «302 Found» с одним лишь заголовком Location.
    ///
    /// Читатель тела это учитывает и завершается по нулю (см. <see cref="ReadToEndBodyAsync"/>),
    /// но ноля он не получал: поток TLS 1.2 превращал обычный FIN в исключение, и весь ответ
    /// пропадал вместе с ним. Отказ выглядел сетевым сбоем и потому уводил поиск в приветствие —
    /// хотя рукопожатие к тому моменту давно состоялось, запрос ушёл и ответ пришёл целиком.
    /// Поток TLS 1.3 то же самое обрабатывает верно, отчего беда казалась избирательной по узлам:
    /// проявлялась ровно там, где сервер остался на TLS 1.2.
    ///
    /// Настоящее сокращение ответа этим не маскируется: чтение по объявленной длине проверяет
    /// полноту само и на нуле сообщает о преждевременном закрытии.
    /// </remarks>
    private static async ValueTask<int> ReadTransportAsync(Atom.IO.Stream transport, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await transport.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (TlsConnectionClosedException)
        {
            return 0;
        }
    }
}