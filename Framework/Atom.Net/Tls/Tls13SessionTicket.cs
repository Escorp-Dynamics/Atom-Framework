using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Билет возобновления сессии TLS 1.3, полученный в NewSessionTicket (RFC 8446, §4.6.1).
/// </summary>
/// <remarks>
/// Хранится на уровне обработчика по имени узла и превращается в <see cref="Tls13PskOffer"/>
/// при следующем соединении с тем же узлом. Билет без PSK бесполезен, поэтому PSK выводится
/// сразу при получении, пока resumption master secret жив в памяти рукопожатия.
/// </remarks>
public sealed class Tls13SessionTicket
{
    /// <summary>Тело билета — идентификатор, который вернётся серверу при ресумпции.</summary>
    public required ReadOnlyMemory<byte> Identity { get; init; }

    /// <summary>PSK, выведенный из resumption master secret и ticket_nonce этого билета.</summary>
    public required ReadOnlyMemory<byte> PreSharedKey { get; init; }

    /// <summary>Случайная добавка ticket_age_add, которой сервер маскирует возраст.</summary>
    public required uint TicketAgeAdd { get; init; }

    /// <summary>Хэш-функция набора, с которым выдавался билет.</summary>
    public HashAlgorithmName Hash { get; init; }

    /// <summary>Заявленный срок действия билета.</summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromSeconds(60 * 60 * 24 * 7);

    /// <summary>Момент получения билета.</summary>
    public DateTimeOffset IssuedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Сколько 0-RTT данных сервер готов принять по этому билету; ноль — early data не предлагать.
    /// </summary>
    public int MaxEarlyData { get; init; }

    /// <summary>Истёк ли билет по заявленному сервером сроку.</summary>
    public bool IsExpired => DateTimeOffset.UtcNow - IssuedAtUtc > Lifetime;

    /// <summary>
    /// Превращает билет в предложение для следующего рукопожатия.
    /// </summary>
    /// <returns>Предложение PSK для <see cref="TlsSettings.PskOffer"/>.</returns>
    public Tls13PskOffer ToOffer() => new()
    {
        Identity = Identity,
        PreSharedKey = PreSharedKey,
        TicketAgeAdd = TicketAgeAdd,
        Hash = Hash,
        IssuedAtUtc = IssuedAtUtc,
        MaxEarlyData = MaxEarlyData,
    };
}
