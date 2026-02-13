using Atom.Algorithms.Text;

namespace Atom.Text.Tests;

/// <summary>
/// Тесты для TextExtensions и SubstringSearchAlgorithm.
/// </summary>
[TestFixture, Parallelizable(ParallelScope.All)]
public class TextExtensionsTests(ILogger logger) : BenchmarkTests<TextExtensionsTests>(logger)
{
    public TextExtensionsTests() : this(ConsoleLogger.Unicode) { }

    #region CountOf with Algorithm Instance Tests

    [TestCase(TestName = "CountOf: с экземпляром алгоритма KMP")]
    public void CountOfWithKmpInstance()
    {
        var algorithm = new KmpAlgorithm();
        var result = "hello world hello".CountOf("hello", StringComparison.Ordinal, algorithm);
        Assert.That(result, Is.EqualTo(2));
    }

    [TestCase(TestName = "CountOf: с экземпляром алгоритма без StringComparison")]
    public void CountOfWithAlgorithmNoComparison()
    {
        var algorithm = new BoyerMooreAlgorithm();
        var result = "test string test".CountOf("test", algorithm);
        Assert.That(result, Is.EqualTo(2));
    }

    #endregion

    #region CountOf with Generic Algorithm Tests

    [TestCase(TestName = "CountOf<KmpAlgorithm>: базовый тест")]
    public void CountOfGenericKmp()
    {
        var result = "abcabcabc".CountOf<KmpAlgorithm>("abc", StringComparison.Ordinal);
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf<BoyerMooreAlgorithm>: базовый тест")]
    public void CountOfGenericBoyerMoore()
    {
        var result = "abcabcabc".CountOf<BoyerMooreAlgorithm>("abc");
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf<RabinKarpAlgorithm>: базовый тест")]
    public void CountOfGenericRabinKarp()
    {
        var result = "abcabcabc".CountOf<RabinKarpAlgorithm>("abc");
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf<ZAlgorithm>: базовый тест")]
    public void CountOfGenericZ()
    {
        var result = "abcabcabc".CountOf<ZAlgorithm>("abc");
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf<AhoCorasickAlgorithm>: базовый тест")]
    public void CountOfGenericAhoCorasick()
    {
        var result = "abcabcabc".CountOf<AhoCorasickAlgorithm>("abc");
        Assert.That(result, Is.EqualTo(3));
    }

    #endregion

    #region CountOf with SubstringSearchAlgorithm Enum Tests

    [TestCase(TestName = "CountOf: SubstringSearchAlgorithm.KMP")]
    public void CountOfWithEnumKmp()
    {
        var result = "hello hello hello".CountOf("hello", StringComparison.Ordinal, SubstringSearchAlgorithm.KMP);
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf: SubstringSearchAlgorithm.BoyerMoore")]
    public void CountOfWithEnumBoyerMoore()
    {
        var result = "hello hello hello".CountOf("hello", StringComparison.Ordinal, SubstringSearchAlgorithm.BoyerMoore);
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf: SubstringSearchAlgorithm.RabinKarp")]
    public void CountOfWithEnumRabinKarp()
    {
        var result = "hello hello hello".CountOf("hello", StringComparison.Ordinal, SubstringSearchAlgorithm.RabinKarp);
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf: SubstringSearchAlgorithm.AhoCorasick")]
    public void CountOfWithEnumAhoCorasick()
    {
        var result = "hello hello hello".CountOf("hello", StringComparison.Ordinal, SubstringSearchAlgorithm.AhoCorasick);
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf: SubstringSearchAlgorithm.Z")]
    public void CountOfWithEnumZ()
    {
        var result = "hello hello hello".CountOf("hello", StringComparison.Ordinal, SubstringSearchAlgorithm.Z);
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf: с enum без StringComparison")]
    public void CountOfWithEnumNoComparison()
    {
        var result = "test test test".CountOf("test", SubstringSearchAlgorithm.KMP);
        Assert.That(result, Is.EqualTo(3));
    }

    #endregion

    #region CountOf Default Overloads Tests

    [TestCase(TestName = "CountOf: дефолтный (без параметров)")]
    public void CountOfDefault()
    {
        var result = "abc abc abc".CountOf("abc");
        Assert.That(result, Is.EqualTo(3));
    }

    [TestCase(TestName = "CountOf: только StringComparison")]
    public void CountOfWithComparison()
    {
        var result = "ABC abc ABC".CountOf("abc", StringComparison.OrdinalIgnoreCase);
        Assert.That(result, Is.EqualTo(3));
    }

    #endregion

    #region CountOf Single Character Tests

    [TestCase(TestName = "CountOf: одиночный символ")]
    public void CountOfSingleChar()
    {
        var result = "aabbccaa".CountOf('a');
        Assert.That(result, Is.EqualTo(4));
    }

    [TestCase(TestName = "CountOf: одиночный символ с регистронезависимым сравнением")]
    public void CountOfSingleCharCaseInsensitive()
    {
        var result = "AaBbAa".CountOf('a', StringComparison.OrdinalIgnoreCase);
        Assert.That(result, Is.EqualTo(4));
    }

    [TestCase(TestName = "CountOf: символ не найден")]
    public void CountOfSingleCharNotFound()
    {
        var result = "hello".CountOf('x');
        Assert.That(result, Is.Zero);
    }

    [TestCase(TestName = "CountOf: пустая строка для символа")]
    public void CountOfSingleCharEmptyString()
    {
        var result = "".CountOf('a');
        Assert.That(result, Is.Zero);
    }

    [TestCase(TestName = "CountOf: null символ")]
    public void CountOfNullChar()
    {
        var result = "hello".CountOf(char.MinValue);
        Assert.That(result, Is.Zero);
    }

    #endregion

    #region Contains with Algorithm Instance Tests

    [TestCase(TestName = "Contains: с экземпляром алгоритма")]
    public void ContainsWithAlgorithmInstance()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That("hello world".Contains("world", StringComparison.Ordinal, algorithm), Is.True);
            Assert.That("hello world".Contains("xyz", StringComparison.Ordinal, algorithm), Is.False);
        });
    }

    [TestCase(TestName = "Contains: с экземпляром алгоритма без StringComparison")]
    public void ContainsWithAlgorithmNoComparison()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.That("hello world".Contains("world", algorithm), Is.True);
    }

    #endregion

    #region Contains with Generic Algorithm Tests

    [TestCase(TestName = "Contains<KmpAlgorithm>: базовый тест")]
    public void ContainsGenericKmp()
    {
        Assert.Multiple(() =>
        {
            Assert.That("hello world".Contains<KmpAlgorithm>("world", StringComparison.Ordinal), Is.True);
            Assert.That("hello world".Contains<KmpAlgorithm>("WORLD", StringComparison.OrdinalIgnoreCase), Is.True);
        });
    }

    [TestCase(TestName = "Contains<BoyerMooreAlgorithm>: базовый тест")]
    public void ContainsGenericBoyerMoore() => Assert.That("hello world".Contains<BoyerMooreAlgorithm>("world"), Is.True);

    [TestCase(TestName = "Contains<RabinKarpAlgorithm>: базовый тест")]
    public void ContainsGenericRabinKarp() => Assert.That("hello world".Contains<RabinKarpAlgorithm>("world"), Is.True);

    [TestCase(TestName = "Contains<ZAlgorithm>: базовый тест")]
    public void ContainsGenericZ() => Assert.That("hello world".Contains<ZAlgorithm>("world"), Is.True);

    [TestCase(TestName = "Contains<AhoCorasickAlgorithm>: базовый тест")]
    public void ContainsGenericAhoCorasick() => Assert.That("hello world".Contains<AhoCorasickAlgorithm>("world"), Is.True);

    #endregion

    #region Contains with SubstringSearchAlgorithm Enum Tests

    [TestCase(TestName = "Contains: все алгоритмы через enum")]
    public void ContainsAllAlgorithmsViaEnum()
    {
        var source = "hello world";
        var pattern = "world";

        Assert.Multiple(() =>
        {
            Assert.That(source.Contains(pattern, StringComparison.Ordinal, SubstringSearchAlgorithm.KMP), Is.True);
            Assert.That(source.Contains(pattern, StringComparison.Ordinal, SubstringSearchAlgorithm.BoyerMoore), Is.True);
            Assert.That(source.Contains(pattern, StringComparison.Ordinal, SubstringSearchAlgorithm.RabinKarp), Is.True);
            Assert.That(source.Contains(pattern, StringComparison.Ordinal, SubstringSearchAlgorithm.AhoCorasick), Is.True);
            Assert.That(source.Contains(pattern, StringComparison.Ordinal, SubstringSearchAlgorithm.Z), Is.True);
        });
    }

    [TestCase(TestName = "Contains: с enum без StringComparison")]
    public void ContainsWithEnumNoComparison() => Assert.That("hello world".Contains("world", SubstringSearchAlgorithm.KMP), Is.True);

    #endregion

    #region Contains Default Overloads Tests

    [TestCase(TestName = "Contains: дефолтный")]
    public void ContainsDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That("hello world".Contains("world"), Is.True);
            Assert.That("hello world".Contains("xyz"), Is.False);
        });
    }

    [TestCase(TestName = "Contains: только StringComparison")]
    public void ContainsWithComparison() => Assert.That("Hello World".Contains("hello", StringComparison.OrdinalIgnoreCase), Is.True);

    #endregion

    #region Edge Cases Tests

    [TestCase(TestName = "Edge: пустая исходная строка")]
    public void EdgeEmptySource()
    {
        Assert.Multiple(() =>
        {
            Assert.That("".CountOf("test"), Is.Zero);
            Assert.That("".Contains("test"), Is.False);
        });
    }

    [TestCase(TestName = "Edge: пустой паттерн")]
    public void EdgeEmptyPattern()
    {
        Assert.Multiple(() =>
        {
            Assert.That("test".CountOf(""), Is.Zero);
            // Примечание: стандартное поведение .NET - string.Contains("") возвращает true
            // Это соответствует стандарту, пустая строка содержится в любой строке
            Assert.That("test".Contains(""), Is.True);
        });
    }

    [TestCase(TestName = "Edge: Unicode символы")]
    public void EdgeUnicode()
    {
        var source = "Привет мир! Hello world! Привет!";
        Assert.Multiple(() =>
        {
            Assert.That(source.CountOf("Привет"), Is.EqualTo(2));
            Assert.That(source.Contains("мир"), Is.True);
        });
    }

    [TestCase(TestName = "Edge: специальные символы")]
    public void EdgeSpecialChars()
    {
        var source = "line1\nline2\tline3\rline4";
        Assert.Multiple(() =>
        {
            Assert.That(source.Contains('\n'), Is.True);
            Assert.That(source.Contains('\t'), Is.True);
            Assert.That(source.Contains('\r'), Is.True);
        });
    }

    #endregion

    #region ToUnixStyleFormat Tests

    [TestCase(TestName = "ToUnixStyleFormat: null возвращает null")]
    public void ToUnixStyleFormatNull()
    {
        string? source = null;
        Assert.That(source.ToUnixStyleFormat(), Is.Null);
    }

    [TestCase(TestName = "ToUnixStyleFormat: пустая строка")]
    public void ToUnixStyleFormatEmpty() => Assert.That("".ToUnixStyleFormat(), Is.EqualTo(""));

    [TestCase(TestName = "ToUnixStyleFormat: строка без форматирования")]
    public void ToUnixStyleFormatNoFormatting()
    {
        var result = "hello world".ToUnixStyleFormat();
        Assert.That(result, Does.Contain("hello world"));
    }

    [TestCase(TestName = "ToUnixStyleFormat: удаление форматирования")]
    public void ToUnixStyleFormatRemoveFormatting()
    {
        var result = "[red]hello[/red]".ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo("hello"));
    }

    [TestCase(TestName = "ToUnixStyleFormat: вложенные теги с удалением")]
    public void ToUnixStyleFormatNestedTagsRemove()
    {
        // Используем реальные цвета которые поддерживаются форматтером
        var result = "[red][green]hello[/green][/red]".ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo("hello"));
    }

    [TestCase(TestName = "ToUnixStyleFormat: неизвестные теги сохраняются")]
    public void ToUnixStyleFormatUnknownTags()
    {
        var result = "[unknown]hello[/unknown]".ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Does.Contain("[unknown]"));
        Assert.That(result, Does.Contain("[/unknown]"));
    }

    [TestCase(TestName = "ToUnixStyleFormat: ANSI escape-коды добавляются")]
    public void ToUnixStyleFormatAnsiCodes()
    {
        var result = "[red]hello[/red]".ToUnixStyleFormat(removeFormatting: false);
        // Должен содержать ANSI escape-коды
        Assert.That(result, Does.Contain("\x1b["));
    }

    [TestCase(TestName = "ToUnixStyleFormat: с фоновым цветом")]
    public void ToUnixStyleFormatWithBackground()
    {
        var result = "[red:blue]hello[/red:blue]".ToUnixStyleFormat(removeFormatting: true);
        Assert.That(result, Is.EqualTo("hello"));
    }

    #endregion

    #region Consistency Tests

    [TestCase(TestName = "Консистентность: все методы возвращают одинаковые результаты")]
    public void ConsistencyAllMethodsSameResult()
    {
        var source = "the quick brown fox jumps over the lazy dog";
        var pattern = "the";

        var expectedCount = 2;

        Assert.Multiple(() =>
        {
            // Разные способы вызова CountOf
            Assert.That(source.CountOf(pattern), Is.EqualTo(expectedCount));
            Assert.That(source.CountOf(pattern, StringComparison.Ordinal), Is.EqualTo(expectedCount));
            Assert.That(source.CountOf<KmpAlgorithm>(pattern), Is.EqualTo(expectedCount));
            Assert.That(source.CountOf<BoyerMooreAlgorithm>(pattern), Is.EqualTo(expectedCount));
            Assert.That(source.CountOf(pattern, SubstringSearchAlgorithm.RabinKarp), Is.EqualTo(expectedCount));
            Assert.That(source.CountOf(pattern, new ZAlgorithm()), Is.EqualTo(expectedCount));
        });
    }

    #endregion

    #region NotSupportedException Tests

    [TestCase(TestName = "NotSupportedException: неверный enum")]
    public void NotSupportedEnumValue()
    {
        var invalidAlgorithm = (SubstringSearchAlgorithm)999;
        Assert.Throws<NotSupportedException>(() => "test".CountOf("t", StringComparison.Ordinal, invalidAlgorithm));
    }

    #endregion

    #region Stress Tests

    [TestCase(TestName = "Стресс-тест: много вызовов CountOf"), Benchmark]
    public void StressTestCountOf()
    {
        var source = string.Concat(Enumerable.Repeat("hello world ", 1000));

        for (var i = 0; i < 1000; i++)
        {
            _ = source.CountOf("hello");
        }

        Assert.Pass("Стресс-тест CountOf завершен успешно");
    }

    [TestCase(TestName = "Стресс-тест: много вызовов Contains"), Benchmark]
    public void StressTestContains()
    {
        var source = string.Concat(Enumerable.Repeat("hello world ", 1000));

        for (var i = 0; i < 1000; i++)
        {
            _ = source.Contains("world");
        }

        Assert.Pass("Стресс-тест Contains завершен успешно");
    }

    #endregion
}
