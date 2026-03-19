#pragma warning disable CA5398

using System.Runtime.CompilerServices;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Tls;

/// <summary>
/// Представляет профиль TLS, определяющий поведение как у конкретного браузера (JA3/JA4).
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="TlsSettings"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct TlsSettings() : IEquatable<TlsSettings>
{
    /// <summary>
    ///
    /// </summary>
    public SslProtocols MinVersion { get; init; } = SslProtocols.Tls12;

    /// <summary>
    ///
    /// </summary>
    public SslProtocols MaxVersion { get; init; } = SslProtocols.Tls13;

    /// <summary>
    /// Порядок Cipher Suites.
    /// </summary>
    public IEnumerable<CipherSuite> CipherSuites { get; init; } = [];

    /// <summary>
    /// Расширения TLS.
    /// </summary>
    public IEnumerable<ITlsExtension> Extensions { get; init; } = [];

    /// <summary>
    /// Использовать ли онлайн-проверку отзыва сертификата.
    /// </summary>
    public bool CheckCertificateRevocationList { get; init; } = true;

    /// <summary>
    /// Пользовательский callback валидации сертификата сервера.
    /// </summary>
    public Func<X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? ServerCertificateValidationCallback { get; init; }

    /// <summary>
    /// Политика идентификации сессии.
    /// </summary>
    public SessionIdPolicy SessionIdPolicy { get; init; }

    /// <summary>
    /// Пауза после завершения TLS перед отправкой клиентского пролога (H2 preface / H1 запрос).
    /// </summary>
    public TimeSpan Delay { get; init; } = TimeSpan.Zero;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(
        MinVersion.GetHashCode(),
        MaxVersion.GetHashCode(),
        CipherSuites.GetHashCode(),
        Extensions.GetHashCode(),
        CheckCertificateRevocationList.GetHashCode(),
        ServerCertificateValidationCallback?.GetHashCode() ?? 0,
        SessionIdPolicy.GetHashCode(),
        Delay.GetHashCode()
    );

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(TlsSettings other) => MinVersion.Equals(other.MinVersion) && MaxVersion.Equals(other.MaxVersion)
        && CipherSuites.Equals(other.CipherSuites) && Extensions.Equals(other.Extensions)
        && CheckCertificateRevocationList.Equals(other.CheckCertificateRevocationList)
        && Equals(ServerCertificateValidationCallback, other.ServerCertificateValidationCallback)
        && SessionIdPolicy.Equals(other.SessionIdPolicy) && Delay.Equals(other.Delay);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        TlsSettings other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TlsSettings left, TlsSettings right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TlsSettings left, TlsSettings right) => !left.Equals(right);
}