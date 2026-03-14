using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Генерирует HMAC-SHA256 подписи для аутентификации запросов к REST API Polymarket CLOB.
/// </summary>
/// <remarks>
/// Совместим с NativeAOT. Не использует рефлексию.
/// Формат подписи: Base64(HMAC-SHA256(Base64Decode(secret), message))
/// где message = timestamp + "\n" + nonce + "\n" + METHOD + "\n" + path [+ "\n" + body]
/// </remarks>
public static class PolymarketApiSigner
{
    /// <summary>
    /// Создаёт HMAC-SHA256 подпись запроса для аутентификации в Polymarket CLOB API.
    /// </summary>
    /// <param name="apiSecret">Секретный ключ (Base64-кодированный).</param>
    /// <param name="timestamp">UNIX-timestamp запроса (секунды).</param>
    /// <param name="nonce">Уникальный nonce запроса.</param>
    /// <param name="method">HTTP-метод (GET, POST, DELETE).</param>
    /// <param name="requestPath">Путь запроса (например, "/markets").</param>
    /// <param name="body">Тело запроса (для POST/PUT). Null для GET/DELETE без тела.</param>
    /// <returns>Base64-кодированная подпись.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Sign(
        string apiSecret,
        string timestamp,
        string nonce,
        string method,
        string requestPath,
        string? body = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiSecret);
        ArgumentException.ThrowIfNullOrEmpty(timestamp);
        ArgumentException.ThrowIfNullOrEmpty(nonce);
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentException.ThrowIfNullOrEmpty(requestPath);

        // Формирование сообщения для подписи
        var message = string.IsNullOrEmpty(body)
            ? $"{timestamp}\n{nonce}\n{method.ToUpperInvariant()}\n{requestPath}"
            : $"{timestamp}\n{nonce}\n{method.ToUpperInvariant()}\n{requestPath}\n{body}";

        // Декодирование секрета из Base64
        var secretBytes = Convert.FromBase64String(apiSecret);

        // Вычисление HMAC-SHA256
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hashBytes = HMACSHA256.HashData(secretBytes, messageBytes);

        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Генерирует текущий UNIX-timestamp в секундах.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetTimestamp() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Генерирует криптографически безопасный nonce.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GenerateNonce()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }
}
