using System.Buffers;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Накопитель байт для разбора кадров, приходящих кусками произвольного размера.
/// </summary>
/// <remarks>
/// Существует ради одной вещи: разбор кадрового протокола поверх потока — это цикл «дописали
/// кусок, сняли столько целых кадров, сколько собралось». Наивная реализация на списке байт
/// делает на каждом витке копию всего накопленного, и стоимость разбора становится
/// КВАДРАТИЧНОЙ по размеру ответа: на сотне килобайт это уже заметно, на мегабайте — определяет
/// всё время запроса.
///
/// Здесь вместо этого пара указателей в арендованном буфере. Разобранная часть не удаляется, а
/// оставляется позади; уплотнение происходит, только когда голова ушла достаточно далеко, и
/// стоит одного перемещения хвоста вместо копии на каждый кадр.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "Параметр нужен только для аренды буфера и полем не становится.")]
public sealed class StreamFrameBuffer : IDisposable
{
    /// <summary>Порог, после которого выгоднее сдвинуть хвост в начало.</summary>
    private const int CompactionThreshold = 8192;

    private byte[] buffer;
    private int head;
    private int tail;

    /// <summary>
    /// Создаёт накопитель.
    /// </summary>
    /// <param name="capacity">Начальная вместимость.</param>
    public StreamFrameBuffer(int capacity = 4096) => buffer = ArrayPool<byte>.Shared.Rent(Math.Max(capacity, 256));

    /// <summary>Непрочитанные байты.</summary>
    public ReadOnlySpan<byte> Available => buffer.AsSpan(head, tail - head);

    /// <summary>Сколько байт доступно для разбора.</summary>
    public int Length => tail - head;

    /// <summary>
    /// Дописывает очередной кусок.
    /// </summary>
    /// <param name="data">Данные.</param>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;

        EnsureRoom(data.Length);
        data.CopyTo(buffer.AsSpan(tail));
        tail += data.Length;
    }

    /// <summary>
    /// Отмечает часть буфера разобранной.
    /// </summary>
    /// <param name="count">Сколько байт снять.</param>
    public void Consume(int count)
    {
        head += count;

        if (head == tail)
        {
            head = 0;
            tail = 0;
            return;
        }

        // Уплотняем не на каждом кадре, а когда позади накопилось заметно: перемещение хвоста
        // стоит копии, и делать её ради нескольких байт значит вернуть ту самую квадратичность.
        if (head < CompactionThreshold) return;

        buffer.AsSpan(head, tail - head).CopyTo(buffer);
        tail -= head;
        head = 0;
    }

    private void EnsureRoom(int required)
    {
        if (tail + required <= buffer.Length) return;

        if (head > 0)
        {
            buffer.AsSpan(head, tail - head).CopyTo(buffer);
            tail -= head;
            head = 0;

            if (tail + required <= buffer.Length) return;
        }

        var grown = ArrayPool<byte>.Shared.Rent(Math.Max(buffer.Length * 2, tail + required));
        buffer.AsSpan(0, tail).CopyTo(grown);

        ArrayPool<byte>.Shared.Return(buffer);
        buffer = grown;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (buffer.Length is 0) return;

        ArrayPool<byte>.Shared.Return(buffer);
        buffer = [];
    }
}
