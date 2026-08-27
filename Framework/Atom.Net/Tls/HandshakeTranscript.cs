using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Пуловский аккумулятор Handshake-сообщений с возможностью посчитать хэш без сброса.
/// Не вызывает GC на «счастливом пути», большие куски (Certificate) складируются чанками.
/// </summary>
internal sealed class HandshakeTranscript : IDisposable
{
    private const int ChunkSize = 16 * 1024;
    private readonly List<byte[]> chunks = new(capacity: 8);
    private int lastLen;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(ReadOnlySpan<byte> data)
    {
        var span = data;

        while (!span.IsEmpty)
        {
            if (chunks.Count is 0 || lastLen == chunks[^1].Length)
            {
                var buf = ArrayPool<byte>.Shared.Rent(Math.Max(ChunkSize, span.Length));
                lastLen = 0;
                chunks.Add(buf);
            }

            var bufArr = chunks[^1];
            var free = bufArr.Length - lastLen;
            var take = Math.Min(free, span.Length);

            span[..take].CopyTo(bufArr.AsSpan(lastLen));
            lastLen += take;
            span = span[take..];
        }
    }

    /// <summary>
    /// Заменяет накопленный транскрипт синтетическим сообщением message_hash.
    /// </summary>
    /// <param name="alg">Хэш-функция согласованного набора шифров.</param>
    /// <remarks>
    /// ★ Требование RFC 8446 §4.4.1 и единственное место во всём протоколе, где транскрипт
    /// переписывается задним числом. При HelloRetryRequest первое приветствие клиента заменяется
    /// сообщением типа 254 с его ХЭШЕМ внутри — так обе стороны получают одинаковый транскрипт,
    /// не храня первое сообщение целиком.
    ///
    /// Забыть эту замену значит получить неверный verify_data в Finished, то есть отказ уже в
    /// самом конце успешного во всём остальном рукопожатия.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReplaceWithMessageHash(HashAlgorithmName alg)
    {
        var hash = ComputeHash(alg);

        // ★ Буферы возвращаются в пул, а не бросаются. Прежде здесь стоял голый Clear, и при
        // HelloRetryRequest все накопленные куски терялись мимо пула — а это единственное место,
        // где транскрипт переписывается целиком.
        ReturnChunks();

        ReadOnlySpan<byte> header = [254, 0, 0, (byte)hash.Length];

        Append(header);
        Append(hash);
    }

    /// <summary>
    /// Возвращает копию всех записанных байт транскрипта (диагностика).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] CopyRaw()
    {
        var total = 0;
        for (var i = 0; i < chunks.Count; i++) total += (i == chunks.Count - 1) ? lastLen : chunks[i].Length;
        var result = new byte[total];
        var offset = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            var len = (i == chunks.Count - 1) ? lastLen : chunks[i].Length;
            chunks[i].AsSpan(0, len).CopyTo(result.AsSpan(offset));
            offset += len;
        }
        return result;
    }

    /// <summary>
    /// Вычисляет хэш всех записанных байт под указанный алгоритм, не меняя состояния.
    /// </summary>
    /// <param name="alg">Хэш-функция.</param>
    /// <returns>Хэш транскрипта.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] ComputeHash(HashAlgorithmName alg)
    {
        using var ih = IncrementalHash.CreateHash(alg);

        for (var i = 0; i < chunks.Count - 1; i++) ih.AppendData(chunks[i]);
        if (chunks.Count > 0) ih.AppendData(chunks[^1].AsSpan(0, lastLen));

        return ih.GetHashAndReset(); // локальный инкрементальный, безопасно
    }

    /// <summary>
    /// Возвращает накопленные куски в пул.
    /// </summary>
    /// <remarks>
    /// ★ Освобождать транскрипт надо СРАЗУ по завершении рукопожатия, а не по смерти соединения.
    /// Дальше он не нужен: все хэши, что от него требуются, уже сняты. Прежде метода не было
    /// вовсе — только финализатор, — и каждое живое соединение держало один-три арендованных
    /// куска по шестнадцать килобайт до самого конца. При тысяче одновременных соединений это
    /// десятки мегабайт, занятых ничем, да ещё и мимо пула: сборщик возвращает их не сразу.
    /// </remarks>
    public void Dispose()
    {
        ReturnChunks();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Возвращает все куски в пул и очищает список.
    /// </summary>
    private void ReturnChunks()
    {
        foreach (var chunk in chunks) ArrayPool<byte>.Shared.Return(chunk, clearArray: true);

        chunks.Clear();
        lastLen = 0;
    }

    ~HandshakeTranscript()
    {
        // На случай, если Dispose не вызовут: возвращаем куски в пул.
        foreach (var chunk in chunks) ArrayPool<byte>.Shared.Return(chunk, clearArray: true);
    }
}