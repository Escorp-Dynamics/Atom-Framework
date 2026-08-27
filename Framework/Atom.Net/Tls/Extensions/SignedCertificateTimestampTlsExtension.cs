using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// signed_certificate_timestamp (0x0012).
/// </summary>
/// <remarks>
/// В ClientHello расширение не несёт данных: клиент лишь сообщает, что готов принять метки
/// прозрачности сертификатов. Браузеры отправляют его всегда, и его отсутствие — заметный признак
/// не-браузерного клиента.
///
/// Прежняя реализация была заглушкой нулевого размера, то есть расширение не попадало в
/// ClientHello вовсе, хотя и присутствовало в профиле.
/// </remarks>
public sealed class SignedCertificateTimestampTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0012;

    /// <inheritdoc/>
    public override int Size => 2 + 2;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 0);
        offset += 2;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Id = 0x0012;
        base.Reset();
    }
}
