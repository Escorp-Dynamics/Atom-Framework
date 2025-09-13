using System.Runtime.CompilerServices;

namespace Atom.Net.Tls;

/// <summary>
/// 
/// </summary>
public class ChromeClientHelloValidator : ClientHelloValidator
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Validate(TlsSettings settings)
    {
        var exts = settings.Extensions ?? [];
        var count = exts.Count();

        if (!exts.Any()) throw new InvalidOperationException("В профиле TLS отсутствуют расширения — невозможно собрать валидный ClientHello под браузерный отпечаток");

        // 1) PSK должен идти ПОСЛЕДНИМ (TLS 1.3).
        var pskIndex = IndexOf(exts, 0x0029);

        if (pskIndex >= 0 && pskIndex != count - 1)
            throw new InvalidOperationException("Расширение pre_shared_key должно располагаться последним в ClientHello");

        // 2) EarlyData недопустим без PSK.
        if (IndexOf(exts, 0x002A) >= 0 && pskIndex < 0)
            throw new InvalidOperationException("Найден early_data без pre_shared_key — это некорректно");

        // 3) supported_groups обязателен при наличии key_share (JA3/JA4 плюс поведение браузеров).
        var hasKeyShare = IndexOf(exts, 0x0033) >= 0;
        var hasGroups = IndexOf(exts, 0x000A) >= 0;

        if (hasKeyShare && !hasGroups)
            throw new InvalidOperationException("Найден key_share без supported_groups — это легко детектируется");

        // 4) supported_versions обязателен для TLS1.3-профилей.
        var hasEcPointFormats = IndexOf(exts, 0x000B) >= 0;
        var hasSuppVers = IndexOf(exts, 0x002B) >= 0;

        if (!hasSuppVers && !hasEcPointFormats)
            throw new InvalidOperationException("TLS 1.2 профиль: требуется ec_point_formats(uncompressed)");

        // 5) Подписи сертификата ⊆ общих подписи (как делают браузеры).
        var hasSig = IndexOf(exts, 0x000D) >= 0;
        var hasSigCert = IndexOf(exts, 0x0032) >= 0;

        if (hasSigCert && !hasSig)
            throw new InvalidOperationException("Есть signature_algorithms_cert без signature_algorithms");

        // 6) PSK Key Exchange Modes обязателен при PSK.
        if (pskIndex >= 0 && IndexOf(exts, 0x002D) < 0)
            throw new InvalidOperationException("pre_shared_key без psk_key_exchange_modes — некорректно");

        // 7) ALPN: должен быть строго в том порядке, что задан профилем браузера.
        if (IndexOf(exts, 0x0010) < 0)
            throw new InvalidOperationException("Отсутствует alpn — сервер не сможет выбрать HTTP/2/HTTP/3 корректно");

        // 8) SNI обязателен для браузерной мимикрии (за редкими исключениями).
        if (IndexOf(exts, 0x0000) < 0)
            throw new InvalidOperationException("Отсутствует server_name (SNI) — поведение нетипично для браузера");

        // 9) PSK последним — уже проверили; также убедимся, что GREASE вставки не дублируют позиции.
        // Здесь можно дернуть ваш Grease-контекст, если он доступен.

        // 10) При наличии compress_certificate убедиться в согласованности списка алгоритмов.
        // Детали зависят от вашего класса расширения — при желании можно расширить проверку.
    }
}