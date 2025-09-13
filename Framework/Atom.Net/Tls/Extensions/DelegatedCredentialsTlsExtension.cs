using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS (0x0037): delegated_credentials.
/// Сообщает поддержку делегированных DC-сертификатов (Cloudflare).
/// </summary>
public class DelegatedCredentialsTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0037;

    /// <inheritdoc/>
    public override int Size => 2 + 2; // ID + Length (0)

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], 0);
        offset += 2;
    }
}