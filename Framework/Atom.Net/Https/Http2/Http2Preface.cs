using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Atom.Net.Https.Connections;
using Atom.Net.Https.Http;

namespace Atom.Net.Https.Http2;

/// <summary>
/// Формирование преамбулы соединения HTTP/2 и стартового обмена параметрами.
/// </summary>
/// <remarks>
/// Начало соединения — самая наблюдаемая его часть: сервер видит фиксированную преамбулу, затем
/// SETTINGS с конкретным составом и порядком, затем приращение окна и, у некоторых браузеров,
/// дерево приоритетов. Из этой последовательности и строится отпечаток HTTP/2, поэтому её сборка
/// вынесена отдельно и полностью определяется профилем, а не логикой соединения.
/// </remarks>
public static class Http2Preface
{
    /// <summary>Обязательная преамбула клиента (RFC 9113, §3.4).</summary>
    public static ReadOnlySpan<byte> ClientMagic => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

    /// <summary>
    /// Записывает преамбулу, кадр SETTINGS, приращение окна соединения и кадры приоритетов.
    /// </summary>
    /// <param name="destination">Буфер назначения.</param>
    /// <param name="settings">Профиль HTTP/2.</param>
    /// <param name="connectionWindowIncrement">Приращение окна соединения; ноль — не отправлять.</param>
    /// <returns>Число записанных байт.</returns>
    public static int Write(Span<byte> destination, in Http2Settings settings, uint connectionWindowIncrement)
    {
        var offset = 0;

        ClientMagic.CopyTo(destination);
        offset += ClientMagic.Length;

        offset += WriteSettings(destination[offset..], settings);

        if (connectionWindowIncrement > 0)
        {
            offset += WriteWindowUpdate(destination[offset..], streamId: 0, connectionWindowIncrement);
        }

        // Кадры приоритетов шлёт не всякий браузер: Firefox строит служебное дерево, Chrome — нет.
        // Отправлять их «на всякий случай» нельзя: лишние кадры так же заметны, как недостающие.
        if (settings.UsePriorityFrames)
        {
            foreach (var priority in settings.PriorityTree)
            {
                offset += WritePriority(destination[offset..], priority);
            }
        }

        return offset;
    }

    /// <summary>
    /// Записывает кадр SETTINGS в порядке, заданном профилем.
    /// </summary>
    /// <param name="destination">Буфер назначения.</param>
    /// <param name="settings">Профиль HTTP/2.</param>
    /// <returns>Число записанных байт.</returns>
    public static int WriteSettings(Span<byte> destination, in Http2Settings settings)
    {
        var payloadOffset = Http2FrameHeader.Size;
        var count = 0;

        // Порядок берём из профиля дословно: сервер видит именно последовательность пар, и
        // перестановка меняет отпечаток, даже если значения совпадают.
        foreach (var setting in settings.SettingsOrder)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(payloadOffset, 2), (ushort)setting.Id);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(payloadOffset + 2, 4), setting.Value);
            payloadOffset += 6;
            count++;
        }

        var payloadLength = count * 6;
        new Http2FrameHeader(payloadLength, Http2FrameType.Settings, flags: 0, streamId: 0).Write(destination);

        return Http2FrameHeader.Size + payloadLength;
    }

    /// <summary>
    /// Записывает кадр WINDOW_UPDATE.
    /// </summary>
    /// <param name="destination">Буфер назначения.</param>
    /// <param name="streamId">Поток; ноль — уровень соединения.</param>
    /// <param name="increment">Приращение окна.</param>
    /// <returns>Число записанных байт.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteWindowUpdate(Span<byte> destination, int streamId, uint increment)
    {
        new Http2FrameHeader(4, Http2FrameType.WindowUpdate, flags: 0, streamId).Write(destination);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(Http2FrameHeader.Size, 4), increment & 0x7FFFFFFF);

        return Http2FrameHeader.Size + 4;
    }

    /// <summary>
    /// Записывает кадр PRIORITY.
    /// </summary>
    /// <param name="destination">Буфер назначения.</param>
    /// <param name="priority">Описание приоритета.</param>
    /// <returns>Число записанных байт.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WritePriority(Span<byte> destination, in StreamPriority priority)
    {
        new Http2FrameHeader(5, Http2FrameType.Priority, flags: 0, priority.Id).Write(destination);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(Http2FrameHeader.Size, 4), unchecked((uint)priority.DependsOn) & 0x7FFFFFFF);

        // Вес передаётся уменьшенным на единицу: диапазон 1..256 кодируется байтом 0..255.
        destination[Http2FrameHeader.Size + 4] = priority.Weight;

        return Http2FrameHeader.Size + 5;
    }

    /// <summary>
    /// Вычисляет размер буфера, достаточный для всей стартовой последовательности.
    /// </summary>
    /// <param name="settings">Профиль HTTP/2.</param>
    /// <returns>Требуемый размер в байтах.</returns>
    public static int GetRequiredSize(in Http2Settings settings)
    {
        var settingsCount = settings.SettingsOrder.Count();
        var priorityCount = settings.UsePriorityFrames ? settings.PriorityTree.Count() : 0;

        return ClientMagic.Length
            + Http2FrameHeader.Size + (settingsCount * 6)
            + Http2FrameHeader.Size + 4
            + (priorityCount * (Http2FrameHeader.Size + 5));
    }
}
