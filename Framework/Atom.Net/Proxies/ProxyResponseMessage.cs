using System.Net;
using System.Runtime.CompilerServices;
using Atom.Net.Https;

namespace Atom.Net.Proxies;

/// <summary>
/// Представляет расширенную версию <see cref="HttpsResponseMessage"/> для валидации прокси.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ProxyResponseMessage"/>.
/// </remarks>
/// <param name="statusCode">Код статуса ответа.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public class ProxyResponseMessage(HttpStatusCode statusCode) : HttpsResponseMessage(statusCode)
{
    /// <summary>
    /// Определяет, валидны ли прокси по скорости.
    /// </summary>
    public bool IsSpeedValid { get; set; }

    /// <summary>
    /// Определяет, валидны ли прокси по коду статуса ответа.
    /// </summary>
    public bool IsStatusCodeValid { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ProxyResponseMessage(HttpsResponseMessage message, bool isSpeedValid, bool statusCodeValid) : this()
    {
        StatusCode = message.StatusCode;
        ReasonPhrase = message.ReasonPhrase;
        Content = message.Content;
        Version = message.Version;
        RequestMessage = message.RequestMessage;
        Duration = message.Duration;
        Exception = message.Exception;
        Traffic = message.Traffic;
        IsSpeedValid = isSpeedValid;
        IsStatusCodeValid = statusCodeValid;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ProxyResponseMessage"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ProxyResponseMessage() : this(default) { }
}