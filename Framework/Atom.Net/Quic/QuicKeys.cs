using System.Security.Cryptography;
using Atom.Net.Tls;

namespace Atom.Net.Quic;

/// <summary>
/// Комплект ключей одного направления на одном уровне шифрования QUIC.
/// </summary>
/// <remarks>
/// Три ключа, а не один: ключ и вектор инициализации защищают полезную нагрузку пакета, а
/// отдельный ключ защиты заголовка — номер пакета и младшие биты первого байта. Это разные
/// механизмы с разными входами, и путать их нельзя: защита заголовка снимается ДО расшифровки
/// нагрузки, потому что без неё неизвестна даже длина номера пакета.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5358:Review cipher mode usage with cryptography experts", Justification = "RFC 9001 §5.4.3 предписывает для маски защиты заголовка ОДНО блочное преобразование AES; режим ECB здесь означает именно его, а не шифрование данных.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "SCS0013:Potential usage of weak CipherMode", Justification = "См. CA5358: одиночный блок по RFC 9001, а не режим шифрования сообщения.")]
public sealed class QuicKeys : IDisposable
{
    /// <summary>Длина вектора инициализации в QUIC.</summary>
    public const int IvLength = 12;

    private readonly Aes headerProtection;

    private readonly byte[] key;
    private readonly byte[] iv;
    private readonly byte[] headerProtectionKey;
    private readonly byte[] secret;

    /// <summary>
    /// Секрет трафика, из которого выведен комплект.
    /// </summary>
    /// <remarks>
    /// Нужен для смены ключей: следующий комплект выводится из текущего секрета, а не из
    /// исходного материала рукопожатия.
    /// </remarks>
    public ReadOnlyMemory<byte> Secret => secret;

    /// <summary>Шифр для полезной нагрузки.</summary>
    public IAeadCipher Cipher { get; }

    /// <summary>
    /// Выводит комплект ключей из секрета трафика.
    /// </summary>
    /// <param name="hash">Хэш-функция набора шифров.</param>
    /// <param name="secret">Секрет трафика.</param>
    /// <param name="keyLength">Длина ключа AEAD: 16 для AES-128 и ChaCha20, 32 для AES-256.</param>
    /// <param name="useChaCha">Использовать ли ChaCha20-Poly1305 вместо AES-GCM.</param>
    /// <remarks>
    /// Метки <c lang="text">quic key</c>, <c lang="text">quic iv</c> и <c lang="text">quic hp</c> заданы RFC 9001, §5.1 и разворачиваются
    /// той же функцией HKDF-Expand-Label, что и в TLS, — включая префикс <c lang="text">tls13 </c>. Своя
    /// реализация здесь была бы лишней и разошлась бы с TLS при первой же правке.
    /// </remarks>
    public QuicKeys(HashAlgorithmName hash, ReadOnlySpan<byte> secret, int keyLength, bool useChaCha)
    {
        this.secret = secret.ToArray();
        key = Tls13KeySchedule.ExpandLabel(hash, secret, "quic key", [], keyLength);
        iv = Tls13KeySchedule.ExpandLabel(hash, secret, "quic iv", [], IvLength);
        headerProtectionKey = Tls13KeySchedule.ExpandLabel(hash, secret, "quic hp", [], keyLength);

        Cipher = useChaCha ? new ChaCha20Poly1305Aead(key) : new AesGcmAead(key);

        // Защита заголовка у наборов AES — это ECB по одному блоку выборки. Режим ECB здесь не
        // «слабое шифрование», а именно то, что предписывает RFC 9001 §5.4.3: одиночное
        // блочное преобразование для получения маски.
        headerProtection = Aes.Create();
        headerProtection.Mode = CipherMode.ECB;
        headerProtection.Padding = PaddingMode.None;
        headerProtection.Key = headerProtectionKey;
    }

    /// <summary>
    /// Вычисляет маску защиты заголовка по выборке из зашифрованной нагрузки.
    /// </summary>
    /// <param name="sample">Ровно 16 байт выборки.</param>
    /// <param name="mask">Буфер не менее пяти байт.</param>
    public void ComputeHeaderMask(ReadOnlySpan<byte> sample, Span<byte> mask)
    {
        Span<byte> block = stackalloc byte[16];
        headerProtection.EncryptEcb(sample[..16], block, PaddingMode.None);
        block[..5].CopyTo(mask);
    }

    /// <summary>
    /// Строит nonce для пакета с указанным номером.
    /// </summary>
    /// <param name="packetNumber">Полный номер пакета.</param>
    /// <param name="destination">Буфер длиной <see cref="IvLength"/>.</param>
    /// <remarks>
    /// Номер дополняется нулями слева до длины вектора инициализации и складывается с ним по
    /// модулю два — RFC 9001, §5.3. Тот же приём, что у ChaCha20 в TLS 1.2.
    /// </remarks>
    public void BuildNonce(ulong packetNumber, Span<byte> destination)
    {
        iv.CopyTo(destination);

        unchecked
        {
            for (var index = 0; index < 8; index++)
                destination[IvLength - 1 - index] ^= (byte)(packetNumber >> (index * 8));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Cipher.Dispose();
        headerProtection.Dispose();

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(iv);
        CryptographicOperations.ZeroMemory(headerProtectionKey);
        CryptographicOperations.ZeroMemory(secret);
    }
}
