using System.Runtime.InteropServices;

namespace Atom.Net.Https.Profiles;

/// <summary>
/// Минимальный снимок browser-shaped поведения на уровне request headers.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct BrowserHeaderProfile
{
    public BrowserHeaderProfile() { }

    /// <summary>
    /// Базовый тип запроса, если вызывающий слой не указал контекст явно.
    /// </summary>
    public RequestKind DefaultRequestKind { get; init; } = RequestKind.Fetch;

    /// <summary>
    /// Политика referrer по умолчанию для browser profile.
    /// </summary>
    public ReferrerPolicyMode DefaultReferrerPolicy { get; init; } = ReferrerPolicyMode.StrictOriginWhenCrossOrigin;

    /// <summary>
    /// Сохранять ли браузерный casing имён заголовков для H1.
    /// </summary>
    public bool UseOriginalHeaderCase { get; init; } = true;

    /// <summary>
    /// Сохранять ли профильный порядок заголовков.
    /// </summary>
    public bool UsePreserveHeaderOrder { get; init; } = true;

    /// <summary>
    /// Добавлять ли Connection: keep-alive для H1-профилей.
    /// </summary>
    public bool UseConnectionKeepAlive { get; init; } = true;

    /// <summary>
    /// Разрешить ли cookie crumbling в H2/H3-подобных профилях.
    /// </summary>
    public bool UseCookieCrumbling { get; init; }

    /// <summary>
    /// Нужно ли автоматически формировать client hints.
    /// </summary>
    public bool UseClientHints { get; init; }

    /// <summary>
    /// Нужно ли автоматически эмитить Accept-Encoding.
    /// </summary>
    public bool EmitAcceptEncoding { get; init; } = true;

    /// <summary>
    /// Нужно ли автоматически эмитить Accept-Language.
    /// </summary>
    public bool EmitAcceptLanguage { get; init; } = true;

    /// <summary>
    /// Локаль, от которой строится автоматический Accept-Language, в формате BCP-47
    /// (например <c lang="text">en-US</c>, <c lang="text">ru-RU</c>).
    /// </summary>
    /// <remarks>
    /// Значение строится как <c lang="text">{локаль},{базовый подтег};q={вес}</c>, где вес задаёт семейство:
    /// Firefox — 0.5, Chromium-семейство и Safari — 0.9 (по capture-эталонам Safari тоже
    /// шлёт полную форму с весом). У локали без региона базовый подтег не добавляется.
    /// Явный заголовок от вызывающего слоя всегда сильнее этого дефолта.
    /// </remarks>
    public string AcceptLanguageLocale { get; init; } = "en-US";
}