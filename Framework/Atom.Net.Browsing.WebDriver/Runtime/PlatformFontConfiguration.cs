using System.Text;

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
/// подсунутая браузеру через переменную окружения <c>FONTCONFIG_FILE</c>.
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
    ];

    /// <summary>
    /// Строит конфигурацию fontconfig для заявленной платформы либо возвращает <see langword="null"/>,
    /// если подменять нечего.
    /// </summary>
    internal static string? Build(Device? device)
    {
        var families = device?.ClientHints?.Platform switch
        {
            "Windows" => WindowsFamilies,
            "macOS" => MacFamilies,
            _ => null,
        };

        if (families is null)
            return null;

        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0"?>""");
        builder.AppendLine("""<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">""");
        builder.AppendLine("<fontconfig>");

        // Системный конфиг подключаем ПЕРВЫМ: без него у браузера не останется ни одного шрифта,
        // и страницы отрисуются пустыми прямоугольниками.
        builder.AppendLine("""  <include ignore_missing="yes">/etc/fonts/fonts.conf</include>""");

        foreach (var (family, fallback) in families)
        {
            // Двусторонняя привязка: `alias` объявляет семейство известным и задаёт замену, а
            // `match` с `binding="same"` заставляет отдавать подстановку и при точном запросе
            // семейства — иначе fontconfig считает неизвестное семейство отсутствующим.
            builder.AppendLine("  <alias binding=\"same\">");
            builder.AppendLine($"    <family>{family}</family>");
            builder.AppendLine("    <accept>");
            builder.AppendLine($"      <family>{fallback}</family>");
            builder.AppendLine("    </accept>");
            builder.AppendLine("  </alias>");

            builder.AppendLine("  <match target=\"pattern\">");
            builder.AppendLine($"    <test qual=\"any\" name=\"family\"><string>{family}</string></test>");
            builder.AppendLine($"    <edit name=\"family\" mode=\"assign\" binding=\"same\"><string>{fallback}</string></edit>");
            builder.AppendLine("  </match>");
        }

        builder.AppendLine("</fontconfig>");
        return builder.ToString();
    }
}
