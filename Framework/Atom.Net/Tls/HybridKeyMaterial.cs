using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Секретная часть гибридного ключа X25519 + ML-KEM-768.
/// </summary>
/// <remarks>
/// Обе половины нужны одновременно: общий секрет гибрида — их конкатенация, и потеря любой делает
/// рукопожатие невозможным. Отдельный тип, а не пара значений, — чтобы освобождение примитива
/// ML-KEM не потерялось при передаче через нетипизированное поле эфемерного ключа.
/// </remarks>
/// <param name="kem">Ключ ML-KEM-768, которым расшифровывается инкапсулированный секрет.</param>
/// <param name="x25519PrivateKey">Закрытый скаляр X25519.</param>
public sealed class HybridKeyMaterial(MLKem kem, byte[] x25519PrivateKey) : IDisposable
{
    // Поле объявлено явно: обращение к параметру первичного конструктора из методов анализаторы
    // не считают обращением к данным экземпляра, а освобождение обязано его обнулять.
    private readonly byte[] x25519PrivateKey = x25519PrivateKey;

    /// <summary>Ключ ML-KEM-768.</summary>
    public MLKem Kem { get; } = kem;

    /// <summary>
    /// Расшифровывает инкапсулированный секрет ML-KEM.
    /// </summary>
    /// <param name="ciphertext">Шифротекст из доли ключа сервера.</param>
    /// <param name="destination">Буфер на 32 байта.</param>
    public void Decapsulate(ReadOnlySpan<byte> ciphertext, Span<byte> destination) => Kem.Decapsulate(ciphertext, destination);

    /// <summary>
    /// Выводит классическую половину общего секрета.
    /// </summary>
    /// <param name="peerPublicKey">Открытый ключ X25519 сервера.</param>
    /// <returns>Общий секрет X25519.</returns>
    public byte[] DeriveX25519(ReadOnlySpan<byte> peerPublicKey) => X25519.DeriveSharedSecret(x25519PrivateKey, peerPublicKey);

    /// <inheritdoc/>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(x25519PrivateKey);
        Kem.Dispose();
    }
}
