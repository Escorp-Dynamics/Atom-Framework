namespace Atom.Net.Tls;

/// <summary>
/// Сервер согласовал TLS 1.2 в ответ на предложение TLS 1.3.
/// </summary>
/// <remarks>
/// Не ошибка, а развилка: версию мы выбираем ДО отправки ClientHello — по потолку профиля, — а
/// решает её сервер. Серверов, не умеющих TLS 1.3, в сети по-прежнему достаточно, и такой ответ
/// совершенно нормален.
///
/// Отдельный тип нужен затем, чтобы отличить эту развилку от настоящей неполадки. Прежде она
/// приходила как «набор шифров 0xC02F не поддержан в TLS 1.3» — сообщение, по которому не
/// догадаться ни о причине, ни о том, что делать: разбор ServerHello шёл по правилам TLS 1.3,
/// натыкался на набор шифров из TLS 1.2 и падал.
/// </remarks>
public sealed class TlsVersionDowngradeException : InvalidOperationException
{
    /// <summary>
    /// Создаёт исключение.
    /// </summary>
    public TlsVersionDowngradeException()
        : base("Сервер согласовал TLS 1.2 вместо предложенного TLS 1.3") { }

    /// <summary>
    /// Создаёт исключение с заданным сообщением.
    /// </summary>
    /// <param name="message">Сообщение.</param>
    public TlsVersionDowngradeException(string message) : base(message) { }

    /// <summary>
    /// Создаёт исключение с сообщением и причиной.
    /// </summary>
    /// <param name="message">Сообщение.</param>
    /// <param name="innerException">Причина.</param>
    public TlsVersionDowngradeException(string message, Exception innerException) : base(message, innerException) { }
}
