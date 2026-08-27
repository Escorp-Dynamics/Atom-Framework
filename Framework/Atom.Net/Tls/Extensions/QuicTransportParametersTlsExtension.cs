using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// quic_transport_parameters (0x0039).
/// </summary>
/// <remarks>
/// Единственный канал, по которому стороны QUIC договариваются о пределах потоков, данных и
/// времени простоя: собственных сообщений для этого в QUIC нет, всё едет внутри рукопожатия TLS.
///
/// Отсюда следует и обратное: параметры наблюдаемы сервером ровно так же, как набор шифров, и
/// входят в отпечаток. Отправить их «как удобно» — значит отличаться от браузера в первом же
/// пакете.
/// </remarks>
public sealed class QuicTransportParametersTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0039;

    /// <summary>
    /// Закодированные параметры транспорта.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; } = ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + Data.Length;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)Data.Length);
        offset += 2;

        Data.Span.CopyTo(buffer[offset..]);
        offset += Data.Length;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Id = 0x0039;
        Data = ReadOnlyMemory<byte>.Empty;
        base.Reset();
    }
}
