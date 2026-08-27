using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Предложение возобновления сессии: PSK-идентификатор и материал из билета TLS 1.3.
/// </summary>
/// <remarks>
/// Помещается в <see cref="TlsSettings.PskOffer"/> на ОДНО соединение. PSK билета — секретный
/// материал: предложение нельзя ни кэшировать в самом профиле (он живёт тысячи запросов), ни
/// переиспользовать после успешной ресумпции, пока сервер не выдал новый билет.
///
/// Хэш здесь — хэш набора шифров, с которым был выдан билет: сервер вправе принять PSK только
/// с набором той же хэш-функции, поэтому и binder обязан считаться ею ещё ДО того, как станет
/// известно, что выбрал сервер.
/// </remarks>
public sealed class Tls13PskOffer
{
    /// <summary>Идентификатор PSK — тело билета, как его выдал сервер.</summary>
    public required ReadOnlyMemory<byte> Identity { get; init; }

    /// <summary>PSK, выведенный из resumption master secret и ticket_nonce.</summary>
    public required ReadOnlyMemory<byte> PreSharedKey { get; init; }

    /// <summary>Случайная добавка ticket_age_add из билета.</summary>
    public required uint TicketAgeAdd { get; init; }

    /// <summary>Хэш-функция набора, с которым выдавался билет.</summary>
    public required HashAlgorithmName Hash { get; init; }

    /// <summary>
    /// Внешний PSK (задан вне TLS-сессии): binder метится «ext binder», а не «res binder»
    /// (RFC 8446, §4.2.11.2). Для возобновления по билету сессии остаётся <see langword="false"/>.
    /// </summary>
    public bool External { get; init; }

    /// <summary>Момент выдачи билета (по нашим часам) — от него считается возраст.</summary>
    public DateTimeOffset IssuedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Obfuscated ticket age: возраст билета в миллисекундах, наложенный на добавку сервера
    /// (RFC 8446, §4.2.11.1). Переполнение uint здесь и есть задуманное наложение по модулю 2³².
    /// </summary>
    public uint ObfuscatedTicketAge
    {
        get
        {
            var ageMilliseconds = (long)(DateTimeOffset.UtcNow - IssuedAtUtc).TotalMilliseconds;
            return unchecked((uint)ageMilliseconds + TicketAgeAdd);
        }
    }

    /// <summary>Длина binder'а — совпадает с длиной вывода хэш-функции билета.</summary>
    public int BinderLength => Tls13KeySchedule.GetHashLength(Hash);

    /// <summary>
    /// Сколько 0-RTT данных сервер объявил в билете (расширение early_data в NewSessionTicket);
    /// ноль — early_data в ClientHello не ставится.
    /// </summary>
    public int MaxEarlyData { get; init; }
}
