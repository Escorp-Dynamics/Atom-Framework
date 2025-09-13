namespace Atom.Net.Tls;

/// <summary>
/// Представляет базовый интерфейс для реализации валидаторов Client Hello.
/// </summary>
public interface IClientHelloValidator
{
    /// <summary>
    /// Валидирует настройки TLS перед формированием Client Hello.
    /// </summary>
    /// <param name="settings">Настройки TLS.</param>
    void Validate(TlsSettings settings);
}