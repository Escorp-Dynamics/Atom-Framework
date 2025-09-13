using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Net.Https.Tls.Extensions;

namespace Atom.Net.Tls;

/// <summary>
/// Представляет валидатор Client Hello для Google Chrome.
/// </summary>
public abstract class ClientHelloValidator : IClientHelloValidator
{
    private static readonly Lazy<ChromeClientHelloValidator> chrome = new(() => new(), true);

    /// <summary>
    /// Google Chrome.
    /// </summary>
    public static ChromeClientHelloValidator Chrome => chrome.Value;

    /// <inheritdoc/>
    public abstract void Validate(TlsSettings settings);

    /// <summary>
    /// Ищет индекс первого вхождения расширения по его id.
    /// </summary>
    /// <param name="extensions">Коллекция расширений.</param>
    /// <param name="id">Идентификатор расширения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static int IndexOf([NotNull] IEnumerable<ITlsExtension> extensions, ushort id)
    {
        var i = 0;

        foreach (var extension in extensions)
        {
            if (Equals(extension.Id, id)) return i;
            ++i;
        }

        return -1;
    }
}