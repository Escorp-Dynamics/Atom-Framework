using System.Text;
using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Готовит браузеру набор шрифтов заявленной платформы.
/// </summary>
/// <remarks>
/// Перечисление шрифтов — одна из самых дешёвых и самых надёжных проверок операционной системы:
/// набор Segoe UI/Calibri/Cambria/Consolas есть на любой Windows и отсутствует на Linux целиком.
/// Замер на реальном таргете при заявленном Windows дал 0 доступных из 8 — то есть окружение
/// прямо опровергало заявленную платформу.
///
/// Правильный слой решения — не JavaScript, а сам браузер: подменять результаты измерения текста
/// из скрипта бессмысленно, потому что метрики видны ещё и через вёрстку (ширина элементов,
/// переносы), а патч измерений детектируется надёжнее, чем прячет. Поэтому шрифты заявленной
/// платформы выдаются НАСТОЯЩИМ механизмом шрифтов: конфигурация fontconfig с псевдонимами,
/// подсунутая браузеру через переменную окружения <c lang="text">FONTCONFIG_FILE</c>.
///
/// Конфигурация ЛОКАЛЬНА для процесса браузера и лежит в каталоге профиля: системные настройки
/// пользователя не затрагиваются. Системный конфиг подключается первым, поэтому обычные шрифты
/// продолжают работать, а псевдонимы лишь дополняют набор.
///
/// Ограничение, которое надо понимать: псевдоним даёт НАЛИЧИЕ семейства, но метрики остаются
/// метриками подставленного шрифта. Проверка «есть ли Segoe UI» пройдена, проверка «совпадает ли
/// ширина строки с эталонной для Segoe UI» — нет. Полное совпадение достижимо только настоящими
/// файлами шрифтов этой платформы либо метрически совместимыми клонами.
/// </remarks>
internal static class PlatformFontConfiguration
{
    internal const string FileName = "fonts.conf";

    /// <summary>Имя файла конфигурации fontconfig в каталоге профиля.</summary>
    /// <summary>
    /// Откуда берутся файлы шрифтов-доноров.
    /// </summary>
    /// <remarks>
    /// Ищем файлы, а не спрашиваем fontconfig: запуск <c lang="text">fc-match</c> добавил бы зависимость от
    /// внешней программы на пути, который обязан работать всегда. Имена файлов у этих пакетов
    /// стабильны во всех дистрибутивах.
    /// </remarks>
    private static readonly string[] FontSearchRoots =
    [
        "/usr/share/fonts",
        "/usr/local/share/fonts",
    ];

    /// <summary>Префикс имени файла донора: забирает все начертания семейства разом.</summary>
    private static readonly Dictionary<string, string> DonorFilePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Liberation Sans"] = "LiberationSans",
        ["Liberation Serif"] = "LiberationSerif",
        ["Liberation Mono"] = "LiberationMono",
        ["DejaVu Sans"] = "DejaVuSans",
        ["Carlito"] = "Carlito",
        ["Caladea"] = "Caladea",
        ["Noto Color Emoji"] = "NotoColorEmoji",
    };

    /// <summary>Чем заменить донора, которого нет на машине, — по убыванию близости начертания.</summary>
    /// <remarks>
    /// Carlito и Caladea метрически совместимы с Calibri и Cambria, но в базовую поставку
    /// дистрибутивов не входят: замер на рабочей машине нашёл 0 файлов обоих. Без замены
    /// семейство остаётся без файла и разрешается в чужой шрифт — ровно то расхождение, которое
    /// и выдаёт подмену.
    /// </remarks>
    private static readonly Dictionary<string, string[]> DonorSubstitutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Carlito"] = ["Liberation Sans", "DejaVu Sans"],
        ["Caladea"] = ["Liberation Serif", "DejaVu Sans"],
        ["Noto Color Emoji"] = ["DejaVu Sans"],
        ["Liberation Sans"] = ["DejaVu Sans"],
        ["Liberation Serif"] = ["DejaVu Sans"],
        ["Liberation Mono"] = ["DejaVu Sans"],
    };

    /// <summary>Каталог с копиями доноров внутри профиля.</summary>
    internal const string FontsDirectoryName = "fonts";

    private static readonly (string Family, string Fallback)[] WindowsFamilies =
    [
        ("Segoe UI", "Liberation Sans"),
        ("Segoe UI Symbol", "DejaVu Sans"),
        ("Segoe UI Emoji", "Noto Color Emoji"),
        ("Calibri", "Carlito"),
        ("Cambria", "Caladea"),
        ("Consolas", "Liberation Mono"),
        ("Tahoma", "DejaVu Sans"),
        ("Candara", "Liberation Sans"),
        ("Corbel", "Liberation Sans"),
        ("Sylfaen", "Liberation Serif"),
        ("Georgia", "Liberation Serif"),
        ("Verdana", "DejaVu Sans"),
        ("Arial", "Liberation Sans"),
        ("Times New Roman", "Liberation Serif"),
        ("Courier New", "Liberation Mono"),
        ("MS Shell Dlg 2", "Liberation Sans"),
    ];

    /// <summary>
    /// Семейства, выдающие Linux, — их браузер видеть не должен.
    /// </summary>
    /// <remarks>
    /// ★ Замер эмуляции macOS показал, что подстановка давала нужные семейства, но НЕ УБИРАЛА
    /// свои: страница видела одновременно <c lang="text">Helvetica Neue</c>, <c lang="text">Geneva</c> — и рядом
    /// <c lang="text">DejaVu Sans</c>, <c lang="text">Liberation Sans</c>, <c lang="text">Noto Sans</c>, <c lang="text">Cantarell</c>. Ни одного
    /// из последних на настоящем макбуке не бывает, и проверяется это тем же перечислением, что
    /// и наличие нужных, — то есть даром.
    ///
    /// Список намеренно перечисляет ИМЕНА, а не файлы: детектор опрашивает набор по именам, и
    /// закрывать надо ровно то, что он спрашивает. Семейства-доноры сюда не входят — они
    /// переименовываются на этапе сканирования и под своими именами уже не существуют.
    ///
    /// Полнота здесь недостижима: наборы шрифтов у дистрибутивов разные. Перечислены те, что
    /// ставятся почти везде и потому чаще всего и попадают в опрос.
    /// </remarks>
    private static readonly string[] LinuxOnlyFamilies =
    [
        "Cantarell", "Ubuntu", "Ubuntu Mono", "Ubuntu Condensed",
        "Adwaita Sans", "Adwaita Mono",
        "Fira Sans", "Fira Mono", "Fira Code",
        "Bitstream Vera Sans", "Bitstream Vera Serif", "Bitstream Vera Sans Mono",
        "FreeSans", "FreeSerif", "FreeMono",
        "Nimbus Sans", "Nimbus Roman", "Nimbus Mono PS",
        "Droid Sans", "Droid Serif", "Droid Sans Mono",
        "URW Bookman", "URW Gothic", "Century Schoolbook L",
        "Hack", "Inconsolata", "Source Code Pro", "Cousine", "Tinos", "Arimo",
        "Noto Sans", "Noto Serif", "Noto Mono", "Noto Sans Mono",
        "Oxygen", "Oxygen Mono", "Cascadia Code", "Cascadia Mono",

        // Сами доноры: их СИСТЕМНЫЕ оригиналы прятать теперь можно и нужно. Копии в профиле уже
        // носят заявленные имена (переименованы при сканировании нашего каталога), поэтому
        // отклонение по имени бьёт только по оригиналам и подстановку не ломает.
        "Liberation Sans", "Liberation Serif", "Liberation Mono",
        "DejaVu Sans", "DejaVu Serif", "DejaVu Sans Mono",
        "Carlito", "Caladea", "Noto Color Emoji",
    ];

    /// <summary>
    /// Семейства, которые обязаны быть у iPhone и iPad.
    /// </summary>
    /// <remarks>
    /// ★ Замер независимым анализатором отпечатка (CreepJS) на профиле iPhone показал в списке
    /// шрифтов «DejaVu Sans», «Liberation Mono» и «Noto Sans Canadian Aboriginal» — набор Linux,
    /// которого на устройстве Apple не бывает НИ ПРИ КАКОЙ установке. Подмена шрифтов до этой
    /// правки работала только для Windows и macOS, а iOS оставался с настоящими шрифтами машины.
    ///
    /// Состав взят из системного набора iOS: San Francisco как системный, Helvetica Neue и
    /// классические семейства, доступные веб-странице, плюс моноширинный Menlo.
    /// </remarks>
    private static readonly (string Family, string Fallback)[] IosFamilies =
    [
        ("SF Pro Text", "Liberation Sans"),
        ("SF Pro Display", "Liberation Sans"),
        ("Helvetica Neue", "Liberation Sans"),
        ("Helvetica", "Liberation Sans"),
        ("Arial", "Liberation Sans"),
        ("Verdana", "DejaVu Sans"),
        ("Trebuchet MS", "Liberation Sans"),
        ("Times New Roman", "Liberation Serif"),
        ("Georgia", "Liberation Serif"),
        ("Palatino", "Liberation Serif"),
        ("Menlo", "Liberation Mono"),
        ("Courier New", "Liberation Mono"),
        ("Courier", "Liberation Mono"),
        ("Avenir", "Liberation Sans"),
        ("Avenir Next", "Liberation Sans"),
        ("Optima", "Liberation Sans"),
        ("Apple Color Emoji", "Noto Color Emoji"),

        // Покрытие иероглифов: на устройстве Apple за него отвечает PingFang. Без донора
        // страницы с китайским текстом рисовались бы квадратами — отпечаток среды без шрифтов.
        ("PingFang SC", "Noto Sans CJK HK"),
    ];

    /// <summary>
    /// Семейства, которые обязаны быть у телефона на Android.
    /// </summary>
    /// <remarks>
    /// Здесь противоречие мягче: семейство Noto есть и на Android, и на этой машине. Но Roboto —
    /// системный шрифт Android, и его отсутствие так же наблюдаемо, как присутствие Liberation,
    /// которого на телефоне нет.
    /// </remarks>
    private static readonly (string Family, string Fallback)[] AndroidFamilies =
    [
        ("Roboto", "Liberation Sans"),
        ("Roboto Mono", "Liberation Mono"),
        ("Noto Sans", "DejaVu Sans"),
        ("Noto Serif", "Liberation Serif"),
        ("Noto Color Emoji", "Noto Color Emoji"),
        ("Droid Sans Mono", "Liberation Mono"),
        ("Carrois Gothic SC", "Liberation Sans"),
        ("Coming Soon", "Liberation Sans"),
        ("Cutive Mono", "Liberation Mono"),
        ("Dancing Script", "Liberation Sans"),
        ("Noto Sans CJK SC", "Noto Sans CJK HK"),
    ];

    private static readonly (string Family, string Fallback)[] MacFamilies =
    [
        ("Helvetica Neue", "Liberation Sans"),
        ("SF Pro Text", "Liberation Sans"),
        ("SF Pro Display", "Liberation Sans"),
        ("Menlo", "Liberation Mono"),
        ("Monaco", "Liberation Mono"),
        ("Geneva", "Liberation Sans"),
        ("Lucida Grande", "DejaVu Sans"),
        ("Apple Color Emoji", "Noto Color Emoji"),

        // Общеупотребимые семейства, которые есть и на macOS: без них generic-семейства
        // (serif/sans-serif) уходили в случайный системный шрифт, и ширины строк переставали
        // походить на макбук — а это видно и без перечисления, по вёрстке.
        ("Helvetica", "Liberation Sans"),
        ("Arial", "Liberation Sans"),
        ("Times", "Liberation Serif"),
        ("Times New Roman", "Liberation Serif"),
        ("Georgia", "Liberation Serif"),
        ("Courier", "Liberation Mono"),
        ("Courier New", "Liberation Mono"),

        // Покрытие иероглифов на macOS — PingFang, как и на iOS.
        ("PingFang SC", "Noto Sans CJK HK"),
    ];

    /// <summary>
    /// Копирует файлы шрифтов-доноров в профиль и раздаёт им заявленные имена.
    /// </summary>
    /// <param name="families">Пары «заявленное семейство — донор».</param>
    /// <param name="profilePath">Каталог профиля браузера.</param>
    /// <returns>Пары «путь копии — имя семейства, под которым она будет видна».</returns>
    /// <remarks>
    /// Один донор обслуживает несколько заявленных семейств, поэтому копия получает имя ПЕРВОГО
    /// из них, а остальные ссылаются на него псевдонимом. Так у каждого файла ровно одно
    /// настоящее имя, и перечисление шрифтов страницей не выдаёт ни донора, ни его происхождения.
    ///
    /// Начертания забираются все: копируются файлы, чьё имя начинается с префикса семейства.
    /// Без этого жирный и наклонный текст подставлялись бы чем попало, а разница в метриках
    /// видна и без перечисления — по ширине блоков.
    /// </remarks>
    private static List<(string Path, string Family)> CopyDonorFonts((string Family, string Fallback)[] families, string profilePath)
    {
        var result = new List<(string Path, string Family)>();
        var targetDirectory = IOPath.Combine(profilePath, FontsDirectoryName);

        try
        {
            Directory.CreateDirectory(targetDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        // ★ КАЖДОЕ семейство получает СВОЮ копию донора под своим именем.
        //
        // Прежде донор давал имя лишь ПЕРВОМУ семейству из списка, а остальные оставались вовсе
        // без файла: `Verdana` и `Tahoma` делят донора DejaVu, и Verdana существовала только как
        // строка в alias. Замер `fc-match` под живым конфигом показывал, во что это выливается:
        // Calibri, Cambria, Tahoma и Verdana разрешались в ОДИН файл под чужим именем
        // «Segoe UI Symbol», а браузер видел 5 семейств вместо шестнадцати заявленных. Отдельная
        // копия на семейство снимает и то, и другое — имена перестают делить файл.
        foreach (var (family, fallback) in families)
        {
            // Отсутствующий на машине донор заменяется запасным того же начертания: без этого
            // семейство остаётся без файла и падает в подстановку к соседу. На большинстве
            // дистрибутивов нет Carlito и Caladea, то есть Calibri и Cambria — почти всегда.
            var prefix = ResolveDonorPrefix(fallback);
            if (prefix is null) continue;

            foreach (var source in FindFontFiles(prefix))
            {
                // Имя файла несёт семейство: один и тот же донор копируется под разными именами,
                // и правило сканирования различает копии по пути.
                var fileName = SanitizeFileName(family) + "-" + IOPath.GetFileName(source);
                var target = IOPath.Combine(targetDirectory, fileName);

                try
                {
                    if (!File.Exists(target)) File.Copy(source, target);
                    result.Add((target, family));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Один недоступный файл не повод остаться вовсе без шрифтов заявленной ОС.
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Подбирает префикс файлов донора, подставляя запасного при отсутствии основного.
    /// </summary>
    /// <param name="donor">Имя семейства-донора.</param>
    /// <returns>Префикс имени файла либо <see langword="null"/>, если ни одного файла нет.</returns>
    private static string? ResolveDonorPrefix(string donor)
    {
        if (DonorFilePrefixes.TryGetValue(donor, out var prefix) && FindFontFiles(prefix).Any())
            return prefix;

        if (!DonorSubstitutes.TryGetValue(donor, out var substitutes)) return null;

        foreach (var substitute in substitutes)
        {
            if (DonorFilePrefixes.TryGetValue(substitute, out var substitutePrefix)
                && FindFontFiles(substitutePrefix).Any())
            {
                return substitutePrefix;
            }
        }

        return null;
    }

    /// <summary>Приводит имя семейства к безопасному имени файла.</summary>
    private static string SanitizeFileName(string family)
    {
        var builder = new StringBuilder(family.Length);

        foreach (var character in family)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');

        return builder.ToString();
    }

    /// <summary>
    /// Ищет файлы шрифтов по префиксу имени.
    /// </summary>
    /// <param name="prefix">Начало имени файла.</param>
    /// <returns>Найденные пути.</returns>
    private static IEnumerable<string> FindFontFiles(string prefix)
    {
        foreach (var root in FontSearchRoots)
        {
            if (!Directory.Exists(root)) continue;

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(root, prefix + "*", SearchOption.AllDirectories);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var extension = IOPath.GetExtension(file);

                if (string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>
    /// Привязывает обобщённые семейства к тем, что есть на заявленной платформе.
    /// </summary>
    /// <param name="builder">Накопитель конфигурации.</param>
    /// <param name="families">Пары «заявленное семейство — донор».</param>
    /// <remarks>
    /// ★ Без этого <c lang="text">sans-serif</c>, <c lang="text">serif</c> и <c lang="text">monospace</c> после сокрытия системных
    /// шрифтов уходили в первый попавшийся оставшийся — вплоть до экзотических письменностей.
    /// Замер это показывает сразу: ширина строки в обобщённом семействе скачет, а она видна не
    /// только измерением, но и обычной вёрсткой.
    ///
    /// Что во что разрешается — свойство платформы, и браузер здесь лишь следует системе.
    /// </remarks>
    private static void AppendGenericBinding(StringBuilder builder, (string Family, string Fallback)[] families)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (family, _) in families) available.Add(family);

        AppendGeneric(builder, available, "sans-serif", ["Helvetica Neue", "Helvetica", "Arial", "Segoe UI"]);
        AppendGeneric(builder, available, "serif", ["Times", "Times New Roman", "Georgia", "Cambria"]);
        AppendGeneric(builder, available, "monospace", ["Menlo", "Consolas", "Courier New", "Courier"]);

        // ★ Шрифт оболочки. Без привязки 'system-ui' и системные CSS-шрифты ('caption', 'menu',
        // 'message-box', 'icon') резолвились в родной линуксовый ответ — замер при заявленном
        // Windows давал 'Arial 16px', тогда как оболочка Windows рисует ими 'Segoe UI 12px',
        // а macOS — свой системный шрифт. Читается двумя строками CSS, без измерений.
        AppendGeneric(builder, available, "system-ui", ["Segoe UI", "SF Pro Text", "Helvetica Neue"]);
        AppendGeneric(builder, available, "ui-sans-serif", ["Segoe UI", "SF Pro Text", "Helvetica Neue"]);
        AppendGeneric(builder, available, "ui-serif", ["Times New Roman", "Cambria", "Georgia"]);
        AppendGeneric(builder, available, "ui-monospace", ["Consolas", "Menlo", "Courier New"]);
    }

    /// <summary>
    /// Привязывает одно обобщённое семейство к первому доступному из предпочтений.
    /// </summary>
    /// <param name="builder">Накопитель конфигурации.</param>
    /// <param name="available">Семейства, объявленные для платформы.</param>
    /// <param name="generic">Обобщённое семейство.</param>
    /// <param name="preferences">Предпочтения в порядке убывания.</param>
    private static void AppendGeneric(StringBuilder builder, HashSet<string> available, string generic, string[] preferences)
    {
        foreach (var preference in preferences)
        {
            if (!available.Contains(preference)) continue;

            builder.AppendLine("  <alias>");
            builder.AppendLine($"    <family>{generic}</family>");
            builder.AppendLine("    <prefer>");
            builder.AppendLine($"      <family>{preference}</family>");
            builder.AppendLine("    </prefer>");
            builder.AppendLine("  </alias>");

            return;
        }
    }

    /// <summary>
    /// Прячет от браузера семейства, которых на заявленной платформе быть не может.
    /// </summary>
    /// <param name="builder">Накопитель конфигурации.</param>
    /// <remarks>
    /// Донор не прячется: он и есть то, чем отрисовывается заявленное семейство, и без него
    /// подстановка перестала бы работать. Прячется всё остальное из списка — то, что на
    /// настоящей Windows или macOS не встречается ни при какой установке.
    ///
    /// Отклонение идёт по имени семейства на этапе выбора: fontconfig просто не включает такой
    /// шрифт в набор, и страница его не находит — ни перечислением, ни измерением ширины.
    /// </remarks>
    private static void AppendFamilyHiding(StringBuilder builder, string? declaredPlatform)
    {
        // Доноры прятать не нужно и НЕЛЬЗЯ: они уже переименованы на этапе сканирования и под
        // своим именем не существуют, а отклонение по имени убрало бы сам файл вместе с
        // подстановкой.
        var hidden = new List<string>(LinuxOnlyFamilies);

        // ★ У Android часть «линуксовых» семейств — СВОИ. Noto и Droid стоят на телефоне штатно,
        // и прятать их там значит создавать нехватку там, где её быть не должно: отпечаток
        // телефона без Noto так же неправдоподобен, как iPhone с DejaVu.
        if (string.Equals(declaredPlatform, "Android", StringComparison.Ordinal))
        {
            hidden.RemoveAll(static family =>
                family.StartsWith("Noto", StringComparison.Ordinal)
                || family.StartsWith("Droid", StringComparison.Ordinal));
        }

        if (hidden.Count is 0) return;

        builder.AppendLine("  <selectfont>");
        builder.AppendLine("    <rejectfont>");

        foreach (var family in hidden)
        {
            builder.AppendLine("      <pattern>");
            builder.AppendLine($"        <patelt name=\"family\"><string>{family}</string></patelt>");
            builder.AppendLine("      </pattern>");
        }

        builder.AppendLine("    </rejectfont>");
        builder.AppendLine("  </selectfont>");
    }

    /// <summary>
    /// Строит конфигурацию fontconfig для заявленной платформы либо возвращает <see langword="null"/>,
    /// если подменять нечего.
    /// </summary>
    internal static string? Build(Device? device, string profilePath)
    {
        var families = device?.ClientHints?.Platform switch
        {
            "Windows" => WindowsFamilies,
            "macOS" => MacFamilies,
            "iOS" => IosFamilies,
            "Android" => AndroidFamilies,
            _ => null,
        };

        if (families is null)
            return null;

        // ★ Копии доноров кладём В ПРОФИЛЬ и переименовываем при сканировании НАШЕГО каталога.
        // Иначе донора не спрятать: отклонить его по имени нельзя — вместе с именем пропадает и
        // файл, а с ним подстановка (проверено: `Helvetica Neue` уходил на `Noto Sans`).
        // Собственный каталог решает это разом — системный `Liberation Sans` отклоняется, а его
        // копия существует уже под заявленным именем.
        var donorFiles = CopyDonorFonts(families, profilePath);
        if (donorFiles.Count is 0) return null;

        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0"?>""");
        builder.AppendLine("""<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">""");
        builder.AppendLine("<fontconfig>");

        // ★ Системный конфиг НЕ подключается вовсе — и это принципиально.
        //
        // Пока он подключался, странице были видны все шрифты машины, а прятались лишь те, что
        // перечислены списком. Списком такое не закрыть: на обычной рабочей машине стоят сотни
        // семейств (Nerd Fonts, сирийские, Fira, Adwaita), и любое из них на «айфоне» невозможно.
        // Замер независимым анализатором на профиле iPhone показывал «DejaVu Sans» и
        // «Liberation Mono» прямо в списке шрифтов.
        //
        // Поэтому источник шрифтов ровно один — собственный каталог профиля с донорами,
        // переименованными в заявленные семейства. Страница видит СТОЛЬКО ЖЕ и ТО ЖЕ, что и на
        // настоящем устройстве, а не набор этой машины.
        builder.AppendLine($"  <dir>{IOPath.Combine(profilePath, FontsDirectoryName)}</dir>");
        builder.AppendLine($"  <cachedir>{IOPath.Combine(profilePath, FontsDirectoryName, "cache")}</cachedir>");

        foreach (var (path, family) in donorFiles)
        {
            builder.AppendLine("  <match target=\"scan\">");
            builder.AppendLine($"    <test name=\"file\"><string>{path}</string></test>");
            builder.AppendLine($"    <edit name=\"family\" mode=\"assign\"><string>{family}</string></edit>");
            builder.AppendLine("  </match>");
        }

        // ★ Донор ПЕРЕИМЕНОВЫВАЕТСЯ, а не просто получает псевдоним. Разница решающая: псевдоним
        // ДОБАВЛЯЕТ заявленное семейство, оставляя донора на месте, и страница видела сразу оба —
        // рядом с `Helvetica Neue` стояли `Liberation Sans` и `DejaVu Sans`, которых на макбуке
        // не бывает. Переименование на этапе сканирования убирает донора под его собственным
        // именем: остаётся ровно то семейство, которое мы заявляем.
        //
        // ★ Псевдонимов между заявленными семействами БОЛЬШЕ НЕТ.
        //
        // Пока один донор обслуживал несколько имён, первое становилось основным, а остальные
        // ссылались на него псевдонимом — и запрос `Verdana` разрешался в файл с именем
        // «Segoe UI Symbol». Именно так подмена и читалась: `fc-match` под живым конфигом
        // показывал у Tahoma, Verdana, Georgia и Times New Roman ЧУЖОЕ имя семейства, тогда как
        // на настоящей Windows каждое имеет собственный файл. Теперь каждому семейству копируется
        // свой экземпляр донора (см. CopyDonorFonts), и правило сканирования по ПУТИ файла даёт
        // ему собственное имя — псевдонимы не нужны вовсе.
        //
        // Переименование по имени донора здесь тоже не требуется: доноры лежат только в нашем
        // каталоге и уже переименованы пофайлово, под собственными именами их не существует.

        AppendGenericBinding(builder, families);
        AppendFamilyHiding(builder, device?.ClientHints?.Platform);
        AppendRenderingDefaults(builder);

        builder.AppendLine("</fontconfig>");
        return builder.ToString();
    }

    /// <summary>
    /// Правила отрисовки: слабый хинтинг и сглаживание, как в системной настройке.
    /// </summary>
    /// <remarks>
    /// ★ Наш конфиг подменяет системный целиком, и вместе со списком шрифтов терялись правила
    /// отрисовки: fontconfig брал умолчание с полным хинтингом. Полный хинтинг прибивает глифы к
    /// пиксельной сетке, и ширина текста выходит ЦЕЛОЙ — тогда как Chrome считает компоновку в
    /// 1/64 пикселя, и <c lang="text">getBoundingClientRect</c> почти никогда не даёт целого числа.
    /// Замер: 13 семейств из 13 дали целую ширину при заявленном Windows против 0 из 13 без подмены,
    /// причём <c lang="text">measureText</c> в том же документе оставался дробным — два пути
    /// измерения одного шрифта расходились характером числа. Проверяется одной строкой
    /// <c lang="text">Number.isInteger(rect.width)</c>.
    /// </remarks>
    private static void AppendRenderingDefaults(StringBuilder builder)
    {
        builder.AppendLine("  <match target=\"font\">");
        builder.AppendLine("    <edit name=\"antialias\" mode=\"assign\"><bool>true</bool></edit>");
        builder.AppendLine("    <edit name=\"hinting\" mode=\"assign\"><bool>true</bool></edit>");
        builder.AppendLine("    <edit name=\"hintstyle\" mode=\"assign\"><const>hintslight</const></edit>");
        builder.AppendLine("    <edit name=\"rgba\" mode=\"assign\"><const>rgb</const></edit>");
        builder.AppendLine("    <edit name=\"lcdfilter\" mode=\"assign\"><const>lcddefault</const></edit>");
        builder.AppendLine("  </match>");
    }
}
