using System.Net;
using System.Text.Json;
using Atom.Net.Browsing.WebDriver.Protocol;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

internal interface IPageTransport
{
    Uri? CurrentUrl { get; }

    string? CurrentTitle { get; }

    string? CurrentContent { get; }

    HttpsResponseMessage Navigate(Uri url, NavigationSettings settings);

    HttpsResponseMessage Reload(Uri fallbackUrl, NavigationSettings? settings = null);

    IReadOnlyList<Cookie> GetAllCookies();

    void SetCookies(IEnumerable<Cookie> cookies);

    void ClearAllCookies();

    JsonElement? Evaluate(string script);

    void SubscribeCallback(string callbackPath);

    void UnSubscribeCallback(string callbackPath);

    bool TryDequeueEvent(out BridgeMessage? message);
}