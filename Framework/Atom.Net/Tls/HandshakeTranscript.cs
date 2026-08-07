using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Пуловский аккумулятор Handshake-сообщений с возможностью посчитать хэш без сброса.
/// Не вызывает GC на «счастливом пути», большие куски (Certificate) складируются чанками.
/// </summary>
internal sealed class HandshakeTranscript
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
    /// Вычисляет хэш всех записанных байт под указанный алгоритм, без изменений внутреннего состояния.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] ComputeHash(HashAlgorithmName alg)
    {
        using var ih = IncrementalHash.CreateHash(alg);

        for (var i = 0; i < chunks.Count - 1; i++) ih.AppendData(chunks[i]);
        if (chunks.Count > 0) ih.AppendData(chunks[^1].AsSpan(0, lastLen));

        return ih.GetHashAndReset(); // локальный инкрементальный, безопасно
    }

    ~HandshakeTranscript()
    {
        // На случай, если Dispose не вызовут: возвращаем чанки в пул.
        foreach (var c in chunks)
            ArrayPool<byte>.Shared.Return(c, clearArray: true);
    }
}