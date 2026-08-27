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
    /// Эфемерный ключ для X25519. Каждый вызов создаёт новый ключ.
    /// </summary>
    /// <remarks>
    /// Опирается на собственную реализацию <see cref="Tls.X25519"/>, а не на платформенный
    /// <see cref="ECDiffieHellman"/>: .NET на ряде систем отказывает в кривой <c>1.3.101.110</c>
    /// с <see cref="PlatformNotSupportedException"/> (проверено на этой машине). Без X25519 состав
    /// ключевых долей в ClientHello отличается от браузерного, поэтому зависеть здесь от платформы
    /// нельзя — иначе мимикрия ломается на ровном месте, причём только на части окружений.
    ///
    /// В <see cref="Ephemeral"/> кладём приватный скаляр (32 байта): для X25519 этого достаточно,
    /// объект ключа не нужен, а лишних аллокаций на горячем пути не возникает.
    /// </remarks>
    public static KeyShare X25519
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var privateKey = Tls.X25519.CreatePrivateKey();
            return new KeyShare(NamedGroup.X25519, Tls.X25519.GetPublicKey(privateKey), privateKey);
        }
    }

    /// <summary>
    /// Эфемерный гибридный ключ X25519 + ML-KEM-768.
    /// </summary>
    /// <remarks>
    /// Основная группа современного Chrome. Доля ключа — конкатенация в порядке
    /// draft-kwiatkowski-tls-ecdhe-mlkem: сначала ключ инкапсуляции ML-KEM-768 (1184 байта), затем
    /// открытый ключ X25519 (32 байта), всего 1216. Порядок обратный привычному «сначала
    /// классика» — перепутав его, получим отказ рукопожатия, а не молчаливое расхождение.
    ///
    /// Доступность проверяется через <see cref="IsMLKemSupported"/>: примитив опирается на
    /// криптографическую библиотеку системы и присутствует не везде.
    /// </remarks>
    public static KeyShare X25519MLKem768
    {
        get
        {
            var kem = MLKem.GenerateKey(MLKemAlgorithm.MLKem768);

            try
            {
                var encapsulationKey = kem.ExportEncapsulationKey();
                var x25519Private = Tls.X25519.CreatePrivateKey();
                var x25519Public = Tls.X25519.GetPublicKey(x25519Private);

                var share = new byte[encapsulationKey.Length + x25519Public.Length];
                encapsulationKey.CopyTo(share.AsSpan());
                x25519Public.CopyTo(share.AsSpan(encapsulationKey.Length));

                return new KeyShare(NamedGroup.X25519MLKem768, share, new HybridKeyMaterial(kem, x25519Private));
            }
            catch
            {
                kem.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Доступен ли примитив ML-KEM на этой платформе.
    /// </summary>
    public static bool IsMLKemSupported => MLKem.IsSupported;

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
            Buffer.BlockCopy(p.Q.X, 0, pub, 1, p.Q.X.Length);
            Buffer.BlockCopy(p.Q.Y, 0, pub, 1 + p.Q.X.Length, p.Q.Y.Length);

            return new KeyShare(NamedGroup.Secp256r1, pub, ecdh);
        }
    }

    /// <summary>
    /// Эфемерный ключ для NIST P‑384 (пригодится позже; опционально включайте в профили).
    /// Каждый вызов создаёт новый ключ.
    /// </summary>
    /// <summary>
    /// Доля ключа на кривой P-521.
    /// </summary>
    /// <remarks>
    /// Нужна не для первого сообщения, а для ответа на HelloRetryRequest: группу P-521 профиль
    /// Firefox объявляет, но долю для неё заранее не отправляет, и сервер, предпочитающий именно
    /// её, попросит повторить приветствие уже с ней.
    /// </remarks>
    public static KeyShare P521
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP521);
            var p = ecdh.ExportParameters(includePrivateParameters: false);
            var pub = new byte[1 + p.Q.X!.Length + p.Q.Y!.Length];
            pub[0] = 0x04;
            Buffer.BlockCopy(p.Q.X, 0, pub, 1, p.Q.X.Length);
            Buffer.BlockCopy(p.Q.Y, 0, pub, 1 + p.Q.X.Length, p.Q.Y.Length);

            return new KeyShare(NamedGroup.Secp521r1, pub, ecdh);
        }
    }

    /// <summary>
    /// Создаёт долю ключа для заданной группы.
    /// </summary>
    /// <param name="group">Группа.</param>
    /// <returns>Доля ключа.</returns>
    /// <remarks>
    /// Конечнополевые группы (ffdhe) намеренно не поддержаны: в открытой сети их не выбирает
    /// никто, а модульное возведение в степень на трёх тысячах бит стоило бы дороже всего
    /// рукопожатия. Отказ здесь внятный — лучше, чем молчаливый обрыв.
    /// </remarks>
    public static KeyShare ForGroup(NamedGroup group) => group switch
    {
        NamedGroup.X25519 => X25519,
        NamedGroup.X25519MLKem768 => X25519MLKem768,
        NamedGroup.Secp256r1 => P256,
        NamedGroup.Secp384r1 => P384,
        NamedGroup.Secp521r1 => P521,
        _ => throw new NotSupportedException($"Доля ключа для группы {group} (0x{(ushort)group:X4}) не поддержана"),
    };

    public static KeyShare P384
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
            var p = ecdh.ExportParameters(includePrivateParameters: false);
            var pub = new byte[1 + p.Q.X!.Length + p.Q.Y!.Length];
            pub[0] = 0x04;
            Buffer.BlockCopy(p.Q.X, 0, pub, 1, p.Q.X.Length);
            Buffer.BlockCopy(p.Q.Y, 0, pub, 1 + p.Q.X.Length, p.Q.Y.Length);

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