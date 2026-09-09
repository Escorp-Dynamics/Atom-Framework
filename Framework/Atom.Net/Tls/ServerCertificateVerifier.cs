using System.Linq;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Atom.Net.Tls;

/// <summary>
/// Проверка подлинности сервера: цепочка сертификатов, имя хоста и подпись CertificateVerify.
/// </summary>
/// <remarks>
/// Выделено в отдельный тип, потому что нужно обеим версиям протокола: TLS 1.2 и TLS 1.3
/// различаются составом рукопожатия, но требования к сертификату у них одинаковые. Держать эту
/// логику дважды означало бы рано или поздно разойтись в правилах — а расхождение в проверке
/// подлинности это не косметика, а дыра.
/// </remarks>
public static class ServerCertificateVerifier
{
    /// <summary>Контекстная строка для подписи сервера в TLS 1.3 (RFC 8446, §4.4.3).</summary>
    private static ReadOnlySpan<byte> ServerContext => "TLS 1.3, server CertificateVerify"u8;

    /// <summary>
    /// Проверяет цепочку сертификатов и соответствие имени хоста.
    /// </summary>
    /// <param name="certificates">Цепочка в порядке получения: первым — конечный сертификат.</param>
    /// <param name="host">Имя хоста из SNI.</param>
    /// <param name="settings">Настройки TLS: политика отзыва и пользовательская проверка.</param>
    /// <exception cref="CryptographicException">Проверка не пройдена.</exception>
    public static void Validate(IReadOnlyList<X509Certificate2> certificates, string? host, in TlsSettings settings)
    {
        if (certificates is null || certificates.Count is 0) throw new CryptographicException("Сертификат сервера отсутствует");

        var leaf = certificates[0];

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = settings.CheckCertificateRevocationList ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);

        // Проверяем назначение «аутентификация сервера»: сертификат, выданный для другой цели,
        // не должен приниматься даже при корректной цепочке.
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));

        for (var index = 1; index < certificates.Count; index++) chain.ChainPolicy.ExtraStore.Add(certificates[index]);

        var chainBuilt = chain.Build(leaf);
        var nameMatches = !string.IsNullOrEmpty(host) && HostnameMatches(leaf, host);

        var errors = SslPolicyErrors.None;
        if (!chainBuilt) errors |= SslPolicyErrors.RemoteCertificateChainErrors;
        if (!nameMatches) errors |= SslPolicyErrors.RemoteCertificateNameMismatch;

        var callback = settings.ServerCertificateValidationCallback;
        if (callback is not null)
        {
            if (!callback(leaf, chain, errors)) throw new CryptographicException("Пользовательская проверка сертификата отклонила соединение");
            return;
        }

        if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) is not 0)
        {
            throw new CryptographicException("Не удалось построить цепочку сертификатов сервера");
        }

        if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) is not 0)
        {
            throw new CryptographicException(
                $"Имя хоста не соответствует сертификату сервера: ожидалось '{host}', в сертификате {DescribeCertificateNames(leaf)}");
        }
    }

    /// <summary>
    /// Проверяет подпись CertificateVerify сервера (RFC 8446, §4.4.3).
    /// </summary>
    /// <param name="certificate">Конечный сертификат сервера.</param>
    /// <param name="signatureScheme">Идентификатор схемы подписи из сообщения.</param>
    /// <param name="signature">Значение подписи.</param>
    /// <param name="transcriptHash">Хэш транскрипта вплоть до сообщения Certificate включительно.</param>
    /// <exception cref="CryptographicException">Подпись не соответствует сертификату.</exception>
    /// <remarks>
    /// Это ключевое доказательство того, что на другом конце действительно владелец приватного
    /// ключа, а не тот, кто просто переслал чужой сертификат. Без этой проверки цепочка сама по
    /// себе ничего не гарантирует.
    /// </remarks>
    public static void VerifySignature(X509Certificate2 certificate, ushort signatureScheme, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> transcriptHash)
    {
        // Подписывается не сам транскрипт, а специальная конструкция: 64 пробела, контекстная
        // строка, нулевой разделитель и хэш. Префикс из пробелов защищает от переноса подписи
        // между ролями и версиями протокола.
        var contentLength = 64 + ServerContext.Length + 1 + transcriptHash.Length;
        var content = contentLength <= 256 ? stackalloc byte[contentLength] : new byte[contentLength];

        content[..64].Fill(0x20);
        ServerContext.CopyTo(content[64..]);
        content[64 + ServerContext.Length] = 0;
        transcriptHash.CopyTo(content[(64 + ServerContext.Length + 1)..]);

        if (!TryVerify(certificate, signatureScheme, signature, content))
            throw new CryptographicException($"Подпись CertificateVerify не прошла проверку (схема 0x{signatureScheme:X4})");
    }

    private static bool TryVerify(X509Certificate2 certificate, ushort signatureScheme, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> content)
    {
        return signatureScheme switch
        {
            // rsa_pss_rsae_* и rsa_pss_pss_*: современные браузеры и серверы выбирают именно их.
            0x0804 or 0x0809 => VerifyRsaPss(certificate, signature, content, HashAlgorithmName.SHA256),
            0x0805 or 0x080A => VerifyRsaPss(certificate, signature, content, HashAlgorithmName.SHA384),
            0x0806 or 0x080B => VerifyRsaPss(certificate, signature, content, HashAlgorithmName.SHA512),

            // ecdsa_secp*_sha*: подпись приходит в формате DER, как и требует X.509.
            0x0403 => VerifyEcdsa(certificate, signature, content, HashAlgorithmName.SHA256),
            0x0503 => VerifyEcdsa(certificate, signature, content, HashAlgorithmName.SHA384),
            0x0603 => VerifyEcdsa(certificate, signature, content, HashAlgorithmName.SHA512),

            // rsa_pkcs1_*: в TLS 1.3 допустимы только в сертификатах, но встречаются у части серверов.
            0x0401 => VerifyRsaPkcs1(certificate, signature, content, HashAlgorithmName.SHA256),
            0x0501 => VerifyRsaPkcs1(certificate, signature, content, HashAlgorithmName.SHA384),
            0x0601 => VerifyRsaPkcs1(certificate, signature, content, HashAlgorithmName.SHA512),

            _ => throw new NotSupportedException($"Схема подписи 0x{signatureScheme:X4} не поддержана"),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool VerifyRsaPss(X509Certificate2 certificate, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> content, HashAlgorithmName hash)
    {
        using var rsa = certificate.GetRSAPublicKey();
        return rsa is not null && rsa.VerifyData(content, signature, hash, RSASignaturePadding.Pss);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool VerifyRsaPkcs1(X509Certificate2 certificate, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> content, HashAlgorithmName hash)
    {
        using var rsa = certificate.GetRSAPublicKey();
        return rsa is not null && rsa.VerifyData(content, signature, hash, RSASignaturePadding.Pkcs1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool VerifyEcdsa(X509Certificate2 certificate, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> content, HashAlgorithmName hash)
    {
        using var ecdsa = certificate.GetECDsaPublicKey();
        return ecdsa is not null && ecdsa.VerifyData(content, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
    }

    /// <summary>
    /// Проверяет соответствие имени хоста сертификату по SAN, а при его отсутствии — по CN.
    /// </summary>
    /// <param name="certificate">Сертификат.</param>
    /// <param name="host">Имя хоста.</param>
    /// <returns><see langword="true"/>, если имя соответствует.</returns>
    /// <remarks>
    /// ★ Перебираются ВСЕ имена из SAN, а не первое. Прежний код брал
    /// <c lang="text">GetNameInfo(X509NameType.DnsName)</c>, который возвращает лишь одну запись, — и
    /// отвергал мультидоменные сертификаты, когда запрошенное имя стояло в списке не первым.
    /// Случай не редкий, а типовой: универсальный сертификат CDN несёт
    /// <c lang="text">[*.example.com, example.com]</c>, и обращение к голому <c lang="text">example.com</c> не совпадало
    /// с шаблоном <c lang="text">*.example.com</c>, потому что шаблон покрывает только поддомены.
    ///
    /// Возврата к CN при наличии SAN нет — так требует RFC 6125 §6.4.4 и так поступают браузеры:
    /// имея SAN, они CN не смотрят вовсе. Прежний запасной путь означал, что сертификат с
    /// нужным CN принимался ДАЖЕ ТОГДА, когда SAN явно не содержал запрошенного имени.
    ///
    /// Адреса из SAN тоже учитываются: обращение к <c lang="text">https://1.2.3.4/</c> сверяется с записями
    /// iPAddress, а не с доменными именами.
    /// </remarks>
    public static bool HostnameMatches(X509Certificate2 certificate, string host)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (string.IsNullOrEmpty(host)) return false;

        var subjectAlternativeName = FindSubjectAlternativeName(certificate);

        if (subjectAlternativeName is not null)
        {
            if (IPAddress.TryParse(host, out var address))
            {
                foreach (var candidate in subjectAlternativeName.EnumerateIPAddresses())
                {
                    if (candidate.Equals(address)) return true;
                }

                return false;
            }

            foreach (var candidate in subjectAlternativeName.EnumerateDnsNames())
            {
                if (WildcardMatch(candidate, host)) return true;
            }

            // SAN есть, но имени в нём нет: сертификат выдан не для этого хоста, и смотреть в CN
            // означало бы принять его вопреки прямому перечислению.
            return false;
        }

        var commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return !string.IsNullOrEmpty(commonName) && WildcardMatch(commonName, host);
    }

    /// <summary>
    /// Перечисляет имена сертификата для сообщения об ошибке.
    /// </summary>
    /// <param name="certificate">Сертификат.</param>
    /// <returns>Имена через запятую.</returns>
    /// <remarks>
    /// Без них отказ «имя не соответствует» ничего не объясняет: непонятно, чей сертификат
    /// пришёл и какое имя мы вообще проверяли. Разбирательство из-за этого начинается с
    /// написания отдельного пробника — а должно начинаться с чтения сообщения.
    /// </remarks>
    public static string DescribeCertificateNames(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var extension = FindSubjectAlternativeName(certificate);
        var names = extension is null ? [] : extension.EnumerateDnsNames().ToArray();

        return names.Length > 0
            ? string.Join(", ", names)
            : certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? "(имён нет)";
    }

    /// <summary>
    /// Находит расширение альтернативных имён субъекта.
    /// </summary>
    /// <param name="certificate">Сертификат.</param>
    /// <returns>Расширение или <see langword="null"/>, если его нет.</returns>
    private static X509SubjectAlternativeNameExtension? FindSubjectAlternativeName(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509SubjectAlternativeNameExtension subjectAlternativeName) return subjectAlternativeName;

            // Расширение может прийти нетипизированным — тогда разбираем его сами по идентификатору.
            if (string.Equals(extension.Oid?.Value, SubjectAlternativeNameOid, StringComparison.Ordinal))
                return new X509SubjectAlternativeNameExtension(extension.RawData, extension.Critical);
        }

        return null;
    }

    /// <summary>Идентификатор расширения альтернативных имён субъекта.</summary>
    private const string SubjectAlternativeNameOid = "2.5.29.17";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WildcardMatch(string pattern, string host)
    {
        // Поддерживается только левый шаблон вида *.example.com — как и в браузерах. Шаблон в
        // середине имени спецификацией не разрешён, и принимать его означало бы ослабить проверку.
        if (pattern.Length > 2 && pattern[0] is '*' && pattern[1] is '.')
        {
            var suffix = pattern.AsSpan(1);

            return host.AsSpan().EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && host.AsSpan().IndexOf('.') > 0;
        }

        return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }
}
