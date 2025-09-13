namespace Atom.Net.Tls;

/// <summary>
/// Минимальный интерфейс AEAD-шифра для record-layer (AES-GCM, ChaCha20-Poly1305).
/// </summary>
public interface IAeadCipher : IDisposable
{
    /// <summary>
    /// Размер тега аутентификации в байтах.
    /// </summary>
    int TagSize { get; }

    /// <summary>
    /// Шифрование пейлоада с AAD. IV/nonce должен быть заранее сформирован без аллокаций.
    /// </summary>
    bool TryEncrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, out int written);

    /// <summary>
    /// Дешифрование пейлоада с AAD.
    /// </summary>
    bool TryDecrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> ciphertext, Span<byte> plaintext, out int written);
}