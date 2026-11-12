using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver.Protocol.Cdp;

/// <summary>
/// Событие протокола отладки (сообщение без идентификатора команды).
/// </summary>
/// <param name="Method">Имя события, например <c lang="text">Page.loadEventFired</c>.</param>
/// <param name="Params">Параметры события; отсоединены от исходного буфера и безопасны для хранения.</param>
/// <param name="SessionId">Сессия цели, к которой относится событие, либо <see langword="null"/> для событий браузера.</param>
internal sealed record CdpEvent(string Method, JsonElement Params, string? SessionId);
