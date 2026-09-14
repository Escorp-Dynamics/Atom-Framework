#pragma warning disable CA5398

using System.Net.Security;
using System.Runtime.CompilerServices;
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
    /// Клиентский сертификат для взаимной аутентификации (RFC 8446 §4.4.2); приватный ключ
    /// обязан присутствовать. Отправляется ТОЛЬКО когда сервер запросил клиентский сертификат.
    /// </summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? ClientCertificate { get; init; }

    /// <summary>
    /// Предложение возобновления сессии TLS 1.3; <see langword="null"/> — полное рукопожатие.
    /// </summary>
    /// <remarks>
    /// Наличие предложения добавляет в ClientHello расширения psk_key_exchange_modes и
    /// замыкающее pre_shared_key. Сервер, отвергший билет, ответит без pre_shared_key в
    /// ServerHello, и рукопожатие продолжится как полное. Предложение действует на одно
    /// соединение: материал билета секретен, и в профиле ему делать нечего.
    /// </remarks>
    public Tls13PskOffer? PskOffer { get; init; }

    /// <summary>
    /// Пауза после завершения TLS перед отправкой клиентского пролога (H2 preface / H1 запрос).
    /// </summary>
    public TimeSpan Delay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Общий таймаут для безтокенного HandshakeAsync().
    /// Значение &lt;= 0 или <see cref="Timeout.InfiniteTimeSpan"/> оставляет поведение без жёсткого лимита.
    /// </summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Переставлять ли расширения ClientHello заново на каждое рукопожатие.
    /// </summary>
    /// <remarks>
    /// Включается профилем браузера, который так себя ведёт. Подробности, замеры и правило
    /// закрепления — в <see cref="ClientHelloExtensionPermutation"/>.
    ///
    /// ★ Отпечаток при этом перестаёт быть постоянным, и это НЕ дефект: у браузера он тоже не
    /// постоянен. Сверять такие профили нужно по составу расширений, а не по хэшу ja3.
    /// </remarks>
    public bool PermuteExtensions { get; init; }

    /// <summary>
    /// Идентификаторы расширений, которые перестановка не двигает.
    /// </summary>
    /// <remarks>
    /// Подставные значения и расширения, обязанные замыкать сообщение, закрепляются сами;
    /// здесь перечисляется лишь то, что закреплено особенностью конкретного браузера.
    /// </remarks>
    public IReadOnlyCollection<ushort>? PermutationAnchors { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(MinVersion);
        hashCode.Add(MaxVersion);
        hashCode.Add(CipherSuites);
        hashCode.Add(Extensions);
        hashCode.Add(CheckCertificateRevocationList);
        hashCode.Add(ServerCertificateValidationCallback);
        hashCode.Add(SessionIdPolicy);
        hashCode.Add(Delay);
        hashCode.Add(HandshakeTimeout);
        hashCode.Add(PermuteExtensions);
        hashCode.Add(PermutationAnchors);
        return hashCode.ToHashCode();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // Наборы сравниваются статическим Equals, как ссылки ниже: инициализаторы `= []` работают только
    // через конструктор, а у default(TlsSettings) — в частности у вложенного Http2.Tls, который никто
    // не задавал явно, — оба поля остаются null. Вызов на экземпляре ронял сравнение любых двух
    // профилей каталога NullReferenceException, включая самосравнение profile.Equals(profile).
    public bool Equals(TlsSettings other) => MinVersion.Equals(other.MinVersion) && MaxVersion.Equals(other.MaxVersion)
        && Equals(CipherSuites, other.CipherSuites) && Equals(Extensions, other.Extensions)
        && CheckCertificateRevocationList.Equals(other.CheckCertificateRevocationList)
        && Equals(ServerCertificateValidationCallback, other.ServerCertificateValidationCallback)
        && SessionIdPolicy.Equals(other.SessionIdPolicy) && Delay.Equals(other.Delay)
        && HandshakeTimeout.Equals(other.HandshakeTimeout)
        && PermuteExtensions.Equals(other.PermuteExtensions)
        && Equals(PermutationAnchors, other.PermutationAnchors);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        TlsSettings other => Equals(other),
        _ => default,
    };


#pragma warning disable MA0196 // Do not use inheritdoc on non-inheriting members
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning restore MA0196 // Do not use inheritdoc on non-inheriting members
    public static bool operator ==(TlsSettings left, TlsSettings right) => left.Equals(right);


#pragma warning disable MA0196 // Do not use inheritdoc on non-inheriting members
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning restore MA0196 // Do not use inheritdoc on non-inheriting members
    public static bool operator !=(TlsSettings left, TlsSettings right) => !left.Equals(right);
}