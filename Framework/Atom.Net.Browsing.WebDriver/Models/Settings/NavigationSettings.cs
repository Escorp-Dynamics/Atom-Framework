using System.Net;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает настройки навигации страницы.
/// </summary>
public sealed class NavigationSettings
{
    /// <summary>
    /// Получает тип навигационного действия.
    /// </summary>
    public NavigationKind Kind { get; init; } = NavigationKind.Default;

    /// <summary>
    /// Получает дополнительные HTTP-заголовки навигации.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Получает прокси, используемый для навигации.
    /// </summary>
    public IWebProxy? Proxy { get; init; }

    /// <summary>
    /// Получает бинарное тело запроса для навигации.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// Получает HTML-содержимое для навигации.
    /// </summary>
    public string? Html { get; init; }
}