using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS "server_name" (0x0000).
/// Используется для указания имени хоста (SNI) при установлении соединения.
/// Обязательно для большинства HTTPS-соединений.
/// </summary>
public class ServerNameTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; }

    /// <inheritdoc/>
    public override int Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var idn = new IdnMapping();
            var ascii = idn.GetAscii(HostName).TrimEnd('.');
            var hostNameBytes = System.Text.Encoding.ASCII.GetBytes(ascii);

            // 2 (len списка) + 1 (type) + 2 (len имени) + N (имя)
            return 2 + 1 + 2 + hostNameBytes.Length;
        }
    }

    /// <summary>
    /// Имя хоста, передаваемое в SNI.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        var idn = new IdnMapping();
        var ascii = idn.GetAscii(HostName).TrimEnd('.');
        var hostNameBytes = System.Text.Encoding.ASCII.GetBytes(ascii);

        // [Extension ID]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id);
        offset += 2;

        // [Extension Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(1 + 2 + hostNameBytes.Length + 2));
        offset += 2;

        // [Server Name List Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)(1 + 2 + hostNameBytes.Length));
        offset += 2;

        // [Name Type] = 0 (host_name)
        buffer[offset++] = 0x00;

        // [Host Name Length]
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)hostNameBytes.Length);
        offset += 2;

        // [Host Name]
        hostNameBytes.CopyTo(buffer[offset..]);
        offset += hostNameBytes.Length;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        HostName = string.Empty;
        base.Reset();
    }
}