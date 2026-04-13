using System.Net;
using System.Runtime.InteropServices;
using Atom.Net.Tcp;
using Atom.Net.Tls;

namespace Atom.Net.Https.Profiles;

/// <summary>
/// Единый immutable snapshot браузерного профиля для orchestration transport и header слоёв.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct BrowserProfile
{
    public BrowserProfile() { }

    /// <summary>
    /// Человекочитаемое имя профиля.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Строка User-Agent для этого профиля.
    /// </summary>
    public required string UserAgent { get; init; }

    /// <summary>
    /// Предпочитаемая версия HTTP для стартового запроса.
    /// </summary>
    public Version PreferredHttpVersion { get; init; } = HttpVersion.Version11;

    /// <summary>
    /// Политика согласования версии HTTP.
    /// </summary>
    public HttpVersionPolicy VersionPolicy { get; init; } = HttpVersionPolicy.RequestVersionOrLower;

    /// <summary>
    /// Настройки TCP-поведения профиля.
    /// </summary>
    public TcpSettings Tcp { get; init; }

    /// <summary>
    /// Настройки TLS-поведения профиля.
    /// </summary>
    public TlsSettings Tls { get; init; }

    /// <summary>
    /// Минимальный snapshot browser-shaped header поведения.
    /// </summary>
    public BrowserHeaderProfile Headers { get; init; }
}