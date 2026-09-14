using System.Buffers.Binary;
using System.Text;

namespace Atom.Display.Wayland.Protocol;

/// <summary>
/// Сборщик сообщений протокола Wayland.
/// </summary>
/// <remarks>
/// Длина сообщения известна только после записи всех аргументов, поэтому заголовок дописывается
/// в конце — место под него резервируется заранее.
/// </remarks>
internal sealed class WaylandMessageWriter
{
    // ★ Событий сотни в секунду, и каждое раньше стоило списка и массива из ToArray.
    // Буфер переиспользуется, а сообщение отдаётся срезом без копирования.
    private byte[] buffer = new byte[InitialCapacity];
    private int length;

    /// <summary>Начинает сообщение указанному объекту.</summary>
    public WaylandMessageWriter Begin(uint objectId, ushort opcode)
    {
        length = 0;
        var header = Reserve(WaylandMessage.HeaderLength);

        BinaryPrimitives.WriteUInt32LittleEndian(header, objectId);

        // Длина дописывается в Build: сейчас известен только код операции.
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], opcode);

        return this;
    }

    /// <summary>Записывает целое.</summary>
    public WaylandMessageWriter WriteInt(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
        return this;
    }

    /// <summary>Записывает беззнаковое целое.</summary>
    public WaylandMessageWriter WriteUInt(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
        return this;
    }

    /// <summary>Отводит место под очередной аргумент.</summary>
    private Span<byte> Reserve(int size)
    {
        if (length + size > buffer.Length)
            Array.Resize(ref buffer, Math.Max(buffer.Length * 2, length + size));

        var slice = buffer.AsSpan(length, size);
        length += size;

        return slice;
    }

    private const int InitialCapacity = 256;

    /// <summary>
    /// Записывает число с фиксированной точкой.
    /// </summary>
    /// <remarks>
    /// Координаты указателя и касания протокол передаёт в формате 24.8 — целая часть и 8 бит
    /// дробной. Передать их целым нельзя: субпиксельная точность видна приложению.
    /// </remarks>
    public WaylandMessageWriter WriteFixed(double value) => WriteInt((int)Math.Round(value * 256.0));

    /// <summary>Записывает строку с завершающим нулём и выравниванием.</summary>
    public WaylandMessageWriter WriteString(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteUInt((uint)(byteCount + 1));

        var padded = WaylandMessage.Align(byteCount + 1);
        var destination = Reserve(padded);

        _ = Encoding.UTF8.GetBytes(value, destination);
        destination[byteCount..].Clear();

        return this;
    }

    /// <summary>Записывает массив байт с выравниванием.</summary>
    public WaylandMessageWriter WriteArray(ReadOnlySpan<byte> value)
    {
        WriteUInt((uint)value.Length);

        var destination = Reserve(WaylandMessage.Align(value.Length));
        value.CopyTo(destination);
        destination[value.Length..].Clear();

        return this;
    }

    /// <summary>
    /// Завершает сообщение, проставляя длину в заголовок.
    /// </summary>
    /// <remarks>Срез действителен до следующего <see cref="Begin"/>: буфер переиспользуется.</remarks>
    public ReadOnlySpan<byte> Build()
    {
        var message = buffer.AsSpan(0, length);
        var opcode = BinaryPrimitives.ReadUInt32LittleEndian(message[4..]) & 0xFFFF;

        BinaryPrimitives.WriteUInt32LittleEndian(message[4..], ((uint)length << 16) | opcode);

        return message;
    }
}
