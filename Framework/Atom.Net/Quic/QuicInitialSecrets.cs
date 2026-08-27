using System.Security.Cryptography;
using Atom.Net.Tls;

namespace Atom.Net.Quic;

/// <summary>
/// Начальные секреты QUIC, выводимые из идентификатора соединения (RFC 9001, §5.2).
/// </summary>
/// <remarks>
/// Пакеты уровня Initial шифруются ключами, которые обе стороны выводят из ОБЩЕДОСТУПНОГО
/// значения — идентификатора соединения, выбранного клиентом, — и фиксированной соли версии.
/// Секретности это не даёт и не должно: смысл в том, чтобы посредник, не понимающий QUIC, не мог
/// читать и изменять служебные поля, а обе стороны договорились о формате до всякого обмена
/// ключами.
///
/// Соль привязана к ВЕРСИИ протокола. Взяв чужую, получим пакеты, которые сервер не расшифрует, —
/// и выглядеть это будет как молчание сети.
/// </remarks>
public static class QuicInitialSecrets
{
    /// <summary>Соль для QUIC v1 (RFC 9001, §5.2).</summary>
    private static readonly byte[] InitialSaltV1 =
    [
        0x38, 0x76, 0x2c, 0xf7, 0xf5, 0x59, 0x34, 0xb3, 0x4d, 0x17,
        0x9a, 0xe6, 0xa4, 0xc8, 0x0c, 0xad, 0xcc, 0xbb, 0x7f, 0x0a,
    ];

    /// <summary>
    /// Выводит пару комплектов ключей уровня Initial.
    /// </summary>
    /// <param name="version">Версия QUIC.</param>
    /// <param name="destinationConnectionId">Идентификатор соединения, выбранный клиентом для первого пакета.</param>
    /// <returns>Ключи клиента и сервера.</returns>
    /// <remarks>
    /// Идентификатор берётся именно из ПЕРВОГО пакета клиента и не меняется, даже когда сервер
    /// позже назначит свой: иначе стороны разойдутся в ключах ровно посреди рукопожатия.
    /// </remarks>
    public static (QuicKeys Client, QuicKeys Server) Derive(QuicVersion version, ReadOnlySpan<byte> destinationConnectionId)
    {
        if (version is not QuicVersion.V1) throw new NotSupportedException($"Версия QUIC {(uint)version:X8} не поддержана");

        var initial = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, destinationConnectionId, InitialSaltV1, initial);

        var clientSecret = Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, initial, "client in", [], 32);
        var serverSecret = Tls13KeySchedule.ExpandLabel(HashAlgorithmName.SHA256, initial, "server in", [], 32);

        try
        {
            // Уровень Initial всегда работает на AES-128-GCM независимо от того, какой набор
            // будет согласован позже: набор ещё не выбран, а ключи уже нужны.
            return (new QuicKeys(HashAlgorithmName.SHA256, clientSecret, keyLength: 16, useChaCha: false),
                    new QuicKeys(HashAlgorithmName.SHA256, serverSecret, keyLength: 16, useChaCha: false));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(initial);
            CryptographicOperations.ZeroMemory(clientSecret);
            CryptographicOperations.ZeroMemory(serverSecret);
        }
    }
}
