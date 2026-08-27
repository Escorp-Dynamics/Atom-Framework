using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Реализация ChaCha20-Poly1305 (RFC 8439) поверх платформенного примитива.
/// </summary>
/// <remarks>
/// Набор нужен не для галочки: его предлагают все современные браузеры, и на машинах без
/// аппаратного AES сервер выбирает именно его. Клиент, который объявляет ChaCha20 в ClientHello,
/// но обрывает соединение, когда сервер его выбирает, ведёт себя так, как ни один браузер себя не
/// ведёт, — и вычисляется по одному этому.
///
/// В отличие от AES-GCM в TLS 1.2, здесь НЕТ явного вектора инициализации в записи: nonce
/// получается сложением по модулю два фиксированного IV со счётчиком записей, как в TLS 1.3.
/// Разница учитывается на уровне потока, а не здесь.
/// </remarks>
public sealed class ChaCha20Poly1305Aead : IAeadCipher
{
    private readonly ChaCha20Poly1305 cipher;

    /// <inheritdoc/>
    public int TagSize { get; }

    /// <summary>
    /// Доступен ли примитив на этой платформе.
    /// </summary>
    /// <remarks>
    /// Платформенная реализация опирается на криптографическую библиотеку системы и на части
    /// сборок может отсутствовать. Проверять это заранее дешевле, чем ловить исключение уже после
    /// согласования набора, когда откатиться на другой уже нельзя.
    /// </remarks>
    public static bool IsSupported => ChaCha20Poly1305.IsSupported;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ChaCha20Poly1305Aead"/>.
    /// </summary>
    /// <param name="key">Ключ длиной 32 байта.</param>
    public ChaCha20Poly1305Aead(ReadOnlySpan<byte> key)
    {
        TagSize = 16;
        cipher = new ChaCha20Poly1305(key);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEncrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, out int written)
    {
        var dataLength = plaintext.Length;

        if (ciphertext.Length < dataLength + TagSize)
        {
            written = 0;
            return default;
        }

        try
        {
            cipher.Encrypt(nonce, plaintext, ciphertext[..dataLength], ciphertext.Slice(dataLength, TagSize), aad);
            written = dataLength + TagSize;
            return true;
        }
        catch (CryptographicException)
        {
            written = 0;
            return default;
        }
        catch (ArgumentException)
        {
            written = 0;
            return default;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDecrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> ciphertext, Span<byte> plaintext, out int written)
    {
        if (ciphertext.Length < TagSize)
        {
            written = 0;
            return default;
        }

        var dataLength = ciphertext.Length - TagSize;

        try
        {
            cipher.Decrypt(nonce, ciphertext[..dataLength], ciphertext.Slice(dataLength, TagSize), plaintext[..dataLength], aad);
            written = dataLength;
            return true;
        }
        catch (CryptographicException)
        {
            // Неверный тег — это либо повреждение, либо подделка. Обе причины обрабатываются
            // вызывающей стороной одинаково: запись отвергается.
            written = 0;
            return default;
        }
        catch (ArgumentException)
        {
            written = 0;
            return default;
        }
    }

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        cipher.Dispose();
        GC.SuppressFinalize(this);
    }
}
