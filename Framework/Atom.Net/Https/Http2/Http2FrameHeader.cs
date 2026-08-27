using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Net.Https.Http2;

/// <summary>
/// Заголовок кадра HTTP/2 (RFC 9113, §4.1).
/// </summary>
/// <remarks>
/// Ровно девять байт: длина полезной нагрузки в 24 битах, тип, флаги и идентификатор потока в 31
/// бите со сброшенным старшим разрядом. Структура сделана <see langword="readonly"/> и без ссылок,
/// чтобы чтение кадра не порождало аллокаций — на горячем пути их проходят десятки тысяч.
/// </remarks>
/// <param name="length">Длина полезной нагрузки.</param>
/// <param name="type">Тип кадра.</param>
/// <param name="flags">Флаги кадра.</param>
/// <param name="streamId">Идентификатор потока.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Auto)]
public readonly struct Http2FrameHeader(int length, Http2FrameType type, byte flags, int streamId) : IEquatable<Http2FrameHeader>
{
    /// <summary>Размер заголовка кадра в байтах.</summary>
    public const int Size = 9;

    /// <summary>Длина полезной нагрузки кадра.</summary>
    public int Length { get; } = length;

    /// <summary>Тип кадра.</summary>
    public Http2FrameType Type { get; } = type;

    /// <summary>Флаги кадра.</summary>
    public byte Flags { get; } = flags;

    /// <summary>Идентификатор потока; ноль означает уровень соединения.</summary>
    public int StreamId { get; } = streamId;

    /// <summary>Установлен ли флаг END_STREAM.</summary>
    public bool EndStream => (Flags & 0x01) is not 0;

    /// <summary>Установлен ли флаг END_HEADERS.</summary>
    public bool EndHeaders => (Flags & 0x04) is not 0;

    /// <summary>Установлен ли флаг PADDED.</summary>
    public bool Padded => (Flags & 0x08) is not 0;

    /// <summary>Установлен ли флаг PRIORITY.</summary>
    public bool Priority => (Flags & 0x20) is not 0;

    /// <summary>Установлен ли флаг ACK (для SETTINGS и PING).</summary>
    public bool Ack => (Flags & 0x01) is not 0;

    /// <summary>
    /// Записывает заголовок в буфер.
    /// </summary>
    /// <param name="destination">Буфер длиной не менее <see cref="Size"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Span<byte> destination)
    {
        // Длина кадра занимает 24 бита и пишется тремя байтами: усечение каждого до одного байта —
        // это разбиение числа на разряды, а не потеря данных. Проверяемая арифметика, включённая
        // для всего фреймворка, иначе бросала бы исключение на любой длине больше 255.
        unchecked
        {
            destination[0] = (byte)(Length >> 16);
            destination[1] = (byte)(Length >> 8);
            destination[2] = (byte)Length;
        }

        destination[3] = (byte)Type;
        destination[4] = Flags;

        // Старший бит идентификатора зарезервирован и обязан быть нулём. Приведение выполняем в
        // непроверяемом контексте: для фреймворка включена проверка переполнения, а здесь смена
        // знакового представления на беззнаковое — штатная операция, и на идентификаторах со
        // взведённым старшим битом она бросала бы исключение вместо того, чтобы сбросить бит.
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(5, 4), unchecked((uint)StreamId) & 0x7FFFFFFF);
    }

    /// <summary>
    /// Читает заголовок кадра из буфера.
    /// </summary>
    /// <param name="source">Буфер длиной не менее <see cref="Size"/>.</param>
    /// <returns>Разобранный заголовок.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Http2FrameHeader Read(ReadOnlySpan<byte> source)
    {
        var length = (source[0] << 16) | (source[1] << 8) | source[2];
        var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(source.Slice(5, 4)) & 0x7FFFFFFF);

        return new Http2FrameHeader(length, (Http2FrameType)source[3], source[4], streamId);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(Length, Type, Flags, StreamId);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Http2FrameHeader other)
        => Length == other.Length && Type == other.Type && Flags == other.Flags && StreamId == other.StreamId;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Http2FrameHeader other && Equals(other);

    /// <summary>
    /// Определяет, равны ли два заголовка.
    /// </summary>
    /// <param name="left">Левый операнд.</param>
    /// <param name="right">Правый операнд.</param>
    /// <returns><see langword="true"/>, если заголовки совпадают.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Http2FrameHeader left, Http2FrameHeader right) => left.Equals(right);

    /// <summary>
    /// Определяет, различаются ли два заголовка.
    /// </summary>
    /// <param name="left">Левый операнд.</param>
    /// <param name="right">Правый операнд.</param>
    /// <returns><see langword="true"/>, если заголовки различаются.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Http2FrameHeader left, Http2FrameHeader right) => !left.Equals(right);
}
