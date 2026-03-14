using Atom.SourceGeneration.PipeWire;
using Atom.SourceGeneration.Tests;

namespace Atom.SourceGeneration.PipeWire.Tests;

[Parallelizable(ParallelScope.All)]
public class SpaHeaderParserTests(ILogger logger) : BenchmarkTests<SpaHeaderParserTests>(logger)
{
    public SpaHeaderParserTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Парсинг enum с авто-инкрементом"), Benchmark]
    public void AutoIncrementTest()
    {
        const string header = """
            enum spa_test {
                TEST_A,
                TEST_B,
                TEST_C,
            };
            """;

        var (name, entries) = SpaHeaderParser.Parse(header);

        Assert.That(name, Is.EqualTo("spa_test"));
        Assert.That(entries, Has.Count.EqualTo(3));
        Assert.That(entries[0], Is.EqualTo(new SpaHeaderParser.EnumEntry("TEST_A", 0)));
        Assert.That(entries[1], Is.EqualTo(new SpaHeaderParser.EnumEntry("TEST_B", 1)));
        Assert.That(entries[2], Is.EqualTo(new SpaHeaderParser.EnumEntry("TEST_C", 2)));
    }

    [TestCase(TestName = "Парсинг enum с hex-значениями"), Benchmark]
    public void HexValuesTest()
    {
        const string header = """
            enum spa_hex {
                HEX_START = 0x100,
                HEX_NEXT,
                HEX_JUMP = 0xFF00,
                HEX_AFTER,
            };
            """;

        var (_, entries) = SpaHeaderParser.Parse(header);

        Assert.That(entries, Has.Count.EqualTo(4));
        Assert.That(entries[0].Value, Is.EqualTo(0x100u));
        Assert.That(entries[1].Value, Is.EqualTo(0x101u));
        Assert.That(entries[2].Value, Is.EqualTo(0xFF00u));
        Assert.That(entries[3].Value, Is.EqualTo(0xFF01u));
    }

    [TestCase(TestName = "Парсинг enum с decimal-значениями"), Benchmark]
    public void DecimalValuesTest()
    {
        const string header = """
            enum spa_dec {
                DEC_A = 10,
                DEC_B,
                DEC_C = 42,
            };
            """;

        var (_, entries) = SpaHeaderParser.Parse(header);

        Assert.That(entries, Has.Count.EqualTo(3));
        Assert.That(entries[0].Value, Is.EqualTo(10u));
        Assert.That(entries[1].Value, Is.EqualTo(11u));
        Assert.That(entries[2].Value, Is.EqualTo(42u));
    }

    [TestCase(TestName = "Пропуск алиасов (ссылки на другие члены)"), Benchmark]
    public void SkipAliasesTest()
    {
        const string header = """
            enum spa_alias {
                ALIAS_A = 0x10,
                ALIAS_B,
                ALIAS_C = ALIAS_A,
                ALIAS_D = ALIAS_B,
            };
            """;

        var (_, entries) = SpaHeaderParser.Parse(header);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0].Name, Is.EqualTo("ALIAS_A"));
        Assert.That(entries[1].Name, Is.EqualTo("ALIAS_B"));
    }

    [TestCase(TestName = "Удаление однострочных C-комментариев"), Benchmark]
    public void SingleLineCommentsTest()
    {
        const string header = """
            enum spa_comment {
                CMT_A,      /** comment */
                CMT_B,      /* another */
                CMT_C = 0x5, /** since 0.3.65 */
            };
            """;

        var (_, entries) = SpaHeaderParser.Parse(header);

        Assert.That(entries, Has.Count.EqualTo(3));
        Assert.That(entries[0].Name, Is.EqualTo("CMT_A"));
        Assert.That(entries[1].Name, Is.EqualTo("CMT_B"));
        Assert.That(entries[2], Is.EqualTo(new SpaHeaderParser.EnumEntry("CMT_C", 5)));
    }

    [TestCase(TestName = "Удаление многострочных C-комментариев"), Benchmark]
    public void MultiLineCommentsTest()
    {
        const string header = """
            enum spa_multi {
                MULTI_A = 0x100,
                MULTI_B,        /**< control stream, data contains
                                  *  spa_pod_sequence with control info. */
                MULTI_C,
            };
            """;

        var (_, entries) = SpaHeaderParser.Parse(header);

        Assert.That(entries, Has.Count.EqualTo(3));
        Assert.That(entries[0].Value, Is.EqualTo(0x100u));
        Assert.That(entries[1].Value, Is.EqualTo(0x101u));
        Assert.That(entries[2].Value, Is.EqualTo(0x102u));
    }

    [TestCase(TestName = "Пустой файл — нет enum"), Benchmark]
    public void EmptyFileTest()
    {
        var (name, entries) = SpaHeaderParser.Parse("");

        Assert.That(name, Is.Empty);
        Assert.That(entries, Is.Empty);
    }

    [TestCase(TestName = "Файл без enum — только комментарии"), Benchmark]
    public void NoEnumTest()
    {
        const string header = """
            /* This file has no enum */
            #define SOME_VALUE 42
            """;

        var (name, entries) = SpaHeaderParser.Parse(header);

        Assert.That(name, Is.Empty);
        Assert.That(entries, Is.Empty);
    }

    [TestCase(TestName = "Enum только с алиасами — пустой результат"), Benchmark]
    public void OnlyAliasesTest()
    {
        const string header = """
            enum spa_only_alias {
                ALIAS_X = ALIAS_Y,
                ALIAS_Z = ALIAS_W,
            };
            """;

        var (name, entries) = SpaHeaderParser.Parse(header);

        Assert.That(name, Is.EqualTo("spa_only_alias"));
        Assert.That(entries, Is.Empty);
    }

    [TestCase(TestName = "Строчные комментарии // игнорируются"), Benchmark]
    public void CppLineCommentsTest()
    {
        const string header = """
            enum spa_cpp {
                // section start
                CPP_A = 0x1,
                CPP_B, // next value
            };
            """;

        var (_, entries) = SpaHeaderParser.Parse(header);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries[0], Is.EqualTo(new SpaHeaderParser.EnumEntry("CPP_A", 1)));
        Assert.That(entries[1], Is.EqualTo(new SpaHeaderParser.EnumEntry("CPP_B", 2)));
    }
}
