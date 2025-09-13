using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers;
using Atom.Net.Tcp;
using Atom.Net.Tls;

namespace Atom.Net.Https;

/// <summary>
/// Представляет настройки HTTP/1.1.
/// </summary>
public readonly struct Http11Settings : IEquatable<Http11Settings>
{
    /// <summary>
    /// Политика форматирования заголовков.
    /// </summary>
    public IHeadersFormattingPolicy? HeadersFormattingPolicy { get; init; }

    /// <summary>
    /// Сохранять оригинальный регистр имён (Chrome-стиль Title-Case).
    /// </summary>
    public bool UseOriginalHeaderCase { get; init; }

    /// <summary>
    /// Сохранять порядок добавления (без переупорядочивания).
    /// </summary>
    public bool UsePreserveHeaderOrder { get; init; }

    /// <summary>
    /// Добавлять "Connection: keep-alive" (как делает Chrome при H1).
    /// </summary>
    public bool UseConnectionKeepAlive { get; init; }

    /// <summary>
    /// Запрещать старое складывание (folding). Должно быть false.
    /// </summary>
    public bool UseHeaderFolding { get; init; }

    /// <summary>
    /// Небольшой джиттер перед первой байтовой активностью H1.
    /// </summary>
    public TimeSpan PrefaceDelay { get; init; }

    /// <summary>
    /// Настройки TLS.
    /// </summary>
    public TlsSettings Tls { get; init; }

    /// <summary>
    /// Настройки TCP.
    /// </summary>
    public TcpSettings Tcp { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(HeadersFormattingPolicy?.GetHashCode());
        hash.Add(UseOriginalHeaderCase.GetHashCode());
        hash.Add(UsePreserveHeaderOrder.GetHashCode());
        hash.Add(UseConnectionKeepAlive.GetHashCode());
        hash.Add(UseHeaderFolding.GetHashCode());
        hash.Add(PrefaceDelay.GetHashCode());
        hash.Add(Tls.GetHashCode());
        hash.Add(Tcp.GetHashCode());

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Http11Settings other) => ((HeadersFormattingPolicy is null && other.HeadersFormattingPolicy is null) || (HeadersFormattingPolicy is not null && other.HeadersFormattingPolicy is not null && HeadersFormattingPolicy.Equals(other.HeadersFormattingPolicy))) && UseOriginalHeaderCase.Equals(other.UseOriginalHeaderCase)
        && UsePreserveHeaderOrder.Equals(other.UsePreserveHeaderOrder) && UseConnectionKeepAlive.Equals(other.UseConnectionKeepAlive)
        && UseHeaderFolding.Equals(other.UseHeaderFolding) && PrefaceDelay.Equals(other.PrefaceDelay)
        && Tls.Equals(other.Tls) && Tcp.Equals(other.Tcp);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        Http11Settings other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Http11Settings left, Http11Settings right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Http11Settings left, Http11Settings right) => !(left == right);
}