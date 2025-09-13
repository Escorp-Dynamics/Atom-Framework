#pragma warning disable CA5398

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Authentication;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS (0x002B): supported_versions.
/// Клиент объявляет набор поддерживаемых версий TLS (RFC 8446, §4.2.1).
/// Важно для TLS 1.3 — без него сервер может «скатиться» в 1.2 или разорвать соединение.
/// </summary>
public class SupportedVersionsTlsExtension : TlsExtension
{
    private ushort[] cache = [];

    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x002B;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + 1 + (cache.Length * 2);

    /// <summary>
    /// Поддерживаемые версии TLS в порядке приоритета (например: 0x0304, 0x0303).
    /// Значения соответствуют <see cref="SslProtocols"/> (0x0303 = TLS 1.2, 0x0304 = TLS 1.3).
    /// </summary>
    public IEnumerable<SslProtocols> Versions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            field = value;
            cache = [.. value.Select(static a => (ushort)(a switch { SslProtocols.Tls12 => 0x0303, _ => 0x0304, }))];
        }
    } = [];

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        var count = cache.Length;
        var bodyLen = 1 + (2 * count);

        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], Id); offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], (ushort)bodyLen); offset += 2;

        buffer[offset++] = (byte)(2 * count);

        foreach (var v in cache)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer[offset..], v);
            offset += 2;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Versions = [];
        base.Reset();
    }
}