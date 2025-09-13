namespace Atom.Net.Https;

/// <summary>
/// Представляет базовый интерфейс для реализации адаптеров под User-Agent.
/// </summary>
public interface IUserAgentAdapter
{
    /// <summary>
    /// Создаёт новый экземпляр <see cref="HttpsClientHandler"/>.
    /// </summary>
    /// <param name="userAgent">User-Agent.</param>
    HttpsClientHandler CreateHandler(string userAgent);
}