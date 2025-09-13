using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Расширение TLS: GREASE.
/// Используется для маскировки fingerprint'а. Не несёт смысла, но помогает в антидетекте.
/// </summary>
public class GreaseTlsExtension : TlsExtension
{
    /// <summary>
    /// Идентификатор расширения (должен быть из GREASE-диапазона).
    /// </summary>
    public override required ushort Id { get; set; }

    /// <inheritdoc/>
    public override int Size => 2 + 2 + Data.Length;

    /// <summary>
    /// Произвольное тело расширения (может быть пустым).
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; } = ReadOnlyMemory<byte>.Empty;

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

    /// <summary>
    /// Создаёт случайный GREASE.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GreaseTlsExtension Create()
    {
        byte len;
        Span<byte> one = stackalloc byte[1];
        RandomNumberGenerator.Fill(one);
        len = (byte)(one[0] & 0x07); // 0..7

        var data = len is 0 ? [] : new byte[len];
        if (len > 0) RandomNumberGenerator.Fill(data);

        return new GreaseTlsExtension { Id = Tls.Grease.Extension, Data = data };
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Id = default;
        Data = ReadOnlyMemory<byte>.Empty;
        base.Reset();
    }
}