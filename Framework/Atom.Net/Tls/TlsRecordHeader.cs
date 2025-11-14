using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Net.Tls;

/// <summary>
/// Заголовок TLS-записи. Формат фиксированный: 5 байт.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="TlsRecordHeader"/>.
/// </remarks>
/// <param name="type">Тип содержимого TLS-записи.</param>
/// <param name="ver"></param>
/// <param name="len"></param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Auto)]
public readonly struct TlsRecordHeader(TlsContentType type, ushort ver, ushort len) : IEquatable<TlsRecordHeader>
{
    /// <summary>
    /// Тип содержимого TLS-записи.
    /// </summary>
    public readonly TlsContentType ContentType { get; } = type;

    /// <summary>
    /// 
    /// </summary>
    public readonly ushort LegacyVersion { get; } = ver;

    /// <summary>
    /// 
    /// </summary>
    public readonly ushort Length { get; } = len;

    /// <summary>
    /// Сериализует заголовок в буфер (минимум 5 байт).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Write(Span<byte> dst)
    {
        dst[0] = (byte)ContentType;
        BinaryPrimitives.WriteUInt16BigEndian(dst.Slice(1, 2), LegacyVersion);
        BinaryPrimitives.WriteUInt16BigEndian(dst.Slice(3, 2), Length);
        return 5;
    }

    /// <summary>
    /// Десериализация из массива 5 байт. Без аллокаций.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TlsRecordHeader Read(ReadOnlySpan<byte> src)
    {
        var type = (TlsContentType)src[0];
        var ver = (ushort)((src[1] << 8) | src[2]);
        var len = (ushort)((src[3] << 8) | src[4]);
        return new TlsRecordHeader(type, ver, len);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(ContentType.GetHashCode(), LegacyVersion.GetHashCode(), Length.GetHashCode());

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(TlsRecordHeader other) => ContentType.Equals(other.ContentType) && LegacyVersion.Equals(other.LegacyVersion) && Length.Equals(other.Length);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        TlsRecordHeader other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TlsRecordHeader left, TlsRecordHeader right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TlsRecordHeader left, TlsRecordHeader right) => !(left == right);
}