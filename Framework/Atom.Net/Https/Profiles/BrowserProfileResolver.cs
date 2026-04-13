namespace Atom.Net.Https.Profiles;

/// <summary>
/// Генерирует browser profile по строке User-Agent.
/// </summary>
public static class BrowserProfileResolver
{
    public static BrowserProfile Resolve(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return BrowserProfileCatalog.CreateChromeDesktopLinux();
        }

        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserProfileCatalog.CreateEdgeDesktopWindows();
        }

        if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserProfileCatalog.CreateFirefoxDesktop();
        }

        if (userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase)
            && userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase)
            && !userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)
            && !userAgent.Contains("Chromium/", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserProfileCatalog.CreateSafariDesktopMacOs();
        }

        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserProfileCatalog.CreateChromeDesktopWindows();
        }

        return BrowserProfileCatalog.CreateChromeDesktopLinux();
    }
}