using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Реализация AES-GCM через BCL (AesGcm).
/// </summary>
public sealed class AesGcmAead : IAeadCipher
{
    private readonly AesGcm aes;

    /// <inheritdoc/>
    public int TagSize { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AesGcmAead"/>.
    /// </summary>
    /// <param name="key"></param>
    public AesGcmAead(ReadOnlySpan<byte> key)
    {
        TagSize = 16;
        aes = new AesGcm(key, TagSize);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="nonce"></param>
    /// <param name="aad"></param>
    /// <param name="plaintext"></param>
    /// <param name="ciphertext"></param>
    /// <param name="written"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEncrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, out int written)
    {
        // ciphertext = data || tag
        var dataLen = plaintext.Length;
        var tagSpan = ciphertext.Slice(dataLen, TagSize);

        try
        {
            aes.Encrypt(nonce, plaintext, ciphertext[..dataLen], tagSpan, aad);
            written = dataLen + TagSize;
            return true;
        }
        catch
        {
            written = 0;
            return default;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="nonce"></param>
    /// <param name="aad"></param>
    /// <param name="ciphertext"></param>
    /// <param name="plaintext"></param>
    /// <param name="written"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDecrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> ciphertext, Span<byte> plaintext, out int written)
    {
        if (ciphertext.Length < TagSize)
        {
            written = 0;
            return default;
        }

        var dataLen = ciphertext.Length - TagSize;
        var tag = ciphertext.Slice(dataLen, TagSize);

        try
        {
            aes.Decrypt(nonce, ciphertext[..dataLen], tag, plaintext[..dataLen], aad);
            written = dataLen;
            return true;
        }
        catch
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
        aes.Dispose();
        GC.SuppressFinalize(this);
    }
}