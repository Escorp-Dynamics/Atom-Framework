using System.Runtime.CompilerServices;
using Atom.Net.Https.Profiles;

namespace Atom.Net.Https;

/// <summary>
/// Представляет базовый адаптер для User-Agent.
/// </summary>
public class UserAgentAdapter : IUserAgentAdapter
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsClientHandler CreateHandler(string userAgent)
    {
        var profile = BrowserProfileResolver.Resolve(userAgent);
        return new HttpsClientHandler
        {
            BrowserProfile = profile,
        };
    }
}