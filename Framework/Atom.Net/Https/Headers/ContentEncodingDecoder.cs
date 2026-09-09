using System.Buffers;
using System.IO.Compression;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Распаковка тела ответа по заголовку <c lang="text">content-encoding</c>.
/// </summary>
/// <remarks>
/// Без этого стек был бы неработоспособен, оставаясь при этом полностью правдоподобным на проводе.
/// Браузер объявляет <c lang="text">accept-encoding: gzip, deflate, br, zstd</c>, и убрать это объявление
/// нельзя — оно часть отпечатка. Но объявив, обязан и распаковывать: сервер, увидев такой
/// заголовок, отвечает сжатым телом. Не распаковав, вызывающая сторона получает двоичный мусор
/// вместо разметки, причём молча и только на тех серверах, которые сжимают. Так это и жило:
/// <c lang="text">www.cloudflare.com</c> отдавал <c lang="text">content-encoding: gzip</c> и тело, начинающееся с
/// <c lang="text">1F 8B</c>, а проверки на <c lang="text">tls.peet.ws</c> проходили лишь потому, что он не сжимает.
///
/// Кодировки применяются справа налево: заголовок перечисляет их в порядке ПРИМЕНЕНИЯ, значит
/// снимать надо с конца. Незнакомая кодировка означает, что дальше разбирать нечего — тело
/// возвращается как есть, потому что испорченный результат хуже нераспакованного.
/// </remarks>
public static class ContentEncodingDecoder
{
    /// <summary>
    /// Предел размера распакованного тела.
    /// </summary>
    /// <remarks>
    /// Сжатие даёт злоумышленнику рычаг: килобайты на проводе разворачиваются в гигабайты в
    /// памяти. Предел щедрый — законные ответы в него укладываются с запасом, — но он есть.
    /// </remarks>
    public const int MaximumDecodedLength = 256 * 1024 * 1024;

    /// <summary>
    /// Распаковывает тело ответа.
    /// </summary>
    /// <param name="body">Тело как получено с провода.</param>
    /// <param name="contentEncoding">Значение заголовка <c lang="text">content-encoding</c>.</param>
    /// <param name="decoded">Распакованное тело либо исходное, если распаковка не требуется.</param>
    /// <returns><see langword="true"/>, если тело действительно распаковано.</returns>
    public static bool TryDecode(byte[] body, string? contentEncoding, out byte[] decoded)
    {
        ArgumentNullException.ThrowIfNull(body);

        decoded = body;

        if (body.Length is 0 || string.IsNullOrWhiteSpace(contentEncoding)) return false;

        // Кодировок обычно одна; список нужен, только чтобы снять их в обратном порядке.
        var encodings = contentEncoding.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = body;
        var changed = false;

        for (var index = encodings.Length - 1; index >= 0; index--)
        {
            if (!TryDecodeSingle(current, encodings[index], out var step)) break;

            current = step;
            changed = true;
        }

        if (changed) decoded = current;

        return changed;
    }

    /// <summary>
    /// Определяет, знаем ли мы такую кодировку.
    /// </summary>
    /// <param name="encoding">Название кодировки.</param>
    /// <returns><see langword="true"/>, если распаковка возможна.</returns>
    public static bool IsSupported(string? encoding)
        => encoding is not null
        && (Matches(encoding, "gzip") || Matches(encoding, "x-gzip")
            || Matches(encoding, "deflate")
            || Matches(encoding, "br")
            || Matches(encoding, "zstd"));

    private static bool TryDecodeSingle(byte[] body, string encoding, out byte[] decoded)
    {
        decoded = body;

        // identity означает «не сжато» — это не ошибка и не повод останавливать разбор.
        if (Matches(encoding, "identity")) return true;

        try
        {
            if (Matches(encoding, "gzip") || Matches(encoding, "x-gzip"))
            {
                decoded = Inflate(body, static source => new GZipStream(source, CompressionMode.Decompress, leaveOpen: true));
                return true;
            }

            if (Matches(encoding, "br"))
            {
                decoded = Inflate(body, static source => new BrotliStream(source, CompressionMode.Decompress, leaveOpen: true));
                return true;
            }

            if (Matches(encoding, "zstd"))
            {
                decoded = Inflate(body, static source => new IO.Compression.ZstdStream(source, CompressionMode.Decompress, leaveOpen: true));
                return true;
            }

            if (Matches(encoding, "deflate"))
            {
                decoded = InflateDeflate(body);
                return true;
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            // ★ Тело объявлено сжатым и не разобралось — это ОТКАЗ, а не повод отдать сжатые
            // байты дальше. Прежде они возвращались как есть, вместе с заголовком
            // content-encoding: вызывающая сторона получала двоичный мусор под видом ответа,
            // с кодом 200 и без единого признака беды. Замер на двух тысячах узлов: так молча
            // портились ответы 77 сайтов, включая Facebook, Instagram и Box.
            //
            // Молчаливая порча хуже явного отказа: по отказу видно, что чинить, а мусор
            // расходится дальше по обработке и всплывает где угодно.
            throw new InvalidOperationException(
                $"Тело ответа объявлено как '{encoding}' ({body.Length} Б), но распаковать его не удалось", error);
        }

        return false;
    }

    /// <summary>
    /// Распаковывает <c lang="text">deflate</c>, у которого на практике две несовместимые формы.
    /// </summary>
    /// <remarks>
    /// RFC 9110 требует обёртку zlib (RFC 1950), но заметная часть серверов отдаёт «голый»
    /// поток deflate (RFC 1951). Браузеры принимают обе формы, определяя её по первому байту, —
    /// иначе часть сети была бы для них недоступна. Делаем так же: сначала zlib, при неудаче
    /// пробуем без обёртки.
    /// </remarks>
    private static byte[] InflateDeflate(byte[] body)
    {
        try
        {
            return Inflate(body, static source => new ZLibStream(source, CompressionMode.Decompress, leaveOpen: true));
        }
        catch (InvalidDataException)
        {
            return Inflate(body, static source => new DeflateStream(source, CompressionMode.Decompress, leaveOpen: true));
        }
    }

    private static byte[] Inflate(byte[] body, Func<Stream, Stream> createDecoder)
    {
        using var source = new MemoryStream(body, writable: false);
        using var decoder = createDecoder(source);

        // Сжатое тело почти всегда разворачивается в несколько раз; начальная ёмкость по этой
        // оценке избавляет от пары удвоений на каждом ответе.
        var output = new ArrayBufferWriter<byte>(Math.Min(body.Length * 4, 1 << 20));
        var buffer = ArrayPool<byte>.Shared.Rent(81920);

        try
        {
            while (true)
            {
                var read = decoder.Read(buffer, 0, buffer.Length);
                if (read is 0) break;

                if (output.WrittenCount + read > MaximumDecodedLength)
                    throw new InvalidDataException($"Распакованное тело превысило предел в {MaximumDecodedLength} байт");

                output.Write(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return output.WrittenSpan.ToArray();
    }

    private static bool Matches(string value, string name)
        => value.Equals(name, StringComparison.OrdinalIgnoreCase);
}
