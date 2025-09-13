using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS (0x0005): status_request.
/// Используется для запроса OCSP stapling от сервера.
/// </summary>
public class StatusRequestTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0005;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 5;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        // [Extension ID]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        // [Length = 5]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 5);
        offset += 2;

        // [status_type = 1 (OCSP)]
        buffer[offset++] = 0x01;

        // [responder_id_list] — пусто
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 0);
        offset += 2;

        // [request_extensions] — пусто
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 0);
        offset += 2;
    }
}