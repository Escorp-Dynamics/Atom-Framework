using System;

namespace Atom.Net.Browsing.Profiles;

/// <summary>
/// Представляет профиль браузера.
/// </summary>
public sealed class WebBrowserProfile
{
    private HardwareProfile _hardware;
    private UserAgent _userAgent;

    public WebBrowserProfile()
    {
        _hardware = new HardwareProfile();
        _userAgent = UserAgent.Empty;
    }

    public WebBrowserProfile(UserAgent userAgent, HardwareProfile hardware)
    {
        _userAgent = userAgent ?? UserAgent.Empty;
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
    }

    /// <summary>
    /// Профиль оборудования.
    /// </summary>
    public HardwareProfile Hardware
    {
        get => _hardware;
        set => _hardware = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Деконструированный User-Agent.
    /// </summary>
    public UserAgent UserAgent
    {
        get => _userAgent;
        set => _userAgent = value ?? UserAgent.Empty;
    }

    /// <summary>
    /// Строковое представление User-Agent.
    /// </summary>
    public string UserAgentString
    {
        get => _userAgent.ToString();
        set => _userAgent = UserAgent.Parse(value);
    }

    /// <summary>
    /// Загружает User-Agent из строки.
    /// </summary>
    /// <param name="value">Строка User-Agent.</param>
    public void LoadUserAgent(string? value) => _userAgent = UserAgent.Parse(value);

    /// <inheritdoc/>
    public override string ToString() => _userAgent.ToString();
}
