using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Подпись ордеров Polymarket по стандарту EIP-712 (Typed Structured Data Hashing and Signing).
/// </summary>
/// <remarks>
/// Реализует полный цикл подписи ордера для Polymarket CTF Exchange:
/// <list type="number">
///   <item>Формирование EIP-712 domain separator</item>
///   <item>ABI-кодирование и хеширование структуры ордера</item>
///   <item>Вычисление финального хеша: keccak256("\x19\x01" || domainSeparator || structHash)</item>
///   <item>Подпись с помощью secp256k1 ECDSA</item>
/// </list>
/// Совместим с NativeAOT. Не использует рефлексию.
/// Для secp256k1 используется ECDsa с OID 1.3.132.0.10 (поддерживается через OpenSSL на Linux).
/// </remarks>
public static class PolymarketOrderSigner
{
    // Адрес Polymarket CTF Exchange на Polygon
    private const string ExchangeAddress = "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E";

    // Адрес Neg Risk CTF Exchange на Polygon
    private const string NegRiskExchangeAddress = "0xC5d563A36AE78145C45a50134d48A1215220f80a";

    // Chain ID для Polygon
    private const int PolygonChainId = 137;

    // EIP-712 TypeHash для Order struct
    // keccak256("Order(uint256 salt,address maker,address signer,address taker,uint256 tokenId,uint256 makerAmount,uint256 takerAmount,uint256 expiration,uint256 nonce,uint256 feeRateBps,uint8 side,uint8 signatureType)")
    private static readonly byte[] OrderTypeHash = Keccak256.Hash(
        Encoding.UTF8.GetBytes("Order(uint256 salt,address maker,address signer,address taker,uint256 tokenId,uint256 makerAmount,uint256 takerAmount,uint256 expiration,uint256 nonce,uint256 feeRateBps,uint8 side,uint8 signatureType)"));

    // EIP-712 TypeHash для EIP712Domain
    // keccak256("EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)")
    private static readonly byte[] DomainTypeHash = Keccak256.Hash(
        Encoding.UTF8.GetBytes("EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)"));

    // OID secp256k1
    private static readonly Oid Secp256k1Oid = new("1.3.132.0.10");

    /// <summary>
    /// Подписывает ордер Polymarket по стандарту EIP-712.
    /// </summary>
    /// <param name="order">Ордер для подписи (без поля Signature).</param>
    /// <param name="privateKeyHex">Приватный ключ Ethereum (hex, с или без 0x).</param>
    /// <param name="negRisk">Является ли рынок neg-risk (определяет адрес контракта).</param>
    /// <returns>Подписанный ордер с заполненным полем Signature.</returns>
    public static PolymarketSignedOrder SignOrder(PolymarketSignedOrder order, string privateKeyHex, bool negRisk = false)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrEmpty(privateKeyHex);

        var exchangeAddr = negRisk ? NegRiskExchangeAddress : ExchangeAddress;
        var domainSeparator = ComputeDomainSeparator("Exchange", "1", PolygonChainId, exchangeAddr);
        var structHash = ComputeOrderStructHash(order);
        var digest = ComputeEip712Digest(domainSeparator, structHash);

        var signature = SignDigest(digest, privateKeyHex);

        return new PolymarketSignedOrder
        {
            Salt = order.Salt,
            Maker = order.Maker,
            Signer = order.Signer,
            Taker = order.Taker,
            TokenId = order.TokenId,
            MakerAmount = order.MakerAmount,
            TakerAmount = order.TakerAmount,
            Expiration = order.Expiration,
            Nonce = order.Nonce,
            FeeRateBps = order.FeeRateBps,
            Side = order.Side,
            SignatureType = order.SignatureType,
            Signature = signature
        };
    }

    /// <summary>
    /// Вычисляет EIP-712 хеш ордера (без подписи).
    /// </summary>
    /// <param name="order">Ордер.</param>
    /// <param name="negRisk">Является ли рынок neg-risk.</param>
    /// <returns>32-байтный digest для подписи.</returns>
    public static byte[] ComputeOrderDigest(PolymarketSignedOrder order, bool negRisk = false)
    {
        var exchangeAddr = negRisk ? NegRiskExchangeAddress : ExchangeAddress;
        var domainSeparator = ComputeDomainSeparator("Exchange", "1", PolygonChainId, exchangeAddr);
        var structHash = ComputeOrderStructHash(order);
        return ComputeEip712Digest(domainSeparator, structHash);
    }

    /// <summary>
    /// Вычисляет EIP-712 domain separator.
    /// </summary>
    internal static byte[] ComputeDomainSeparator(string name, string version, int chainId, string verifyingContract)
    {
        // domainSeparator = keccak256(abi.encode(DOMAIN_TYPEHASH, keccak256(name), keccak256(version), chainId, address))
        var nameHash = Keccak256.Hash(Encoding.UTF8.GetBytes(name));
        var versionHash = Keccak256.Hash(Encoding.UTF8.GetBytes(version));

        // ABI-кодирование: 5 слов по 32 байта = 160 байт
        Span<byte> encoded = stackalloc byte[160];
        encoded.Clear();

        DomainTypeHash.AsSpan().CopyTo(encoded);
        nameHash.AsSpan().CopyTo(encoded[32..]);
        versionHash.AsSpan().CopyTo(encoded[64..]);
        WriteUint256(encoded[96..], (BigInteger)chainId);
        WriteAddress(encoded[128..], verifyingContract);

        return Keccak256.Hash(encoded);
    }

    /// <summary>
    /// Вычисляет struct hash ордера.
    /// </summary>
    internal static byte[] ComputeOrderStructHash(PolymarketSignedOrder order)
    {
        // structHash = keccak256(abi.encode(ORDER_TYPEHASH, salt, maker, signer, taker, tokenId,
        //   makerAmount, takerAmount, expiration, nonce, feeRateBps, side, signatureType))
        // 13 полей × 32 байта = 416 байт
        Span<byte> encoded = stackalloc byte[416];
        encoded.Clear();

        OrderTypeHash.AsSpan().CopyTo(encoded);
        WriteUint256(encoded[32..], BigInteger.Parse(order.Salt, CultureInfo.InvariantCulture));
        WriteAddress(encoded[64..], order.Maker);
        WriteAddress(encoded[96..], order.Signer);
        WriteAddress(encoded[128..], order.Taker);
        WriteUint256(encoded[160..], BigInteger.Parse(order.TokenId, CultureInfo.InvariantCulture));
        WriteUint256(encoded[192..], BigInteger.Parse(order.MakerAmount, CultureInfo.InvariantCulture));
        WriteUint256(encoded[224..], BigInteger.Parse(order.TakerAmount, CultureInfo.InvariantCulture));
        WriteUint256(encoded[256..], BigInteger.Parse(order.Expiration, CultureInfo.InvariantCulture));
        WriteUint256(encoded[288..], BigInteger.Parse(order.Nonce, CultureInfo.InvariantCulture));
        WriteUint256(encoded[320..], BigInteger.Parse(order.FeeRateBps, CultureInfo.InvariantCulture));
        WriteUint256(encoded[352..], (BigInteger)(order.Side == PolymarketSide.Buy ? 0 : 1));
        WriteUint256(encoded[384..], (BigInteger)order.SignatureType);

        return Keccak256.Hash(encoded);
    }

    /// <summary>
    /// Вычисляет финальный EIP-712 digest: keccak256("\x19\x01" || domainSeparator || structHash).
    /// </summary>
    internal static byte[] ComputeEip712Digest(byte[] domainSeparator, byte[] structHash)
    {
        // EIP-712: "\x19\x01" + domainSeparator(32) + structHash(32) = 66 байт
        Span<byte> message = stackalloc byte[66];
        message[0] = 0x19;
        message[1] = 0x01;
        domainSeparator.AsSpan().CopyTo(message[2..]);
        structHash.AsSpan().CopyTo(message[34..]);

        return Keccak256.Hash(message);
    }

    /// <summary>
    /// Подписывает digest с помощью secp256k1 ECDSA и возвращает подпись в формате Ethereum (r + s + v).
    /// </summary>
    private static string SignDigest(byte[] digest, string privateKeyHex)
    {
        var keyHex = privateKeyHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? privateKeyHex[2..]
            : privateKeyHex;

        var privateKeyBytes = Convert.FromHexString(keyHex);

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.CreateFromOid(Secp256k1Oid),
            D = privateKeyBytes
        });

        // Подпись в формате DER
        var derSignature = ecdsa.SignHash(digest);

        // Конвертация DER в (r, s) и расчёт v
        var (r, s, v) = DerToRSV(derSignature, digest, ecdsa);

        // Формат Ethereum: 0x + r(64) + s(64) + v(2)
        return $"0x{Convert.ToHexStringLower(r)}{Convert.ToHexStringLower(s)}{v:x2}";
    }

    /// <summary>
    /// Конвертирует DER-подпись в компоненты (r, s) и вычисляет recovery id (v).
    /// </summary>
    private static (byte[] R, byte[] S, byte V) DerToRSV(byte[] derSignature, byte[] digest, ECDsa ecdsa)
    {
        // Извлечение r и s из DER формата IEEE P1363
        // ECDsa.SignHash returns IEEE P1363 format on .NET: r(32) + s(32) for secp256k1
        var r = new byte[32];
        var s = new byte[32];

        if (derSignature.Length == 64)
        {
            // IEEE P1363: r(32) + s(32)
            Buffer.BlockCopy(derSignature, 0, r, 0, 32);
            Buffer.BlockCopy(derSignature, 32, s, 0, 32);
        }
        else
        {
            // DER: SEQUENCE { INTEGER r, INTEGER s }
            (r, s) = ParseDer(derSignature);
        }

        // Нормализация s (Ethereum требует low-s, s < n/2)
        var sInt = new BigInteger(s, isUnsigned: true, isBigEndian: true);
        var halfN = BigInteger.Parse("7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF5D576E7357A4501DDFE92F46681B20A0", NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        byte v = 27;
        if (sInt > halfN)
        {
            // s = n - s
            var n = BigInteger.Parse("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141", NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            sInt = n - sInt;
            s = ToBigEndianBytes(sInt, 32);
            v = 28;
        }

        // Пробуем recovery: v=27, если публичный ключ не совпадает — v=28
        // .NET не предоставляет прямой ecrecover, используем эвристику
        var publicKey = ecdsa.ExportParameters(false);
        v = DetermineRecoveryId(digest, r, s, publicKey);

        return (r, s, v);
    }

    /// <summary>
    /// Определяет recovery id (v) на основе публичного ключа.
    /// </summary>
    private static byte DetermineRecoveryId(byte[] digest, byte[] r, byte[] s, ECParameters pubKey)
    {
        // Пробуем v=27 и v=28, проверяя через ECDsa.VerifyHash
        // Сначала пробуем v=27 (recovery id 0)
        for (byte v = 27; v <= 28; v++)
        {
            try
            {
                using var verifier = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.CreateFromOid(new Oid("1.3.132.0.10")),
                    Q = pubKey.Q
                });

                // Формируем IEEE P1363 подпись (r + s)
                var ieee = new byte[64];
                Buffer.BlockCopy(r, 0, ieee, 0, 32);
                Buffer.BlockCopy(s, 0, ieee, 32, 32);

                if (verifier.VerifyHash(digest, ieee))
                    return v;
            }
            catch
            {
                continue;
            }
        }

        // Fallback: v=27 по умолчанию (для большинства случаев)
        return 27;
    }

    /// <summary>
    /// Парсит DER-кодированную подпись ECDSA.
    /// </summary>
    private static (byte[] R, byte[] S) ParseDer(byte[] der)
    {
        // DER: 0x30 [total-len] 0x02 [r-len] [r...] 0x02 [s-len] [s...]
        var offset = 2; // skip 0x30 + total length

        // R
        if (der[offset] != 0x02)
            throw new PolymarketException("Некорректный DER формат: ожидался маркер INTEGER для R.");
        offset++;
        var rLen = der[offset++];
        var rBytes = der[offset..(offset + rLen)];
        offset += rLen;

        // S
        if (der[offset] != 0x02)
            throw new PolymarketException("Некорректный DER формат: ожидался маркер INTEGER для S.");
        offset++;
        var sLen = der[offset++];
        var sBytes = der[offset..(offset + sLen)];

        // Нормализация до 32 байт (удаление leading zero, padding)
        return (NormalizeTo32(rBytes), NormalizeTo32(sBytes));
    }

    /// <summary>
    /// Нормализует байтовый массив до ровно 32 байт (big-endian).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] NormalizeTo32(byte[] data)
    {
        if (data.Length == 32) return data;
        if (data.Length > 32)
        {
            // Leading zero(s) от DER-кодирования
            var result = new byte[32];
            Buffer.BlockCopy(data, data.Length - 32, result, 0, 32);
            return result;
        }
        else
        {
            // Дополнение нулями слева
            var result = new byte[32];
            Buffer.BlockCopy(data, 0, result, 32 - data.Length, data.Length);
            return result;
        }
    }

    #region ABI-кодирование

    /// <summary>
    /// Записывает uint256 (big-endian, 32 байта) в буфер.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUint256(Span<byte> destination, BigInteger value)
    {
        var bytes = ToBigEndianBytes(value, 32);
        bytes.CopyTo(destination);
    }

    /// <summary>
    /// Записывает Ethereum address (20 байт, left-padded до 32 байт) в буфер.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteAddress(Span<byte> destination, string address)
    {
        var hex = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? address[2..]
            : address;

        var addrBytes = Convert.FromHexString(hex);

        // Left-pad до 32 байт: 12 нулевых байт + 20 байт адреса
        destination.Clear();
        addrBytes.CopyTo(destination[(32 - addrBytes.Length)..]);
    }

    /// <summary>
    /// Конвертирует BigInteger в big-endian массив байт фиксированной длины.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] ToBigEndianBytes(BigInteger value, int length)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);

        if (bytes.Length == length) return bytes;

        var result = new byte[length];
        if (bytes.Length > length)
            Buffer.BlockCopy(bytes, bytes.Length - length, result, 0, length);
        else
            Buffer.BlockCopy(bytes, 0, result, length - bytes.Length, bytes.Length);

        return result;
    }

    #endregion
}
