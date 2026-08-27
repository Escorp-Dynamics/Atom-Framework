namespace Atom.Net.Quic;

/// <summary>
/// Сборка данных рукопожатия из кадров CRYPTO одного уровня шифрования.
/// </summary>
/// <remarks>
/// Кадры CRYPTO образуют упорядоченный поток байт со смещениями — но приходят в пакетах UDP,
/// которые теряются и обгоняют друг друга. Поэтому куски приходится складывать в правильном
/// порядке, а отдавать наверх — только ЦЕЛЫЕ сообщения рукопожатия.
///
/// Дробление здесь не редкий случай, а норма: ServerHello современного сервера с постквантовой
/// долей ключа занимает больше тысячи байт и в один пакет не помещается. Попытка разобрать
/// половину сообщения даёт «выход за границы» — то есть выглядит как ошибка разбора, а не как
/// нехватка данных.
/// </remarks>
public sealed class QuicCryptoStream
{
    private readonly List<byte> assembled = [];
    private readonly SortedDictionary<ulong, byte[]> pending = [];

    private ulong expectedOffset;
    private int consumed;

    /// <summary>
    /// Принимает кадр CRYPTO.
    /// </summary>
    /// <param name="offset">Смещение данных в потоке рукопожатия.</param>
    /// <param name="data">Данные кадра.</param>
    public void Write(ulong offset, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;

        if (offset + (ulong)data.Length <= expectedOffset) return;

        if (offset > expectedOffset)
        {
            pending[offset] = data.ToArray();
            return;
        }

        var skip = (int)(expectedOffset - offset);
        Append(data[skip..]);
        DrainPending();
    }

    /// <summary>
    /// Пытается снять из потока очередное целое сообщение рукопожатия.
    /// </summary>
    /// <param name="message">Сообщение вместе с четырёхбайтовым заголовком.</param>
    /// <returns><see langword="true"/>, если сообщение собрано целиком.</returns>
    /// <remarks>
    /// Длина сообщения записана в его же заголовке тремя байтами, поэтому граница известна сразу,
    /// как только пришли первые четыре байта.
    /// </remarks>
    public bool TryReadMessage(out ReadOnlyMemory<byte> message)
    {
        message = default;

        var available = assembled.Count - consumed;
        if (available < 4) return false;

        var length = (assembled[consumed + 1] << 16) | (assembled[consumed + 2] << 8) | assembled[consumed + 3];
        var total = 4 + length;

        if (available < total) return false;

        var buffer = new byte[total];
        assembled.CopyTo(consumed, buffer, 0, total);
        consumed += total;

        // Разобранную часть выбрасываем, когда её накопилось заметно: держать весь транскрипт
        // рукопожатия в списке незачем, а копировать на каждом сообщении — расточительно.
        if (consumed > 8192)
        {
            assembled.RemoveRange(0, consumed);
            consumed = 0;
        }

        message = buffer;
        return true;
    }

    private void Append(ReadOnlySpan<byte> data)
    {
        assembled.AddRange(data);
        expectedOffset += (ulong)data.Length;
    }

    private void DrainPending()
    {
        while (pending.Count > 0)
        {
            var entry = System.Linq.Enumerable.First(pending);
            if (entry.Key > expectedOffset) break;

            pending.Remove(entry.Key);

            var skip = (int)(expectedOffset - entry.Key);
            if (skip >= entry.Value.Length) continue;

            Append(entry.Value.AsSpan(skip));
        }
    }
}
