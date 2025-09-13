using System.Runtime.CompilerServices;

namespace Atom.Net.Https;

/// <summary>
/// Представляет базовый адаптер для User-Agent.
/// </summary>
public class UserAgentAdapter : IUserAgentAdapter
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsClientHandler CreateHandler(string userAgent) => throw new NotImplementedException();   // TODO: Реализовать создание экземпляра хендлера на основе User-Agent.
}