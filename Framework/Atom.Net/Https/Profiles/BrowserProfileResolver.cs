namespace Atom.Net.Https.Profiles;

/// <summary>
/// Подбирает профиль браузера по строке агента пользователя.
/// </summary>
/// <remarks>
/// Способ выбора здесь ровно один — разбор строки агента, — и он не так прост, как кажется:
/// порядок проверок важнее их содержания. Каждый браузер на движке Chromium несёт в строке и
/// <c>Chrome/</c>, и <c>Safari/</c>, поэтому проверять надо от частного к общему: Edge раньше
/// Chrome, Chrome раньше Safari, иначе всё сведётся к одному профилю.
///
/// ★ Два прежних промаха, оба молчаливых. Во-первых, выдавались профили ЭПОХИ TLS 1.2 — те, что
/// написаны по памяти и содержат девять расширений, — тогда как рядом лежали снятые с настоящих
/// браузеров и совпадающие с ними побайтно. Отпечаток при этом оставался рабочим, просто чужим.
/// Во-вторых, мобильных строк агента резолвер не знал вовсе: телефон получал настольный профиль,
/// то есть заявлял Android, а вёл себя как настольная система.
/// </remarks>
public static class BrowserProfileResolver
{
    /// <summary>
    /// Подбирает профиль по строке агента.
    /// </summary>
    /// <param name="userAgent">Строка агента; пустая означает выбор по умолчанию.</param>
    /// <returns>Профиль браузера.</returns>
    public static BrowserProfile Resolve(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return BrowserProfileCatalog.CreateChromeDesktopLinuxTls13();

        // На iOS собственный движок сторонним браузерам запрещён, поэтому там ВСЕ браузеры — это
        // WebKit, и транспортный отпечаток у них общий; различается только строка агента.
        if (Contains(userAgent, "iPhone") || Contains(userAgent, "iPad"))
        {
            return Contains(userAgent, "CriOS/") || Contains(userAgent, "EdgiOS/") || Contains(userAgent, "FxiOS/")
                ? BrowserProfileCatalog.CreateChromeIos()
                : BrowserProfileCatalog.CreateSafariIos();
        }

        if (Contains(userAgent, "Android"))
        {
            if (Contains(userAgent, "Firefox/")) return BrowserProfileCatalog.CreateFirefoxAndroid();
            if (Contains(userAgent, "EdgA/")) return BrowserProfileCatalog.CreateEdgeAndroid();
            if (Contains(userAgent, "SamsungBrowser/")) return BrowserProfileCatalog.CreateSamsungInternet();

            return BrowserProfileCatalog.CreateChromeAndroid();
        }

        if (Contains(userAgent, "Edg/")) return BrowserProfileCatalog.CreateEdgeDesktopWindowsTls13();
        if (Contains(userAgent, "Firefox/")) return BrowserProfileCatalog.CreateFirefoxDesktop();

        // Safari отличается от движков Chromium ОТСУТСТВИЕМ их признаков: собственного маркера,
        // по которому его можно узнать положительно, в строке нет.
        if (Contains(userAgent, "Safari/")
            && Contains(userAgent, "Mac OS X")
            && !Contains(userAgent, "Chrome/")
            && !Contains(userAgent, "Chromium/"))
        {
            return BrowserProfileCatalog.CreateSafariDesktopMacOs();
        }

        return Contains(userAgent, "Windows")
            ? BrowserProfileCatalog.CreateChromeDesktopWindowsTls13()
            : BrowserProfileCatalog.CreateChromeDesktopLinuxTls13();
    }

    private static bool Contains(string userAgent, string token)
        => userAgent.Contains(token, StringComparison.OrdinalIgnoreCase);
}
