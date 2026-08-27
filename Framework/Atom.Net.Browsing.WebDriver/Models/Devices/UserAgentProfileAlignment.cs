namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Приводит профиль устройства в соответствие со строкой User-Agent.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. <see cref="Device.UserAgent"/>, <see cref="Device.Platform"/> и
/// <see cref="Device.ClientHints"/> — независимые поля, и ничто не связывает их между собой. Если
/// задать только UA, остальные признаки останутся от РЕАЛЬНОЙ машины: заявляем Windows, а
/// <c>navigator.platform</c> отдаёт <c>Linux x86_64</c> и заголовок <c>Sec-CH-UA-Platform</c> —
/// <c>"Linux"</c>. Такое расхождение замечается антибот-защитой: проверка на реальном таргете с одним
/// лишь подменённым UA дала 0 решений из 8 при явных error-callback от Cloudflare, тогда как без
/// подмены — 151 из 151.
/// Здесь по строке UA выводится согласованный набор: платформа, client hints (включая бренды для
/// <c>navigator.userAgentData</c>), мобильность и число точек касания.
/// ГРАНИЦЫ: заполняются только поля, ОДНОЗНАЧНО следующие из UA. Значения, которые UA не задаёт
/// (часовой пояс, локаль, размеры экрана, память, число ядер), намеренно не трогаются — их
/// правдоподобие определяется задачей, а не строкой UA. Уже заданные вызывающим значения не
/// перетираются: явный выбор важнее вывода.
/// РАЗБОР. Только прямой проход по строке (поиск токена + чтение символов версии). Регулярные
/// выражения здесь не нужны: грамматика UA примитивна, а линейный разбор без аллокаций и без
/// движка сопоставления быстрее и не несёт риска катастрофического бэктрекинга.
/// </remarks>
public static class UserAgentProfileAlignment
{
    /// <summary>
    /// Создаёт профиль устройства, согласованный с указанной строкой User-Agent.
    /// </summary>
    public static Device CreateDevice(string userAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);

        var device = new Device { UserAgent = userAgent };
        AlignToUserAgent(device);
        return device;
    }

    /// <summary>
    /// Дозаполняет профиль признаками, следующими из его <see cref="Device.UserAgent"/>.
    /// </summary>
    /// <remarks>
    /// Уже заполненные поля сохраняются: метод дополняет профиль, а не навязывает свой вариант.
    /// Если UA не задан, профиль остаётся без изменений — выводить не из чего.
    /// </remarks>
    public static void AlignToUserAgent(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var userAgent = device.UserAgent;
        if (string.IsNullOrWhiteSpace(userAgent))
            return;

        var span = userAgent.AsSpan();
        var platform = DetectPlatform(span);
        var isMobile = IsMobileUserAgent(span);

        device.Platform ??= platform.NavigatorPlatform;

        // Мобильность и касания должны следовать за платформой: настольный UA с ненулевыми
        // MaxTouchPoints — такое же расхождение, как и чужой navigator.platform.
        if (isMobile)
        {
            device.IsMobile = true;
            device.HasTouch = true;

            if (device.MaxTouchPoints <= 0)
                device.MaxTouchPoints = 5;
        }

        device.DeviceMemory = AlignDeviceMemory(device.DeviceMemory);
        device.WebGL = AlignWebGl(device.WebGL, platform);

        device.ClientHints = AlignClientHints(device.ClientHints, span, platform, isMobile);

        // WebGL здесь НЕ выводим намеренно. Строка программного рендерера
        // («ANGLE (Google, Vulkan … SwiftShader …)») не содержит признаков операционной системы,
        // поэтому заявленной платформе не противоречит и подмены не требует. Значение имело лишь
        // САМО НАЛИЧИЕ контекста: без GPU он не создавался вовсе, и пустые vendor/renderer выделяли
        // клиента — это решается программным рендерером на уровне запуска браузера, а не строками.
        // Подставлять же конкретную видеокарту было бы хуже: реальные возможности контекста
        // (расширения, лимиты, скорость) ей не соответствуют, и расхождение легко проверяется.
        // Прокидывание Device.WebGL в контекст вкладки при этом работает — если вызывающий задаст
        // значения явно, они будут применены.
    }

    /// <summary>
    /// Приводит объём памяти к значениям, которые вообще способен отдать браузер.
    /// </summary>
    /// <remarks>
    /// Спецификация Device Memory разрешает ровно лестницу 0.25/0.5/1/2/4/8 и обрезает результат
    /// сверху восьмёркой — именно чтобы значение не выделяло клиента. Настоящий браузер БОЛЬШЕ
    /// восьми не отдаёт никогда, поэтому «честные» 32 ГБ хоста — не правдивое значение, а
    /// невозможное: оно выдаёт подмену само по себе, независимо от заявленной платформы.
    /// Округляем ВНИЗ до ближайшей ступени: так значение остаётся достижимым для реальной машины.
    /// </remarks>
    /// <summary>
    /// Подбирает строки WebGL под заявленную платформу.
    /// </summary>
    /// <remarks>
    /// Замер на реальном таргете показал, что при заявленном Windows контекст WebGL у нас
    /// ОТСУТСТВОВАЛ вовсе (`getContext` возвращает null без GPU). Настольного браузера без WebGL
    /// не бывает — это выдача более грубая, чем любая строка рендерера, поэтому заполнение здесь
    /// решает сразу две задачи: включает программный рендеринг (флаги ставятся по наличию
    /// <see cref="Device.WebGL"/>) и даёт связке vendor/renderer вид, ожидаемый для платформы.
    ///
    /// Прежний вывод «строка SwiftShader нейтральна к ОС, подменять не нужно» верен лишь для
    /// профиля СВОЕЙ ОС. Заявляя чужую, мы обязаны отдать и правдоподобный графический стек:
    /// на Windows это ANGLE поверх Direct3D11, на macOS — Metal, на Linux — OpenGL.
    /// </remarks>
    private static WebGLSettings? AlignWebGl(WebGLSettings? existing, PlatformDescriptor platform)
    {
        // Явно заданные значения не трогаем: профиль вправе описывать конкретное железо.
        if (existing is not null)
            return existing;

        var (vendor, renderer) = platform.ClientHintsPlatform switch
        {
            "Windows" => ("Google Inc. (Intel)", "ANGLE (Intel, Intel(R) UHD Graphics 620 (0x00005917) Direct3D11 vs_5_0 ps_5_0, D3D11)"),
            "macOS" => ("Google Inc. (Apple)", "ANGLE (Apple, ANGLE Metal Renderer: Apple M1, Unspecified Version)"),
            "Android" => ("Google Inc. (Qualcomm)", "ANGLE (Qualcomm, Adreno (TM) 640, OpenGL ES 3.2)"),
            _ => (null, null),
        };

        if (vendor is null || renderer is null)
            return null;

        return new WebGLSettings
        {
            Vendor = vendor,
            Renderer = renderer,
            UnmaskedVendor = vendor,
            UnmaskedRenderer = renderer,
        };
    }

    private static double? AlignDeviceMemory(double? deviceMemory)
    {
        // Незаполненное поле — не «оставить как есть», а ПРОТЕЧКА: без подмены страница получает
        // настоящий объём памяти хоста (замер на реальном таргете дал 32 при заявленном Windows —
        // значение, недостижимое ни для одного браузера). Раз профиль строится из UA, поле обязано
        // быть заполнено. 8 — верхняя допустимая ступень и самое обычное значение для настольной
        // машины, поэтому само по себе клиента не выделяет.
        if (deviceMemory is not { } value || value <= 0)
            return 8;

        ReadOnlySpan<double> ladder = [0.25, 0.5, 1, 2, 4, 8];

        var aligned = ladder[0];
        foreach (var step in ladder)
        {
            if (step <= value)
                aligned = step;
        }

        return aligned;
    }

    private static ClientHintsSettings? AlignClientHints(
        ClientHintsSettings? existing,
        ReadOnlySpan<char> userAgent,
        PlatformDescriptor platform,
        bool isMobile)
    {
        // Client hints нужны только Chromium: navigator.userAgentData и заголовки Sec-CH-UA* у
        // Firefox отсутствуют, и подставлять их там — самостоятельный признак подделки.
        if (!TryDetectChromiumBrand(userAgent, out var brandName, out var major, out var full))
            return existing;

        var hints = existing ?? new ClientHintsSettings();

        hints.Platform ??= platform.ClientHintsPlatform;
        hints.PlatformVersion ??= platform.ClientHintsPlatformVersion;
        hints.Architecture ??= platform.Architecture;
        hints.Bitness ??= platform.Bitness;
        hints.Model ??= platform.Model;
        hints.Mobile ??= isMobile;
        hints.Brands ??= BuildBrands(brandName, major);
        hints.FullVersionList ??= BuildBrands(brandName, full);

        return hints;
    }

    /// <summary>
    /// Собирает список брендов в том же виде, в каком его отдаёт Chromium.
    /// </summary>
    /// <remarks>
    /// Chromium всегда добавляет «GREASE»-бренд (намеренно бессмысленный) — по нему сайты не должны
    /// строить логику. Его отсутствие само по себе выглядит подозрительно, поэтому воспроизводим.
    /// </remarks>
    private static List<ClientHintBrand> BuildBrands(string brandName, string version)
    {
        var greaseVersion = version.Contains('.', StringComparison.Ordinal) ? "99.0.0.0" : "99";
        var brands = new List<ClientHintBrand>(3)
        {
            new("Not;A=Brand", greaseVersion),
            new("Chromium", version),
        };

        if (!string.Equals(brandName, "Chromium", StringComparison.Ordinal))
            brands.Add(new ClientHintBrand(brandName, version));

        return brands;
    }

    /// <summary>
    /// Определяет бренд и версию Chromium-браузера.
    /// </summary>
    /// <remarks>
    /// Порядок проверок важен: UA Edge содержит и <c>Chrome/…</c>, и <c>Edg/…</c>, поэтому бренд
    /// определяет именно Edge-токен.
    /// </remarks>
    private static bool TryDetectChromiumBrand(
        ReadOnlySpan<char> userAgent,
        out string brandName,
        out string major,
        out string full)
    {
        if (TryReadVersion(userAgent, "Edg/", out major, out full)
            || TryReadVersion(userAgent, "EdgA/", out major, out full)
            || TryReadVersion(userAgent, "EdgiOS/", out major, out full))
        {
            brandName = "Microsoft Edge";
            return true;
        }

        if (TryReadVersion(userAgent, "Chrome/", out major, out full)
            || TryReadVersion(userAgent, "CriOS/", out major, out full))
        {
            brandName = "Google Chrome";
            return true;
        }

        brandName = string.Empty;
        return false;
    }

    /// <summary>
    /// Читает версию, стоящую сразу за токеном (например, <c>Chrome/151.0.0.0</c>).
    /// </summary>
    /// <param name="major">Мажорная часть версии.</param>
    /// <param name="full">Полная версия, дополненная до четырёх компонентов.</param>
    private static bool TryReadVersion(
        ReadOnlySpan<char> userAgent,
        ReadOnlySpan<char> token,
        out string major,
        out string full)
    {
        major = string.Empty;
        full = string.Empty;

        var start = userAgent.IndexOf(token, StringComparison.Ordinal);
        if (start < 0)
            return false;

        var version = ReadVersionDigits(userAgent[(start + token.Length)..]);
        if (version.IsEmpty)
            return false;

        var dot = version.IndexOf('.');
        major = dot < 0 ? version.ToString() : version[..dot].ToString();
        full = dot < 0 ? major + ".0.0.0" : version.ToString();
        return true;
    }

    /// <summary>
    /// Возвращает ведущую последовательность цифр и точек — то есть номер версии.
    /// </summary>
    private static ReadOnlySpan<char> ReadVersionDigits(ReadOnlySpan<char> value)
    {
        var length = 0;
        while (length < value.Length && (char.IsAsciiDigit(value[length]) || value[length] == '.'))
        {
            length++;
        }

        // Хвостовая точка в номер версии не входит.
        while (length > 0 && value[length - 1] == '.')
        {
            length--;
        }

        return value[..length];
    }

    private static bool IsMobileUserAgent(ReadOnlySpan<char> userAgent)
        => Contains(userAgent, "Mobile")
            || Contains(userAgent, "Android")
            || Contains(userAgent, "iPhone")
            || Contains(userAgent, "iPad");

    private static bool Contains(ReadOnlySpan<char> value, ReadOnlySpan<char> token)
        => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static PlatformDescriptor DetectPlatform(ReadOnlySpan<char> userAgent)
    {
        if (Contains(userAgent, "Windows NT"))
            return DescribeWindows(userAgent);

        if (Contains(userAgent, "Android"))
            return DescribeAndroid(userAgent);

        if (Contains(userAgent, "iPhone") || Contains(userAgent, "iPad"))
            return DescribeIos(userAgent);

        if (Contains(userAgent, "Macintosh") || Contains(userAgent, "Mac OS X"))
            return DescribeMac(userAgent);

        if (Contains(userAgent, "CrOS"))
            return new PlatformDescriptor("Linux x86_64", "Chrome OS", string.Empty, "x86", "64", string.Empty);

        return DescribeLinux(userAgent);
    }

    private static PlatformDescriptor DescribeWindows(ReadOnlySpan<char> userAgent)
    {
        var is64 = Contains(userAgent, "Win64") || Contains(userAgent, "x64") || Contains(userAgent, "WOW64");

        // navigator.platform на Windows — всегда "Win32", в том числе на 64-битных сборках:
        // разрядность отражается только в client hints (bitness), а не в этом поле.
        // UA-строка Windows заморожена на «Windows NT 10.0» и не различает 10 и 11, тогда как client
        // hints отдают версию платформы отдельно. Берём значение, соответствующее Windows 10: оно
        // согласуется с самой строкой UA и не претендует на большее.
        return new PlatformDescriptor(
            NavigatorPlatform: "Win32",
            ClientHintsPlatform: "Windows",
            ClientHintsPlatformVersion: "10.0.0",
            Architecture: Contains(userAgent, "ARM") ? "arm" : "x86",
            Bitness: is64 ? "64" : "32",
            Model: string.Empty);
    }

    private static PlatformDescriptor DescribeAndroid(ReadOnlySpan<char> userAgent)
        => new(
            NavigatorPlatform: "Linux armv8l",
            ClientHintsPlatform: "Android",
            ClientHintsPlatformVersion: ReadAndroidVersion(userAgent),
            Architecture: "arm",
            Bitness: "64",
            Model: ReadAndroidModel(userAgent));

    private static PlatformDescriptor DescribeIos(ReadOnlySpan<char> userAgent)
        => new(
            NavigatorPlatform: Contains(userAgent, "iPad") ? "iPad" : "iPhone",
            ClientHintsPlatform: "iOS",
            ClientHintsPlatformVersion: string.Empty,
            Architecture: "arm",
            Bitness: "64",
            Model: string.Empty);

    private static PlatformDescriptor DescribeMac(ReadOnlySpan<char> userAgent)
        => new(
            NavigatorPlatform: "MacIntel",
            ClientHintsPlatform: "macOS",
            ClientHintsPlatformVersion: ReadMacVersion(userAgent),
            Architecture: Contains(userAgent, "Intel") ? "x86" : "arm",
            Bitness: "64",
            Model: string.Empty);

    private static PlatformDescriptor DescribeLinux(ReadOnlySpan<char> userAgent)
    {
        var is64 = Contains(userAgent, "x86_64");

        return new PlatformDescriptor(
            NavigatorPlatform: is64 ? "Linux x86_64" : "Linux i686",
            ClientHintsPlatform: "Linux",
            ClientHintsPlatformVersion: string.Empty,
            Architecture: "x86",
            Bitness: is64 ? "64" : "32",
            Model: string.Empty);
    }

    /// <summary>Версия Android из фрагмента вида <c>Android 13; …</c>.</summary>
    private static string ReadAndroidVersion(ReadOnlySpan<char> userAgent)
    {
        var start = userAgent.IndexOf("Android ", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;

        var version = ReadVersionDigits(userAgent[(start + "Android ".Length)..]);
        return version.IsEmpty ? string.Empty : version.ToString();
    }

    /// <summary>
    /// Модель устройства из фрагмента вида <c>Android 13; SM-S901B)</c> или <c>… SM-S901B Build/…</c>.
    /// </summary>
    private static string ReadAndroidModel(ReadOnlySpan<char> userAgent)
    {
        var start = userAgent.IndexOf("Android ", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;

        var tail = userAgent[(start + "Android ".Length)..];

        // Пропускаем номер версии и разделитель «; » — дальше идёт модель.
        var separator = tail.IndexOf(';');
        if (separator < 0)
            return string.Empty;

        tail = tail[(separator + 1)..].TrimStart();

        var end = tail.Length;
        var close = tail.IndexOf(')');
        if (close >= 0 && close < end)
            end = close;

        var build = tail.IndexOf(" Build/", StringComparison.Ordinal);
        if (build >= 0 && build < end)
            end = build;

        var semicolon = tail.IndexOf(';');
        if (semicolon >= 0 && semicolon < end)
            end = semicolon;

        return tail[..end].Trim().ToString();
    }

    /// <summary>
    /// Версия macOS из фрагмента вида <c>Mac OS X 10_15_7</c>; подчёркивания приводятся к точкам.
    /// </summary>
    private static string ReadMacVersion(ReadOnlySpan<char> userAgent)
    {
        var start = userAgent.IndexOf("Mac OS X ", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;

        var tail = userAgent[(start + "Mac OS X ".Length)..];

        var length = 0;
        while (length < tail.Length && (char.IsAsciiDigit(tail[length]) || tail[length] == '.' || tail[length] == '_'))
        {
            length++;
        }

        if (length == 0)
            return string.Empty;

        return string.Create(length, tail[..length].ToString(), static (destination, source) =>
        {
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = source[index] == '_' ? '.' : source[index];
            }
        });
    }

    private readonly record struct PlatformDescriptor(
        string NavigatorPlatform,
        string ClientHintsPlatform,
        string ClientHintsPlatformVersion,
        string Architecture,
        string Bitness,
        string Model);
}
