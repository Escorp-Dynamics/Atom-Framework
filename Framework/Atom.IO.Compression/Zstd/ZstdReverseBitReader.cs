using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Обратный битовый поток формата Zstandard (RFC 8878, §3.1.1.3.2.1 и §4.2.2).
/// </summary>
/// <remarks>
/// <para>
/// Кодировщик пакует биты вперёд: значение кладётся младшим битом в текущую битовую позицию,
/// байты сбрасываются little-endian. Поэтому декодер обязан читать поток <b>с конца</b>, причём
/// внутри каждого байта — <b>от старшего бита к младшему</b>: последний записанный бит физически
/// оказывается старшим установленным битом последнего байта.
/// </para>
/// <para>
/// Завершающая набивка — это до семи нулей и один бит «1» в старших разрядах последнего байта.
/// Данными являются биты <b>ниже</b> маркера в том же байте, поэтому их нельзя отбрасывать.
/// </para>
/// <para>
/// Почему понадобился отдельный тип: общий <c lang="text">Atom.IO.ReverseBitReader</c> выдаёт биты внутри байта
/// от младшего к старшему и в <c lang="text">TrySkipPadding</c> обнуляет весь буфер после найденного маркера,
/// то есть теряет до семи бит настоящих данных. Обе особенности несовместимы с Zstd, а сам тип —
/// публичный API другого пакета, поэтому его семантику менять нельзя.
/// </para>
/// <para>
/// Устройство: 64-битное окно, выровненное по СТАРШЕМУ краю — следующий бит всегда лежит в бите 63.
/// Тогда чтение это один сдвиг, а пропуск — один сдвиг влево, без масок и ветвлений.
/// Подкачка идёт пачкой по восемь байт: little-endian чтение восьми байт как раз даёт нужный
/// порядок (старший байт значения — это следующий по потоку байт данных).
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal ref struct ZstdReverseBitReader
{
    private readonly ReadOnlySpan<byte> data;
    private readonly int totalBits;   // сколько бит данных в потоке (без набивки)
    private int index;                // индекс следующего байта для подкачки (движется к началу)
    private ulong window;             // окно: достоверны старшие valid бит, младшие — нули
    private int valid;                // сколько бит окна достоверны
    private int consumed;             // сколько бит уже выдано наружу

    /// <summary>
    /// Создаёт читатель, сразу снимая завершающую набивку.
    /// </summary>
    /// <param name="source">Байты обратного битового потока.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdReverseBitReader(ReadOnlySpan<byte> source)
    {
        data = source;
        totalBits = 0;
        index = -1;
        window = 0;
        valid = 0;
        consumed = 0;
        IsValid = false;

        if (source.IsEmpty) return;

        var last = source[^1];

        // Последний байт обязан содержать маркер '1' — иначе набивка не найдена и поток повреждён.
        if (last == 0) return;

        var marker = 31 - BitOperations.LeadingZeroCount(last);
        window = (ulong)(uint)(last & ((1 << marker) - 1)) << (64 - marker);
        valid = marker;
        index = source.Length - 2;
        totalBits = ((source.Length - 1) * 8) + marker;
        IsValid = true;
    }

    /// <summary>Признак корректного потока (непустой и с найденным маркером набивки).</summary>
    public bool IsValid { readonly get; private set; }

    /// <summary>Сколько бит данных ещё не выдано.</summary>
    public readonly int AvailableBits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => totalBits - consumed;
    }

    /// <summary>
    /// Читает <paramref name="count"/> бит (0..32) из потока.
    /// </summary>
    /// <param name="count">Сколько бит прочитать.</param>
    /// <param name="value">Прочитанное значение.</param>
    /// <returns><see langword="true"/>, если бит хватило.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadBits(int count, out uint value)
    {
        if (count == 0)
        {
            value = 0;
            return true;
        }

        if (consumed + count > totalBits)
        {
            value = 0;
            return false;
        }

        if (valid < count) Refill(count);

        value = (uint)(window >> (64 - count));
        window <<= count;
        valid -= count;
        consumed += count;
        return true;
    }

    /// <summary>
    /// Подсматривает <paramref name="count"/> бит, добивая нулями за концом потока.
    /// </summary>
    /// <remarks>
    /// RFC 8878, §4.2.2: символ Хаффмана определяется по старшим <c lang="text">Max_Number_of_Bits</c> битам,
    /// и у последних символов потока часть этих бит уже за его границей — они считаются нулевыми.
    /// Здесь это получается само собой: окно за концом данных добивается нулями.
    /// </remarks>
    /// <param name="count">Сколько бит подсмотреть.</param>
    /// <returns>Значение, выровненное по старшему краю.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint PeekBitsPadded(int count)
    {
        if (count == 0) return 0;
        if (valid < count) Refill(count);
        return (uint)(window >> (64 - count));
    }

    /// <summary>
    /// Пропускает <paramref name="count"/> бит.
    /// </summary>
    /// <param name="count">Сколько бит пропустить.</param>
    /// <returns><see langword="true"/>, если бит хватило.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySkipBits(int count)
    {
        if (count == 0) return true;
        if (consumed + count > totalBits) return false;

        if (valid < count) Refill(count);

        window <<= count;
        valid -= count;
        consumed += count;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Refill(int count)
    {
        if (index >= 7)
        {
            // Пачкой: little-endian восьмёрка байт кладёт data[index] в старший байт значения,
            // то есть ровно в том порядке, в каком байты идут по обратному потоку.
            var chunk = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(index - 7, 8));
            var loaded = (64 - valid) >> 3;
            var newValid = valid + (loaded << 3);
            var shifted = valid == 0 ? chunk : chunk >> valid;

            // Хвостовые биты частично загруженного байта обнуляем: иначе следующая подкачка
            // наложит на них другие данные.
            window |= newValid == 64 ? shifted : shifted & (ulong.MaxValue << (64 - newValid));
            index -= loaded;
            valid = newValid;
            return;
        }

        while (valid < count)
        {
            if (index < 0)
            {
                // За концом потока — нули; окно уже обнулено сдвигами.
                valid = 64;
                return;
            }

            window |= (ulong)data[index--] << (56 - valid);
            valid += 8;
        }
    }
}
