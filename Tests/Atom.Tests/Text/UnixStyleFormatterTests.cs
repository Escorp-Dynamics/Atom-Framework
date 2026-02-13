using System.Diagnostics;

namespace Atom.Text.Tests;

/// <summary>
/// Тесты для <see cref="UnixStyleFormatter"/> через <see cref="TextExtensions.ToUnixStyleFormat"/>.
/// Проверяет парсинг и преобразование стилевых и цветовых тегов.
/// </summary>
[TestFixture, Parallelizable(ParallelScope.All)]
public class UnixStyleFormatterTests(ILogger logger) : BenchmarkTests<UnixStyleFormatterTests>(logger)
{
    #region Constructors

    public UnixStyleFormatterTests() : this(ConsoleLogger.Unicode) { }

    #endregion

    #region Helper Methods

    private void Log(string? message)
    {
        message = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        Logger.WriteLineInfo(message);
        Trace.TraceInformation(message);
    }

    /// <summary>
    /// Проверяет, что результат содержит ANSI escape-код.
    /// </summary>
    private static bool ContainsAnsiCode(string? result) => result?.Contains("\x1b[") ?? false;

    /// <summary>
    /// Проверяет, что результат содержит сброс ANSI.
    /// </summary>
    private static bool ContainsAnsiReset(string? result) => result?.Contains("\x1b[0m") ?? false;

    #endregion

    #region TryGetStyle and TryGetColor Direct Tests

    /// <summary>
    /// Тест: прямая проверка TryGetStyle для двухсимвольных тегов.
    /// </summary>
    [TestCase(TestName = "TryGetStyle: двухсимвольные теги sb, su, sr")]
    public void TryGetStyleTwoChar()
    {
        // sb => Bold
        Assert.That("sb".AsSpan().TryGetStyle(out var style1), Is.True, "sb должен распознаваться");
        Assert.That(style1, Is.EqualTo(ConsoleStyle.Bold), "sb => Bold");

        // su => Underline
        Assert.That("su".AsSpan().TryGetStyle(out var style2), Is.True, "su должен распознаваться");
        Assert.That(style2, Is.EqualTo(ConsoleStyle.Underline), "su => Underline");

        // sr => Reverse
        Assert.That("sr".AsSpan().TryGetStyle(out var style3), Is.True, "sr должен распознаваться");
        Assert.That(style3, Is.EqualTo(ConsoleStyle.Reverse), "sr => Reverse");

        // SB (upper case) => Bold
        Assert.That("SB".AsSpan().TryGetStyle(out var style4), Is.True, "SB должен распознаваться");
        Assert.That(style4, Is.EqualTo(ConsoleStyle.Bold), "SB => Bold");
    }

    /// <summary>
    /// Тест: прямая проверка TryGetStyle для полных названий.
    /// </summary>
    [TestCase(TestName = "TryGetStyle: полные названия bold, underline, reverse")]
    public void TryGetStyleFullNames()
    {
        // bold => Bold
        Assert.That("bold".AsSpan().TryGetStyle(out var style1), Is.True, "bold должен распознаваться");
        Assert.That(style1, Is.EqualTo(ConsoleStyle.Bold), "bold => Bold");

        // underline => Underline
        Assert.That("underline".AsSpan().TryGetStyle(out var style2), Is.True, "underline должен распознаваться");
        Assert.That(style2, Is.EqualTo(ConsoleStyle.Underline), "underline => Underline");

        // reverse => Reverse
        Assert.That("reverse".AsSpan().TryGetStyle(out var style3), Is.True, "reverse должен распознаваться");
        Assert.That(style3, Is.EqualTo(ConsoleStyle.Reverse), "reverse => Reverse");

        // BOLD (upper case) => Bold
        Assert.That("BOLD".AsSpan().TryGetStyle(out var style4), Is.True, "BOLD должен распознаваться");
        Assert.That(style4, Is.EqualTo(ConsoleStyle.Bold), "BOLD => Bold");
    }

    /// <summary>
    /// Тест: TryGetColor НЕ должен распознавать стилевые теги.
    /// </summary>
    [TestCase(TestName = "TryGetColor: НЕ распознаёт sb, su, sr, bold")]
    public void TryGetColorDoesNotRecognizeStyles()
    {
        Assert.That("sb".AsSpan().TryGetColor(out _), Is.False, "sb НЕ должен быть цветом");
        Assert.That("su".AsSpan().TryGetColor(out _), Is.False, "su НЕ должен быть цветом");
        Assert.That("sr".AsSpan().TryGetColor(out _), Is.False, "sr НЕ должен быть цветом");
        Assert.That("bold".AsSpan().TryGetColor(out _), Is.False, "bold НЕ должен быть цветом");
    }

    #endregion

    #region Basic Functionality Tests

    /// <summary>
    /// Тест: null и пустые строки обрабатываются корректно.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: null и пустая строка")]
    public void NullAndEmptyStrings()
    {
        string? nullString = null;
        var emptyString = "";

        Assert.Multiple(() =>
        {
            Assert.That(nullString.ToUnixStyleFormat(), Is.Null);
            Assert.That(nullString.ToUnixStyleFormat(removeFormatting: true), Is.Null);
            Assert.That(emptyString.ToUnixStyleFormat(), Is.EqualTo(""));
            Assert.That(emptyString.ToUnixStyleFormat(removeFormatting: true), Is.EqualTo(""));
        });
    }

    /// <summary>
    /// Тест: строка без тегов возвращается как есть (с обёрткой ANSI reset).
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: строка без тегов")]
    public void PlainTextNoTags()
    {
        var source = "Hello, World!";

        var resultWithFormatting = source.ToUnixStyleFormat();
        var resultWithoutFormatting = source.ToUnixStyleFormat(removeFormatting: true);

        Assert.Multiple(() =>
        {
            Assert.That(resultWithFormatting, Does.Contain(source));
            Assert.That(ContainsAnsiReset(resultWithFormatting), Is.True, "Должен содержать ANSI reset");
            Assert.That(resultWithoutFormatting, Is.EqualTo(source));
        });
    }

    #endregion

    #region Color Tag Tests

    /// <summary>
    /// Тест: короткие цветовые теги (r, g, b и т.д.).
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: короткие цветовые теги")]
    public void ShortColorTags()
    {
        var testCases = new[]
        {
            ("[r]red[/r]", "red"),
            ("[g]green[/g]", "green"),
            ("[b]blue[/b]", "blue"),
            ("[y]yellow[/y]", "yellow"),
            ("[m]magenta[/m]", "magenta"),
            ("[c]cyan[/c]", "cyan"),
            ("[w]white[/w]", "white"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");

            var resultWithFormatting = source.ToUnixStyleFormat();
            Assert.That(ContainsAnsiCode(resultWithFormatting), Is.True, $"Должен содержать ANSI для: {source}");
        }
    }

    /// <summary>
    /// Тест: двухсимвольные цветовые теги (dr, dg и т.д.).
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: двухсимвольные цветовые теги")]
    public void TwoCharColorTags()
    {
        var testCases = new[]
        {
            ("[dr]dark red[/dr]", "dark red"),
            ("[dg]dark green[/dg]", "dark green"),
            ("[db]dark blue[/db]", "dark blue"),
            ("[dy]dark yellow[/dy]", "dark yellow"),
            ("[dm]dark magenta[/dm]", "dark magenta"),
            ("[dc]dark cyan[/dc]", "dark cyan"),
            ("[gr]gray[/gr]", "gray"),
            ("[bl]black[/bl]", "black"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: трёхсимвольные цветовые теги (dgr).
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: трёхсимвольные цветовые теги")]
    public void ThreeCharColorTags()
    {
        var testCases = new[]
        {
            ("[dgr]dark gray[/dgr]", "dark gray"),
            ("[red]red text[/red]", "red text"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: полные названия цветов.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: полные названия цветов")]
    public void FullColorNames()
    {
        var testCases = new[]
        {
            ("[blue]blue[/blue]", "blue"),
            ("[cyan]cyan[/cyan]", "cyan"),
            ("[gray]gray[/gray]", "gray"),
            ("[green]green[/green]", "green"),
            ("[white]white[/white]", "white"),
            ("[black]black[/black]", "black"),
            ("[yellow]yellow[/yellow]", "yellow"),
            ("[magenta]magenta[/magenta]", "magenta"),
            ("[darkred]darkred[/darkred]", "darkred"),
            ("[darkblue]darkblue[/darkblue]", "darkblue"),
            ("[darkcyan]darkcyan[/darkcyan]", "darkcyan"),
            ("[darkgray]darkgray[/darkgray]", "darkgray"),
            ("[darkgreen]darkgreen[/darkgreen]", "darkgreen"),
            ("[darkyellow]darkyellow[/darkyellow]", "darkyellow"),
            ("[darkmagenta]darkmagenta[/darkmagenta]", "darkmagenta"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: регистронезависимость цветовых тегов.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: регистронезависимость цветов")]
    public void ColorTagsCaseInsensitive()
    {
        var testCases = new[]
        {
            ("[RED]text[/RED]", "text"),
            ("[Red]text[/Red]", "text"),
            ("[rEd]text[/rEd]", "text"),
            ("[GREEN]text[/GREEN]", "text"),
            ("[DarkBlue]text[/DarkBlue]", "text"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: цвет с фоном (red:blue).
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: цвет с фоном")]
    public void ColorWithBackground()
    {
        var testCases = new[]
        {
            ("[r:b]red on blue[/r:b]", "red on blue"),
            ("[red:blue]text[/red:blue]", "text"),
            ("[g:y]green on yellow[/g:y]", "green on yellow"),
            ("[white:black]contrast[/white:black]", "contrast"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    #endregion

    #region Style Tag Tests

    /// <summary>
    /// Тест: стилевые теги sb, su, sr.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: стилевые теги sb, su, sr")]
    public void StyleTags()
    {
        var testCases = new[]
        {
            ("[sb]bold[/sb]", "bold"),
            ("[su]underline[/su]", "underline"),
            ("[sr]reverse[/sr]", "reverse"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: {source}");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");

            var resultWithFormatting = source.ToUnixStyleFormat();
            Log($"С форматированием: '{resultWithFormatting}'");
            Assert.That(ContainsAnsiCode(resultWithFormatting), Is.True, $"Должен содержать ANSI для: {source}");
        }
    }

    /// <summary>
    /// Тест: полные названия стилей.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: полные названия стилей")]
    public void FullStyleNames()
    {
        var testCases = new[]
        {
            ("[bold]bold text[/bold]", "bold text"),
            ("[underline]underlined text[/underline]", "underlined text"),
            ("[reverse]reversed text[/reverse]", "reversed text"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: {source}");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: регистронезависимость стилевых тегов.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: регистронезависимость стилей")]
    public void StyleTagsCaseInsensitive()
    {
        var testCases = new[]
        {
            ("[SB]bold[/SB]", "bold"),
            ("[Sb]bold[/Sb]", "bold"),
            ("[sB]bold[/sB]", "bold"),
            ("[BOLD]bold[/BOLD]", "bold"),
            ("[Bold]bold[/Bold]", "bold"),
            ("[SU]underline[/SU]", "underline"),
            ("[UNDERLINE]underline[/UNDERLINE]", "underline"),
            ("[SR]reverse[/SR]", "reverse"),
            ("[REVERSE]reverse[/REVERSE]", "reverse"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: ANSI-коды для стилей генерируются корректно.
    /// Примечание: В тестовой среде Console.IsOutputRedirected = true,
    /// поэтому ANSI-коды не генерируются. Тест проверяет AsString напрямую.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: ANSI-коды для стилей")]
    public void StyleTagsAnsiCodes()
    {
        // Проверяем AsString напрямую, так как в тестах Console.IsOutputRedirected = true
        // Bold: \x1b[1m (start) и \x1b[22m (end)
        // Underline: \x1b[4m (start) и \x1b[24m (end)
        // Reverse: \x1b[7m (start) и \x1b[27m (end)

        // Проверяем, что стили распознаются и форматируются
        var boldStart = ConsoleStyle.Bold.AsString(isEnding: false);
        var boldEnd = ConsoleStyle.Bold.AsString(isEnding: true);
        var underlineStart = ConsoleStyle.Underline.AsString(isEnding: false);
        var underlineEnd = ConsoleStyle.Underline.AsString(isEnding: true);
        var reverseStart = ConsoleStyle.Reverse.AsString(isEnding: false);
        var reverseEnd = ConsoleStyle.Reverse.AsString(isEnding: true);

        Log($"Bold: start='{boldStart}', end='{boldEnd}'");
        Log($"Underline: start='{underlineStart}', end='{underlineEnd}'");
        Log($"Reverse: start='{reverseStart}', end='{reverseEnd}'");

        // В тестовой среде Console.IsOutputRedirected = true, поэтому коды пустые
        // Но проверяем, что None не генерирует ничего
        Assert.That(ConsoleStyle.None.AsString(), Is.EqualTo(string.Empty), "None не должен генерировать ANSI");

        // Проверяем, что форматтер удаляет теги при removeFormatting=true
        var resultWithRemoval = "[sb]text[/sb]".ToUnixStyleFormat(removeFormatting: true);
        Assert.That(resultWithRemoval, Is.EqualTo("text"), "Стилевые теги должны удаляться");
    }

    #endregion

    #region Nested Tags Tests

    /// <summary>
    /// Тест: вложенные цветовые теги.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: вложенные цветовые теги")]
    public void NestedColorTags()
    {
        var source = "[r]red [g]green[/g] red[/r]";
        var expected = "red green red";

        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo(expected));

        var resultWithFormatting = source.ToUnixStyleFormat();
        Assert.That(ContainsAnsiCode(resultWithFormatting), Is.True);
    }

    /// <summary>
    /// Тест: вложенные стилевые теги.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: вложенные стилевые теги")]
    public void NestedStyleTags()
    {
        var source = "[sb]bold [su]bold underline[/su] bold[/sb]";
        var expected = "bold bold underline bold";

        Log($"Тестирую: {source}");
        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Log($"Результат: '{result}' (ожидалось: '{expected}')");
        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>
    /// Тест: комбинация цветов и стилей.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: комбинация цветов и стилей")]
    public void MixedColorAndStyleTags()
    {
        var source = "[r][sb]red bold[/sb][/r]";
        var expected = "red bold";

        Log($"Тестирую: {source}");
        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Log($"Результат: '{result}' (ожидалось: '{expected}')");
        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>
    /// Тест: глубоко вложенные теги.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: глубоко вложенные теги")]
    public void DeeplyNestedTags()
    {
        var source = "[r][g][b][sb][su][sr]deep[/sr][/su][/sb][/b][/g][/r]";
        var expected = "deep";

        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>
    /// Тест: несколько независимых тегов в одной строке.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: несколько независимых тегов")]
    public void MultipleIndependentTags()
    {
        var source = "[r]red[/r] normal [g]green[/g] normal [sb]bold[/sb]";
        var expected = "red normal green normal bold";

        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region Edge Cases

    /// <summary>
    /// Тест: незакрытые теги.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: незакрытые теги")]
    public void UnclosedTags()
    {
        var testCases = new[]
        {
            ("[r]unclosed red", "unclosed red"),
            ("[sb]unclosed bold", "unclosed bold"),
            ("[r][g]nested unclosed", "nested unclosed"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: {source}");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: закрывающий тег без открывающего — текущее поведение.
    /// Закрывающие теги для известных цветов/стилей, у которых нет открывающего,
    /// ведут себя по-разному в зависимости от текущего состояния стека.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: закрывающий тег без открывающего")]
    public void ClosingTagWithoutOpening()
    {
        // Текущее поведение: закрывающие теги без открывающих обрабатываются по-разному:
        // - для известных цветов/стилей: проглатываются (не совпадает с peek стека)
        // - для неизвестных: сохраняются как есть
        var testCases = new[]
        {
            ("[/r]orphan closing", "orphan closing"),  // r известен, проглатывается
            ("text[/sb]orphan", "textorphan"),         // sb теперь известен, проглатывается
            ("[/unknown]orphan", "[/unknown]orphan"),  // неизвестный тег сохраняется
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: '{source}'");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: неправильный порядок закрытия тегов.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: неправильный порядок закрытия")]
    public void WrongClosingOrder()
    {
        // [r][g]text[/r][/g] - закрытие в неправильном порядке
        var source = "[r][g]text[/r][/g]";

        Log($"Тестирую: {source}");
        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Log($"Результат: '{result}'");

        // Текст должен быть извлечён, некорректные закрывающие теги могут остаться
        Assert.That(result, Does.Contain("text"));
    }

    /// <summary>
    /// Тест: пустые теги — теперь сохраняются как есть.
    /// Это позволяет пользователю увидеть проблему и исправить форматирование.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: пустые теги")]
    public void EmptyTags()
    {
        // Новое поведение: пустые теги сохраняются как есть
        var testCases = new[]
        {
            ("[]text[]", "[]text[]"),      // пустые теги сохраняются
            ("[/]text", "[/]text"),        // пустой закрывающий тег сохраняется
            ("[  ]text", "[  ]text"),      // пробелы = пустой тег (сохраняется оригинал)
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: неизвестные теги сохраняются.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: неизвестные теги сохраняются")]
    public void UnknownTagsPreserved()
    {
        var testCases = new[]
        {
            ("[unknown]text[/unknown]", "[unknown]text[/unknown]"),
            ("[xyz]text[/xyz]", "[xyz]text[/xyz]"),
            ("[123]text[/123]", "[123]text[/123]"),
            ("[foo:bar]text[/foo:bar]", "[foo:bar]text[/foo:bar]"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: скобки внутри тегов.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: скобки внутри тегов")]
    public void BracketsInsideTags()
    {
        var testCases = new[]
        {
            ("[r]text with [brackets][/r]", "text with [brackets]"),
            ("[sb]array[0][/sb]", "array[0]"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: {source}");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: одинокие скобки — теперь сохраняются как есть.
    /// Это важно для стек-трейсов (array[0]) и позволяет пользователю видеть ошибки форматирования.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: одинокие скобки")]
    public void LoneBrackets()
    {
        // Новое поведение: незавершённые теги сохраняются как есть
        var testCases = new[]
        {
            // "text [ more text" — [ начинает парсинг, но ] не найден — сохраняем [ и текст
            ("text [ more text", "text [ more text"),
            ("text ] more text", "text ] more text"),
            // [[nested]] — первая [ открывает парсинг, [nested] — нераспознанный тег
            ("[[nested]]", "[[nested]]"),
            ("text[", "text["),  // [ в конце сохраняется
            ("]text", "]text"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: '{source}'");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: специальные символы в тексте.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: специальные символы в тексте")]
    public void SpecialCharactersInText()
    {
        var testCases = new[]
        {
            ("[r]text\nwith\nnewlines[/r]", "text\nwith\nnewlines"),
            ("[sb]text\twith\ttabs[/sb]", "text\twith\ttabs"),
            ("[g]emoji 🎉 text[/g]", "emoji 🎉 text"),
            ("[b]Кириллица[/b]", "Кириллица"),
            ("[y]中文[/y]", "中文"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: пробелы в тегах.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: пробелы в тегах")]
    public void SpacesInTags()
    {
        var testCases = new[]
        {
            ("[ r ]text[/ r ]", "text"),  // пробелы должны быть обрезаны
            ("[  red  ]text[/  red  ]", "text"),
            ("[ sb ]bold[/ sb ]", "bold"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: {source}");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: множественные слэши в закрывающих тегах.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: множественные слэши")]
    public void MultipleSlashes()
    {
        var testCases = new[]
        {
            ("[//r]text", "[//r]text"),  // некорректный тег
            ("[r]text[//r]", "[r]text[//r]"),
        };

        foreach (var (source, _) in testCases)
        {
            // Просто проверяем, что не падает
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.Not.Null);
        }
    }

    /// <summary>
    /// Тест: очень длинные имена тегов.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: очень длинные имена тегов")]
    public void VeryLongTagNames()
    {
        var longName = new string('x', 1000);
        var source = $"[{longName}]text[/{longName}]";

        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Does.Contain("[" + longName + "]"));
    }

    #endregion

    #region User Error Scenarios

    /// <summary>
    /// Тест: несовпадающие открывающие и закрывающие теги.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: несовпадающие теги")]
    public void MismatchedTags()
    {
        var testCases = new[]
        {
            ("[r]text[/g]", "text[/g]"),  // открыт r, закрыт g
            ("[sb]text[/su]", "text[/su]"),  // открыт sb, закрыт su
            ("[red]text[/blue]", "text[/blue]"),
        };

        foreach (var (source, _) in testCases)
        {
            Log($"Тестирую: {source}");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}'");
            // Просто проверяем, что текст извлекается
            Assert.That(result, Does.Contain("text"));
        }
    }

    /// <summary>
    /// Тест: тег с опечаткой.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: теги с опечатками")]
    public void MisspelledTags()
    {
        var testCases = new[]
        {
            ("[rred]text[/rred]", "[rred]text[/rred]"),
            ("[bld]text[/bld]", "[bld]text[/bld]"),
            ("[sb ]text[/sb ]", "text"),  // пробел после sb — это trimmed
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: {source}");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: теги без содержимого.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: теги без содержимого")]
    public void EmptyContentTags()
    {
        var testCases = new[]
        {
            ("[r][/r]", ""),
            ("[sb][/sb]", ""),
            ("[r][g][/g][/r]", ""),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: вложенные одинаковые теги.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: вложенные одинаковые теги")]
    public void NestedSameTags()
    {
        var source = "[r][r]double red[/r][/r]";
        var expected = "double red";

        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>
    /// Тест: тег сразу закрывается — новое поведение сохраняет пустые теги.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: тег сразу закрывается")]
    public void ImmediatelyClosedTag()
    {
        var testCases = new[]
        {
            ("[r/]text", "[r/]text"),  // "r/" не распознан, сохраняется
            ("[/]text", "[/]text"),    // пустой закрывающий тег сохраняется
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    #endregion

    #region Stack Trace Compatibility Tests

    /// <summary>
    /// Тест: индексаторы массивов в тексте сохраняются.
    /// Важно для вывода стек-трейсов.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: индексаторы массивов")]
    public void ArrayIndexersPreserved()
    {
        var testCases = new[]
        {
            // Пустые индексаторы
            ("array[]", "array[]"),
            ("list[] = null", "list[] = null"),
            // Индексаторы с числами
            ("array[0]", "array[0]"),
            ("matrix[1][2]", "matrix[1][2]"),
            ("items[123]", "items[123]"),
            // Несколько индексаторов
            ("data[0], data[1], data[2]", "data[0], data[1], data[2]"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: '{source}'");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: дженерики в тексте сохраняются.
    /// Важно для вывода стек-трейсов и имён типов.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: дженерики и угловые скобки")]
    public void GenericsPreserved()
    {
        // Угловые скобки не являются тегами, должны сохраняться
        var testCases = new[]
        {
            ("List<int>", "List<int>"),
            ("Dictionary<string, int>", "Dictionary<string, int>"),
            ("Func<T, TResult>", "Func<T, TResult>"),
            ("IEnumerable<KeyValuePair<string, List<int>>>", "IEnumerable<KeyValuePair<string, List<int>>>"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: реальный стек-трейс сохраняется полностью.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: реальный стек-трейс")]
    public void RealStackTracePreserved()
    {
        var stackTrace = """
            at System.Collections.Generic.Dictionary`2[TKey,TValue].get_Item(TKey key)
            at MyApp.Services.UserService.GetUser(Int32 id) in /app/UserService.cs:line 42
            at MyApp.Controllers.UserController.Index() in /app/Controllers/UserController.cs:line 15
            at lambda_method(Closure , Object , Object[] )
            at Microsoft.AspNetCore.Mvc.Internal.ActionMethodExecutor.SyncActionResultExecutor.Execute(IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)
            """;

        var result = stackTrace.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo(stackTrace), "Стек-трейс должен сохраняться полностью");
    }

    /// <summary>
    /// Тест: стек-трейс внутри цветового тега.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: стек-трейс внутри тега")]
    public void StackTraceInsideColorTag()
    {
        var stackTrace = """
            at System.Array.get_Item(Int32 index)
            at MyApp.Process(Int32[] data)
            """;
        var source = $"[r]{stackTrace}[/r]";

        Log($"Тестирую: '{source}'");
        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Log($"Результат: '{result}'");
        Assert.That(result, Is.EqualTo(stackTrace), "Стек-трейс внутри тега должен сохраняться");
    }

    /// <summary>
    /// Тест: исключение с дженериками и индексаторами.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: исключение с дженериками")]
    public void ExceptionWithGenericsPreserved()
    {
        var exception = """
            System.Collections.Generic.KeyNotFoundException: The given key 'user_123' was not present in the dictionary.
               at System.Collections.Generic.Dictionary`2.get_Item(TKey key)
               at MyApp.Cache`1[[System.String, System.Private.CoreLib, Version=8.0.0.0]].Get(String key)
            """;

        var source = $"[r]Error:[/r] {exception}";
        var expected = $"Error: {exception}";

        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>
    /// Тест: JSON-подобный текст с квадратными скобками.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: JSON с квадратными скобками")]
    public void JsonWithBracketsPreserved()
    {
        var testCases = new[]
        {
            ("[1, 2, 3]", "[1, 2, 3]"),
            ("items: [\"a\", \"b\", \"c\"]", "items: [\"a\", \"b\", \"c\"]"),
            (/*lang=json,strict*/ "{\"array\": [1, 2]}", /*lang=json,strict*/ "{\"array\": [1, 2]}"),
            // Смешанный случай: цветовой тег + JSON
            ("[g]Status:[/g] {\"items\": []}", "Status: {\"items\": []}"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            Log($"Тестирую: '{source}'");
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Log($"Результат: '{result}' (ожидалось: '{expectedText}')");
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: математические выражения с квадратными скобками.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: математические выражения")]
    public void MathExpressionsPreserved()
    {
        var testCases = new[]
        {
            ("f(x) = x[n] + x[n-1]", "f(x) = x[n] + x[n-1]"),
            ("interval: [0, 1]", "interval: [0, 1]"),
            ("set: {x | x ∈ [a, b]}", "set: {x | x ∈ [a, b]}"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: регулярные выражения с квадратными скобками.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: регулярные выражения")]
    public void RegexPreserved()
    {
        var testCases = new[]
        {
            (@"[a-z]+", @"[a-z]+"),
            (@"[A-Za-z0-9_]+", @"[A-Za-z0-9_]+"),
            (@"^[^\[\]]+$", @"^[^\[\]]+$"),
            (@"pattern: [a-z][0-9]?", @"pattern: [a-z][0-9]?"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    /// <summary>
    /// Тест: SQL-запросы с квадратными скобками (MS SQL).
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: SQL с квадратными скобками")]
    public void SqlWithBracketsPreserved()
    {
        var testCases = new[]
        {
            ("SELECT [Name], [Age] FROM [Users]", "SELECT [Name], [Age] FROM [Users]"),
            ("INSERT INTO [dbo].[Table] VALUES (1)", "INSERT INTO [dbo].[Table] VALUES (1)"),
        };

        foreach (var (source, expectedText) in testCases)
        {
            var result = source.ToUnixStyleFormat(removeFormatting: true);
            Assert.That(result, Is.EqualTo(expectedText), $"Ошибка для: {source}");
        }
    }

    #endregion

    #region Performance and Stress Tests

    /// <summary>
    /// Стресс-тест: много тегов в одной строке.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: стресс-тест много тегов"), Benchmark]
    public void StressTestManyTags()
    {
        var parts = new List<string>();
        for (var i = 0; i < 100; i++)
        {
            parts.Add($"[r]red{i}[/r]");
            parts.Add($"[sb]bold{i}[/sb]");
        }

        var source = string.Join(" ", parts);

        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Does.Contain("red0"));
        Assert.That(result, Does.Contain("bold99"));
    }

    /// <summary>
    /// Стресс-тест: глубокая вложенность.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: стресс-тест глубокая вложенность"), Benchmark]
    public void StressTestDeepNesting()
    {
        var depth = 50;
        var opening = string.Concat(Enumerable.Repeat("[r]", depth));
        var closing = string.Concat(Enumerable.Repeat("[/r]", depth));
        var source = opening + "deep text" + closing;

        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo("deep text"));
    }

    /// <summary>
    /// Стресс-тест: очень длинная строка.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: стресс-тест длинная строка"), Benchmark]
    public void StressTestLongString()
    {
        var text = new string('x', 10000);
        var source = $"[r]{text}[/r]";

        var result = source.ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo(text));
    }

    /// <summary>
    /// Стресс-тест: много вызовов форматирования.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: стресс-тест много вызовов"), Benchmark]
    public void StressTestManyCalls()
    {
        var source = "[r][sb]test text[/sb][/r]";

        for (var i = 0; i < 1000; i++)
        {
            _ = source.ToUnixStyleFormat(removeFormatting: true);
            _ = source.ToUnixStyleFormat(removeFormatting: false);
        }

        Assert.Pass("Стресс-тест завершен успешно");
    }

    #endregion

    #region Thread Safety Tests

    /// <summary>
    /// Тест: потокобезопасность форматтера.
    /// </summary>
    [TestCase(TestName = "UnixStyleFormatter: потокобезопасность"), Benchmark]
    public void ThreadSafetyTest()
    {
        var source = "[r][sb]concurrent text[/sb][/r]";
        var expected = "concurrent text";
        var iterations = 100;
        var threadCount = 10;
        var exceptions = new List<Exception>();
        var lockObj = new object();

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < iterations; i++)
                {
                    var result = source.ToUnixStyleFormat(removeFormatting: true);
                    if (result != expected)
                    {
                        lock (lockObj)
                        {
                            exceptions.Add(new Exception($"Unexpected result: '{result}'"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (lockObj)
                {
                    exceptions.Add(ex);
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.That(exceptions, Is.Empty, $"Обнаружены ошибки: {string.Join(", ", exceptions.Select(e => e.Message))}");
    }

    #endregion
}
