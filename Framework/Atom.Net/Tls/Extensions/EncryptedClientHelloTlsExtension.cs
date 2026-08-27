using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// encrypted_client_hello (0xFE0D) в режиме имитации.
/// </summary>
/// <remarks>
/// Расширение отправляется браузером ВСЕГДА, даже когда сервер шифрование ClientHello не
/// поддерживает: в этом случае Chrome шлёт правдоподобную пустышку, чтобы наличие расширения не
/// выдавало, для каких именно узлов шифрование включено. Отсутствие расширения у клиента, который
/// представляется современным Chrome, — противоречие, заметное в первом же пакете.
///
/// Содержимое повторяет форму настоящего сообщения (draft-ietf-tls-esni): тип «внешнее», пара
/// идентификаторов набора, идентификатор конфигурации, ключ отправителя и полезная нагрузка.
/// Расшифровать её невозможно и не нужно — сервер, не знающий конфигурации, просто её игнорирует,
/// ровно как и настоящую пустышку браузера.
/// </remarks>
public sealed class EncryptedClientHelloTlsExtension : TlsExtension
{
    /// <summary>Длина ключа отправителя для X25519-HKDF-SHA256.</summary>
    private const int SenderKeyLength = 32;

    /// <summary>
    /// Сколько байт тела занимает служебная часть, помимо зашифрованной.
    /// </summary>
    /// <remarks>
    /// Тип, набор шифров, номер настройки, ключ отправителя и две длины. Нужна, чтобы по готовому
    /// расширению восстановить исходную длину зашифрованной части и создать пустышку такого же
    /// размера, но со свежим содержимым.
    /// </remarks>
    public const int GreaseOverhead = 1 + 2 + 2 + 1 + 2 + SenderKeyLength + 2;

    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0xFE0D;

    /// <summary>
    /// Тело расширения.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; } = ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc/>
    public override int Size => 2 + 2 + Data.Length;

    /// <summary>
    /// Создаёт правдоподобную пустышку.
    /// </summary>
    /// <param name="payloadLength">
    /// Длина зашифрованной части. У браузера она следует из размера внутреннего ClientHello и
    /// потому не постоянна; значение по умолчанию соответствует наблюдаемому.
    /// </param>
    /// <returns>Готовое расширение.</returns>
    public static EncryptedClientHelloTlsExtension CreateGrease(int payloadLength = 208)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payloadLength);

        // type(1) + kdf(2) + aead(2) + config_id(1) + enc<2> + payload<2>
        var data = new byte[1 + 2 + 2 + 1 + 2 + SenderKeyLength + 2 + payloadLength];
        var position = 0;

        // Тип «внешнее сообщение»: именно его отправляет клиент в ClientHello.
        data[position++] = 0x00;

        // HKDF-SHA256 и AES-128-GCM — набор, который выбирает Chrome.
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(position, 2), 0x0001);
        position += 2;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(position, 2), 0x0001);
        position += 2;

        Span<byte> configId = stackalloc byte[1];
        RandomNumberGenerator.Fill(configId);
        data[position++] = configId[0];

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(position, 2), SenderKeyLength);
        position += 2;
        RandomNumberGenerator.Fill(data.AsSpan(position, SenderKeyLength));
        position += SenderKeyLength;

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(position, 2), (ushort)payloadLength);
        position += 2;

        // Полезная нагрузка неотличима от шифротекста именно потому, что случайна.
        RandomNumberGenerator.Fill(data.AsSpan(position, payloadLength));

        return new EncryptedClientHelloTlsExtension { Data = data };
    }

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
        Id = 0xFE0D;
        Data = ReadOnlyMemory<byte>.Empty;
        base.Reset();
    }
}
