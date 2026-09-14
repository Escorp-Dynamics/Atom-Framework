using System.Buffers.Binary;
using System.Text;

namespace Atom.Display.Wayland.Protocol;

/// <summary>
/// Сообщение протокола Wayland.
/// </summary>
/// <remarks>
/// Формат на проводе: идентификатор объекта (4 байта), затем в одном слове — длина всего
/// сообщения в старших 16 битах и код операции в младших. Дальше идут аргументы, каждый выровнен
/// по четырём байтам.
/// </remarks>
internal readonly record struct WaylandMessage
{
    /// <summary>Объект, которому адресовано сообщение.</summary>
    public required uint ObjectId { get; init; }

    /// <summary>Код операции в интерфейсе объекта.</summary>
    public required ushort Opcode { get; init; }

    /// <summary>Тело сообщения без заголовка.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Длина заголовка сообщения.</summary>
    public const int HeaderLength = 8;

    /// <summary>
    /// Разбирает сообщение из буфера.
    /// </summary>
    /// <param name="buffer">Буфер, начинающийся с заголовка.</param>
    /// <param name="message">Разобранное сообщение.</param>
    /// <param name="consumed">Сколько байт занято сообщением.</param>
    /// <returns><see langword="false"/>, если сообщение ещё не пришло целиком.</returns>
    public static bool TryParse(ReadOnlyMemory<byte> buffer, out WaylandMessage message, out int consumed)
    {
        message = default;
        consumed = 0;

        if (buffer.Length < HeaderLength)
            return false;

        var span = buffer.Span;
        var objectId = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var sizeAndOpcode = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);

        var size = (int)(sizeAndOpcode >> 16);
        var opcode = (ushort)(sizeAndOpcode & 0xFFFF);

        if (size < HeaderLength)
            throw new WaylandProtocolException("Длина сообщения меньше заголовка: " + size.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (buffer.Length < size)
            return false;

        message = new WaylandMessage
        {
            ObjectId = objectId,
            Opcode = opcode,
            Payload = buffer[HeaderLength..size],
        };

        consumed = size;
        return true;
    }

    /// <summary>Читает целое со смещения.</summary>
    public int ReadInt(ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(Payload.Span[offset..]);
        offset += 4;
        return value;
    }

    /// <summary>Читает беззнаковое целое со смещения.</summary>
    public uint ReadUInt(ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(Payload.Span[offset..]);
        offset += 4;
        return value;
    }

    /// <summary>
    /// Читает дробное число со смещения.
    /// </summary>
    /// <remarks>
    /// Координаты идут в формате 24.8 с фиксированной точкой: целая часть в старших 24 битах,
    /// дробная — в младших восьми.
    /// </remarks>
    public double ReadFixed(ref int offset) => ReadInt(ref offset) / 256.0;

    /// <summary>
    /// Читает строку со смещения.
    /// </summary>
    /// <remarks>
    /// На проводе строка — это длина с завершающим нулём, затем байты, дополненные до кратности
    /// четырём. Ноль в длину входит, но в результат не попадает.
    /// </remarks>
    public string ReadString(ref int offset)
    {
        var length = (int)ReadUInt(ref offset);
        if (length == 0)
            return string.Empty;

        var text = Encoding.UTF8.GetString(Payload.Span.Slice(offset, length - 1));
        offset += Align(length);
        return text;
    }

    /// <summary>Выравнивает длину по границе слова протокола.</summary>
    public static int Align(int length) => (length + 3) & ~3;
}
