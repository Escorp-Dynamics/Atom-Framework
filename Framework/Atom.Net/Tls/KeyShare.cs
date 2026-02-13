using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Элемент key_share: группа и публичный ключ клиента для неё.
/// </summary>
public class KeyShare
{
    /// <summary>
    /// Идентификатор группы (named group).
    /// </summary>
    public NamedGroup Group { get; init; }

    /// <summary>
    /// Публичный ключ для KeyShare в формате, требуемом конкретной группой:
    /// - P‑256/P‑384: ANSI X9.62 uncompressed: 0x04 | X(32/48) | Y(32/48)
    /// - X25519: 32 байта
    /// </summary>
    public ReadOnlyMemory<byte> PublicKey { get; init; }

    /// <summary>
    /// Эфемерный приватный материал, необходимый для вычисления секрета
    /// после получения ServerHello (ECDH и т.п.). На уровне ClientHello это не требуется,
    /// но мы сохраняем объект для последующей фазы handshake.
    /// Для P‑256/P‑384/X25519 — это ECDiffieHellman.
    /// </summary>
    public object? Ephemeral { get; }

    /// <summary>
    /// Эфемерный ключ для X25519. Требует поддержки платформой ECDH X25519.
    /// Каждый вызов создаёт новый ключ.
    /// </summary>
    public static KeyShare X25519
    {
        get
        {
            // Некоторые платформы .NET 9 позволяют FriendlyName("X25519")
            try
            {
                var curve = ECCurve.CreateFromFriendlyName("X25519");
                var ecdh = ECDiffieHellman.Create(curve);
                var p = ecdh.ExportParameters(includePrivateParameters: false);

                if (p.Q.X is { Length: 32 })
                {
                    var pub = new byte[32];
                    Buffer.BlockCopy(p.Q.X, 0, pub, 0, 32);
                    return new KeyShare(NamedGroup.X25519, pub, ecdh);
                }

                ecdh.Dispose();
            }
            catch
            {
                // Игнор — упадём ниже с NotSupportedException
            }

            throw new NotSupportedException("X25519 недоступен на текущей платформе .NET/OS");
        }
    }

    /// <summary>
    /// Эфемерный ключ для NIST P‑256. Гарантированно доступен на .NET 9.
    /// Каждый вызов создаёт новый ключ.
    /// </summary>
    public static KeyShare P256
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var p = ecdh.ExportParameters(includePrivateParameters: false);
            // Формируем ANSI X9.62 uncompressed: 0x04 | X(32) | Y(32)
            var pub = new byte[1 + p.Q.X!.Length + p.Q.Y!.Length];
            pub[0] = 0x04;
            Buffer.BlockCopy(p.Q.X!, 0, pub, 1, p.Q.X!.Length);
            Buffer.BlockCopy(p.Q.Y!, 0, pub, 1 + p.Q.X!.Length, p.Q.Y!.Length);

            return new KeyShare(NamedGroup.Secp256r1, pub, ecdh);
        }
    }

    /// <summary>
    /// Эфемерный ключ для NIST P‑384 (пригодится позже; опционально включайте в профили).
    /// Каждый вызов создаёт новый ключ.
    /// </summary>
    public static KeyShare P384
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
            var p = ecdh.ExportParameters(includePrivateParameters: false);
            var pub = new byte[1 + p.Q.X!.Length + p.Q.Y!.Length];
            pub[0] = 0x04;
            Buffer.BlockCopy(p.Q.X!, 0, pub, 1, p.Q.X!.Length);
            Buffer.BlockCopy(p.Q.Y!, 0, pub, 1 + p.Q.X!.Length, p.Q.Y!.Length);

            return new KeyShare(NamedGroup.Secp384r1, pub, ecdh);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private KeyShare(NamedGroup group, ReadOnlyMemory<byte> publicKey, object? ephemeral)
    {
        Group = group;
        PublicKey = publicKey;
        Ephemeral = ephemeral;
    }
}