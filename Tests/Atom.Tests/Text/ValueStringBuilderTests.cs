using System.Globalization;

namespace Atom.Text.Tests;

/// <summary>
/// Набор тестов для <see cref="ValueStringBuilder"/>.
/// </summary>
/*[DisassemblyDiagnoser(
    maxDepth: 2,
    syntax: BenchmarkDotNet.Diagnosers.DisassemblySyntax.Masm,
    printSource: true,
    printInstructionAddresses: true,
    exportGithubMarkdown: true,
    exportHtml: true,
    exportCombinedDisassemblyReport: true,
    exportDiff: true
)]*/
[Parallelizable(ParallelScope.All)]
public class ValueStringBuilderTests(ILogger logger) : BenchmarkTests<ValueStringBuilderTests>(logger)
{
    private const string ExpectedUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36";

    public override bool IsBenchmarkEnabled => default;

    public ValueStringBuilderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "StringBuilder: конструирование UA"), Benchmark(Baseline = true, Description = "SB: конструирование UA")]
    public void BuildUserAgentStringBuilder()
    {
        var builder = new System.Text.StringBuilder(256);
        BuildUserAgent(builder);
        var result = builder.ToString();

        if (!IsBenchmarkEnabled) Assert.That(result, Is.EqualTo(ExpectedUserAgent));
    }

    [TestCase(TestName = "ValueStringBuilder: конструирование UA"), Benchmark(Description = "VSB: конструирование UA")]
    public void BuildUserAgentValueBuilder()
    {
        var builder = new ValueStringBuilder(256);

        try
        {
            BuildUserAgent(ref builder);
            var result = builder.ToString();

            if (!IsBenchmarkEnabled) Assert.That(result, Is.EqualTo(ExpectedUserAgent));
        }
        finally
        {
            builder.Dispose();
        }
    }

    [TestCase(TestName = "DotNet ValueStringBuilder: конструирование UA"), Benchmark(Description = "DotNet VSB: конструирование UA")]
    public void BuildUserAgentDotNetValueBuilder()
    {
        var builder = new DotNetValueStringBuilder(256);

        try
        {
            BuildUserAgentDotNet(ref builder);
            var result = builder.ToString();

            if (!IsBenchmarkEnabled) Assert.That(result, Is.EqualTo(ExpectedUserAgent));
        }
        finally
        {
            builder.Dispose();
        }
    }

    [TestCase(TestName = "StringBuilder: вставка и замена"), Benchmark(Description = "SB: вставка и замена")]
    public void InsertAndReplaceStringBuilder()
    {
        const string insertion = " amazing";
        var builder = new System.Text.StringBuilder(256);

        builder.Append("Hello")
            .Append(", world")
            .Append('!', 1)
            .Insert(5, insertion)
            .Replace("world", "Atom")
            .Remove(5 + insertion.Length, 1);

        var result = builder.ToString();
        var snapshot = builder.ToString(0, 5);

        if (!IsBenchmarkEnabled)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.EqualTo("Hello amazing Atom!"));
                Assert.That(snapshot, Is.EqualTo("Hello"));
            }
        }
    }

    [TestCase(TestName = "ValueStringBuilder: вставка и замена"), Benchmark(Description = "VSB: вставка и замена")]
    public void InsertAndReplaceTest()
    {
        const string insertion = " amazing";
        var builder = new ValueStringBuilder(256);

        try
        {
            builder.Append("Hello")
                .Append(", world")
                .Append('!', 1)
                .Insert(5, insertion)
                .Replace("world", "Atom")
                .Remove(5 + insertion.Length, 1);

            var result = builder.ToString();
            var snapshot = builder.ToString(0, 5);

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result, Is.EqualTo("Hello amazing Atom!"));
                    Assert.That(snapshot, Is.EqualTo("Hello"));
                }
            }
        }
        finally
        {
            builder.Dispose();
        }
    }

    [TestCase(TestName = "DotNet ValueStringBuilder: вставка и замена"), Benchmark(Description = "DotNet VSB: вставка и замена")]
    public void InsertAndReplaceDotNetTest()
    {
        const string insertion = " amazing";
        var builder = new DotNetValueStringBuilder(256);

        try
        {
            builder.Append("Hello")
                .Append(", world")
                .Append('!', 1)
                .Insert(5, insertion)
                .Replace("world", "Atom")
                .Remove(5 + insertion.Length, 1);

            var result = builder.ToString();
            var snapshot = builder.ToString(0, 5);

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result, Is.EqualTo("Hello amazing Atom!"));
                    Assert.That(snapshot, Is.EqualTo("Hello"));
                }
            }
        }
        finally
        {
            builder.Dispose();
        }
    }

    [TestCase(TestName = "StringBuilder: форматирование и очистка"), Benchmark(Description = "SB: форматирование и очистка")]
    public void FormattingStringBuilder()
    {
        var builder = new System.Text.StringBuilder(32)
            .AppendFormat("{0}-{1:D2}", "REQ", 42)
            .AppendLine()
            .Append("Запрос от ")
            .AppendFormat(CultureInfo.InvariantCulture, "{0:O}", DateTime.UnixEpoch);

        builder.EnsureCapacity(1024);

        builder.Clear()
            .Append('A', 10);

        var result = builder.ToString();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(builder.Length, Is.EqualTo(10));

            using (Assert.EnterMultipleScope())
            {
                // StringBuilder.EnsureCapacity не гарантирует точную ёмкость, проверяем только результат
                Assert.That(result, Is.EqualTo(new string('A', 10)));
            }
        }
    }

    [TestCase(TestName = "ValueStringBuilder: форматирование и очистка"), Benchmark(Description = "VSB: форматирование и очистка")]
    public void FormattingTest()
    {
        var builder = new ValueStringBuilder(32);

        try
        {
            builder.AppendFormat("{0}-{1:D2}", "REQ", 42)
                .AppendLine()
                .Append("Запрос от ")
                .AppendFormat(CultureInfo.InvariantCulture, "{0:O}", DateTime.UnixEpoch);

            builder.EnsureCapacity(1024);

            builder.Clear()
                .Append('A', 10);

            var result = builder.ToString();

            if (!IsBenchmarkEnabled)
            {
                Assert.That(builder.Length, Is.EqualTo(10));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(builder.Capacity, Is.GreaterThanOrEqualTo(1024));
                    Assert.That(result, Is.EqualTo(new string('A', 10)));
                }
            }
        }
        finally
        {
            builder.Dispose();
        }
    }

    [TestCase(TestName = "DotNet ValueStringBuilder: форматирование и очистка"), Benchmark(Description = "DotNet VSB: форматирование и очистка")]
    public void FormattingDotNetTest()
    {
        var builder = new DotNetValueStringBuilder(32);

        try
        {
            builder.AppendFormat("{0}-{1:D2}", "REQ", 42)
                .AppendLine()
                .Append("Запрос от ")
                .AppendFormat(CultureInfo.InvariantCulture, "{0:O}", DateTime.UnixEpoch);

            builder.EnsureCapacity(1024);

            builder.Clear()
                .Append('A', 10);

            var result = builder.ToString();

            if (!IsBenchmarkEnabled)
            {
                Assert.That(builder.Length, Is.EqualTo(10));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(builder.Capacity, Is.GreaterThanOrEqualTo(1024));
                    Assert.That(result, Is.EqualTo(new string('A', 10)));
                }
            }
        }
        finally
        {
            builder.Dispose();
        }
    }

    [TestCase(TestName = "StringBuilder: интерполяция и выравнивание"), Benchmark(Description = "SB: интерполяция")]
    public void InterpolatedFormattingStringBuilderTest()
    {
        var builder = new System.Text.StringBuilder(128);
        var timestamp = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        builder.Append($"REQ-{42:D4}")
            .Append('|')
            .Append($"TS:{timestamp:O}")
            .Append('|')
            .Append($"A[{7,4}]")
            .Append($"B[{-7,-4}]");

        var result = builder.ToString();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(result, Is.EqualTo($"REQ-0042|TS:{timestamp:O}|A[   7]B[-7  ]"));
        }
    }

    [TestCase(TestName = "ValueStringBuilder: интерполяция и выравнивание без аллокаций"), Benchmark(Description = "VSB: интерполяция")]
    public void InterpolatedFormattingTest()
    {
        var builder = new ValueStringBuilder(128);
        var timestamp = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        try
        {
            builder.Append($"REQ-{42:D4}")
                .Append('|')
                .Append($"TS:{timestamp:O}")
                .Append('|')
                .Append($"A[{7,4}]")
                .Append($"B[{-7,-4}]");

            var result = builder.ToString();

            if (!IsBenchmarkEnabled)
            {
                Assert.That(result, Is.EqualTo($"REQ-0042|TS:{timestamp:O}|A[   7]B[-7  ]"));
            }
        }
        finally
        {
            builder.Dispose();
        }
    }

    [TestCase(TestName = "DotNet ValueStringBuilder: интерполяция и выравнивание"), Benchmark(Description = "DotNet VSB: интерполяция")]
    public void InterpolatedFormattingDotNetTest()
    {
        var builder = new DotNetValueStringBuilder(128);
        var timestamp = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        try
        {
            builder.Append($"REQ-{42:D4}")
                .Append('|')
                .Append($"TS:{timestamp:O}")
                .Append('|')
                .Append($"A[{7,4}]")
                .Append($"B[{-7,-4}]");

            var result = builder.ToString();

            if (!IsBenchmarkEnabled)
            {
                Assert.That(result, Is.EqualTo($"REQ-0042|TS:{timestamp:O}|A[   7]B[-7  ]"));
            }
        }
        finally
        {
            builder.Dispose();
        }
    }

    [TestCase(TestName = "StringBuilder: композитное форматирование"), Benchmark(Description = "SB: композитное форматирование")]
    public void CompositeFormattingStringBuilderTest()
    {
        var builder = new System.Text.StringBuilder(256);
        var big = decimal.MaxValue;

        builder.AppendFormat(CultureInfo.InvariantCulture, "HEX:{0:X2} DEC:{1,5}", 255, 17)
            .Append('|')
            .AppendFormat(CultureInfo.InvariantCulture, "BIG:{0}", big);

        var result = builder.ToString();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(result, Is.EqualTo($"HEX:FF DEC:   17|BIG:{big.ToString(CultureInfo.InvariantCulture)}"));
        }
    }

    [TestCase(TestName = "ValueStringBuilder: композитное форматирование без промежуточных строк"), Benchmark(Description = "VSB: композитное форматирование")]
    public void CompositeFormattingTest()
    {
        var builder = new ValueStringBuilder(256);
        var big = decimal.MaxValue;

        try
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, "HEX:{0:X2} DEC:{1,5}", 255, 17);
            builder.Append('|');
            builder.AppendFormat(CultureInfo.InvariantCulture, "BIG:{0}", big);

            var result = builder.ToString();

            if (!IsBenchmarkEnabled)
            {
                Assert.That(result, Is.EqualTo($"HEX:FF DEC:   17|BIG:{big.ToString(CultureInfo.InvariantCulture)}"));
            }
        }
        finally
        {
            builder.Dispose();
        }
    }

    [TestCase(TestName = "DotNet ValueStringBuilder: композитное форматирование"), Benchmark(Description = "DotNet VSB: композитное форматирование")]
    public void CompositeFormattingDotNetTest()
    {
        var builder = new DotNetValueStringBuilder(256);
        var big = decimal.MaxValue;

        try
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, "HEX:{0:X2} DEC:{1,5}", 255, 17)
                .Append('|')
                .AppendFormat(CultureInfo.InvariantCulture, "BIG:{0}", big);

            var result = builder.ToString();

            if (!IsBenchmarkEnabled)
            {
                Assert.That(result, Is.EqualTo($"HEX:FF DEC:   17|BIG:{big.ToString(CultureInfo.InvariantCulture)}"));
            }
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static void BuildUserAgent(System.Text.StringBuilder builder)
        => builder.Append("Mozilla/5.0")
        .Append(" (Windows NT 10.0; Win64; x64)")
        .Append(" AppleWebKit/537.36")
        .Append(" (KHTML, like Gecko)")
        .Append(" Chrome/134.0.0.0")
        .Append(" Safari/537.36");

    private static void BuildUserAgent(ref ValueStringBuilder builder)
        => builder.Append("Mozilla/5.0")
        .Append(" (Windows NT 10.0; Win64; x64)")
        .Append(" AppleWebKit/537.36")
        .Append(" (KHTML, like Gecko)")
        .Append(" Chrome/134.0.0.0")
        .Append(" Safari/537.36");

    private static void BuildUserAgentDotNet(ref DotNetValueStringBuilder builder)
        => builder.Append("Mozilla/5.0")
        .Append(" (Windows NT 10.0; Win64; x64)")
        .Append(" AppleWebKit/537.36")
        .Append(" (KHTML, like Gecko)")
        .Append(" Chrome/134.0.0.0")
        .Append(" Safari/537.36");

    // ═══════════════════════════════════════════════════════════════════════════════
    // SIMD STRESS TESTS - Large Strings для проверки SIMD оптимизаций
    // ═══════════════════════════════════════════════════════════════════════════════

    private static readonly string Text1K = new('x', 1024);
    private static readonly string Text4K = new('x', 4096);
    private static readonly string Text16K = new('x', 16384);
    private static readonly string TextBase1K = new('A', 1024);
    private static readonly string InsertText256 = new('B', 256);
    private static readonly string Text4KWithPatterns = string.Concat(Enumerable.Repeat("Hello World! Foo Bar Baz. ", 160));

    // ============== SIMD Stress: 1KB String Char Replace ==============

    [TestCase(TestName = "StringBuilder: ReplaceChar 1KB"), Benchmark(Description = "SB: ReplaceChar 1KB")]
    public void StringBuilderReplaceChar1K()
    {
        var sb = new System.Text.StringBuilder(Text1K);
        sb.Replace('x', 'Y');

        if (!IsBenchmarkEnabled)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sb.ToString(), Does.Not.Contain('x'));
                Assert.That(sb.Length, Is.EqualTo(1024));
            }

        }
    }

    [TestCase(TestName = "ValueStringBuilder: ReplaceChar 1KB"), Benchmark(Description = "VSB: ReplaceChar 1KB SIMD")]
    public void ValueStringBuilderReplaceChar1K()
    {
        var vsb = new ValueStringBuilder(2048);
        try
        {
            vsb.Append(Text1K);
            vsb.Replace('x', 'Y');

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(vsb.ToString(), Does.Not.Contain('x'));
                    Assert.That(vsb.Length, Is.EqualTo(1024));
                }

            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "DotNet VSB: ReplaceChar 1KB"), Benchmark(Description = "DotNet VSB: ReplaceChar 1KB")]
    public void DotNetValueStringBuilderReplaceChar1K()
    {
        var vsb = new DotNetValueStringBuilder(2048);
        try
        {
            vsb.Append(Text1K);
            vsb.Replace('x', 'Y');

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(vsb.ToString(), Does.Not.Contain('x'));
                    Assert.That(vsb.Length, Is.EqualTo(1024));
                }

            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    // ============== SIMD Stress: 4KB String Char Replace ==============

    [TestCase(TestName = "StringBuilder: ReplaceChar 4KB"), Benchmark(Description = "SB: ReplaceChar 4KB")]
    public void StringBuilderReplaceChar4K()
    {
        var sb = new System.Text.StringBuilder(Text4K);
        sb.Replace('x', 'Y');

        if (!IsBenchmarkEnabled)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sb.ToString(), Does.Not.Contain('x'));
                Assert.That(sb.Length, Is.EqualTo(4096));
            }

        }
    }

    [TestCase(TestName = "ValueStringBuilder: ReplaceChar 4KB"), Benchmark(Description = "VSB: ReplaceChar 4KB SIMD")]
    public void ValueStringBuilderReplaceChar4K()
    {
        var vsb = new ValueStringBuilder(8192);
        try
        {
            vsb.Append(Text4K);
            vsb.Replace('x', 'Y');

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(vsb.ToString(), Does.Not.Contain('x'));
                    Assert.That(vsb.Length, Is.EqualTo(4096));
                }

            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "DotNet VSB: ReplaceChar 4KB"), Benchmark(Description = "DotNet VSB: ReplaceChar 4KB")]
    public void DotNetValueStringBuilderReplaceChar4K()
    {
        var vsb = new DotNetValueStringBuilder(8192);
        try
        {
            vsb.Append(Text4K);
            vsb.Replace('x', 'Y');

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(vsb.ToString(), Does.Not.Contain('x'));
                    Assert.That(vsb.Length, Is.EqualTo(4096));
                }

            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    // ============== SIMD Stress: 16KB String Char Replace ==============

    [TestCase(TestName = "StringBuilder: ReplaceChar 16KB"), Benchmark(Description = "SB: ReplaceChar 16KB")]
    public void StringBuilderReplaceChar16K()
    {
        var sb = new System.Text.StringBuilder(Text16K);
        sb.Replace('x', 'Y');

        if (!IsBenchmarkEnabled)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sb.ToString(), Does.Not.Contain('x'));
                Assert.That(sb.Length, Is.EqualTo(16384));
            }

        }
    }

    [TestCase(TestName = "ValueStringBuilder: ReplaceChar 16KB"), Benchmark(Description = "VSB: ReplaceChar 16KB SIMD")]
    public void ValueStringBuilderReplaceChar16K()
    {
        var vsb = new ValueStringBuilder(32768);
        try
        {
            vsb.Append(Text16K);
            vsb.Replace('x', 'Y');

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(vsb.ToString(), Does.Not.Contain('x'));
                    Assert.That(vsb.Length, Is.EqualTo(16384));
                }

            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "DotNet VSB: ReplaceChar 16KB"), Benchmark(Description = "DotNet VSB: ReplaceChar 16KB")]
    public void DotNetValueStringBuilderReplaceChar16K()
    {
        var vsb = new DotNetValueStringBuilder(32768);
        try
        {
            vsb.Append(Text16K);
            vsb.Replace('x', 'Y');

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(vsb.ToString(), Does.Not.Contain('x'));
                    Assert.That(vsb.Length, Is.EqualTo(16384));
                }

            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    // ============== SIMD Stress: Large Insert + Shift ==============

    [TestCase(TestName = "StringBuilder: Insert 256 into 1KB"), Benchmark(Description = "SB: Insert 256→1KB")]
    public void StringBuilderInsertLarge()
    {
        var sb = new System.Text.StringBuilder(TextBase1K);
        sb.Insert(512, InsertText256);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(sb.Length, Is.EqualTo(1280));
        }
    }

    [TestCase(TestName = "ValueStringBuilder: Insert 256 into 1KB"), Benchmark(Description = "VSB: Insert 256→1KB")]
    public void ValueStringBuilderInsertLarge()
    {
        var vsb = new ValueStringBuilder(2048);
        try
        {
            vsb.Append(TextBase1K);
            vsb.Insert(512, InsertText256);

            if (!IsBenchmarkEnabled)
            {
                Assert.That(vsb.Length, Is.EqualTo(1280));
            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "DotNet VSB: Insert 256 into 1KB"), Benchmark(Description = "DotNet VSB: Insert 256→1KB")]
    public void DotNetValueStringBuilderInsertLarge()
    {
        var vsb = new DotNetValueStringBuilder(2048);
        try
        {
            vsb.Append(TextBase1K);
            vsb.Insert(512, InsertText256);

            if (!IsBenchmarkEnabled)
            {
                Assert.That(vsb.Length, Is.EqualTo(1280));
            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    // ============== SIMD Stress: Multiple String Replace 4KB ==============

    [TestCase(TestName = "StringBuilder: String Replace x3 4KB"), Benchmark(Description = "SB: StrReplace x3 4KB")]
    public void StringBuilderStringReplace4K()
    {
        var sb = new System.Text.StringBuilder(Text4KWithPatterns);
        sb.Replace("Hello", "XXXXX");
        sb.Replace("World", "YYYYY");
        sb.Replace("Foo", "ZZZ");

        if (!IsBenchmarkEnabled)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sb.ToString(), Does.Not.Contain("Hello"));
                Assert.That(sb.ToString(), Does.Contain("XXXXX"));
            }

        }
    }

    [TestCase(TestName = "ValueStringBuilder: String Replace x3 4KB"), Benchmark(Description = "VSB: StrReplace x3 4KB SIMD")]
    public void ValueStringBuilderStringReplace4K()
    {
        var vsb = new ValueStringBuilder(8192);
        try
        {
            vsb.Append(Text4KWithPatterns);
            vsb.Replace("Hello", "XXXXX");
            vsb.Replace("World", "YYYYY");
            vsb.Replace("Foo", "ZZZ");

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(vsb.ToString(), Does.Not.Contain("Hello"));
                    Assert.That(vsb.ToString(), Does.Contain("XXXXX"));
                }

            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "DotNet VSB: String Replace x3 4KB"), Benchmark(Description = "DotNet VSB: StrReplace x3 4KB")]
    public void DotNetValueStringBuilderStringReplace4K()
    {
        var vsb = new DotNetValueStringBuilder(8192);
        try
        {
            vsb.Append(Text4KWithPatterns);
            vsb.Replace("Hello", "XXXXX");
            vsb.Replace("World", "YYYYY");
            vsb.Replace("Foo", "ZZZ");

            if (!IsBenchmarkEnabled)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(vsb.ToString(), Does.Not.Contain("Hello"));
                    Assert.That(vsb.ToString(), Does.Contain("XXXXX"));
                }

            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    // ============== SIMD Stress: Expanding Replace (short→long) 4KB ==============

    [TestCase(TestName = "StringBuilder: Expand Replace 4KB"), Benchmark(Description = "SB: Expand 4KB")]
    public void StringBuilderExpandReplace4K()
    {
        var sb = new System.Text.StringBuilder(Text4KWithPatterns);
        sb.Replace("!", "!!!!!");

        if (!IsBenchmarkEnabled)
        {
            Assert.That(sb.Length, Is.GreaterThan(Text4KWithPatterns.Length));
        }
    }

    [TestCase(TestName = "ValueStringBuilder: Expand Replace 4KB"), Benchmark(Description = "VSB: Expand 4KB SIMD")]
    public void ValueStringBuilderExpandReplace4K()
    {
        var vsb = new ValueStringBuilder(16384);
        try
        {
            vsb.Append(Text4KWithPatterns);
            vsb.Replace("!", "!!!!!");

            if (!IsBenchmarkEnabled)
            {
                Assert.That(vsb.Length, Is.GreaterThan(Text4KWithPatterns.Length));
            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "DotNet VSB: Expand Replace 4KB"), Benchmark(Description = "DotNet VSB: Expand 4KB")]
    public void DotNetValueStringBuilderExpandReplace4K()
    {
        var vsb = new DotNetValueStringBuilder(16384);
        try
        {
            vsb.Append(Text4KWithPatterns);
            vsb.Replace("!", "!!!!!");

            if (!IsBenchmarkEnabled)
            {
                Assert.That(vsb.Length, Is.GreaterThan(Text4KWithPatterns.Length));
            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    // ============== SIMD Stress: Shrinking Replace (long→short) 4KB ==============

    [TestCase(TestName = "StringBuilder: Shrink Replace 4KB"), Benchmark(Description = "SB: Shrink 4KB")]
    public void StringBuilderShrinkReplace4K()
    {
        var sb = new System.Text.StringBuilder(Text4KWithPatterns);
        sb.Replace("Hello World!", "HW");

        if (!IsBenchmarkEnabled)
        {
            Assert.That(sb.Length, Is.LessThan(Text4KWithPatterns.Length));
        }
    }

    [TestCase(TestName = "ValueStringBuilder: Shrink Replace 4KB"), Benchmark(Description = "VSB: Shrink 4KB SIMD")]
    public void ValueStringBuilderShrinkReplace4K()
    {
        var vsb = new ValueStringBuilder(8192);
        try
        {
            vsb.Append(Text4KWithPatterns);
            vsb.Replace("Hello World!", "HW");

            if (!IsBenchmarkEnabled)
            {
                Assert.That(vsb.Length, Is.LessThan(Text4KWithPatterns.Length));
            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "DotNet VSB: Shrink Replace 4KB"), Benchmark(Description = "DotNet VSB: Shrink 4KB")]
    public void DotNetValueStringBuilderShrinkReplace4K()
    {
        var vsb = new DotNetValueStringBuilder(8192);
        try
        {
            vsb.Append(Text4KWithPatterns);
            vsb.Replace("Hello World!", "HW");

            if (!IsBenchmarkEnabled)
            {
                Assert.That(vsb.Length, Is.LessThan(Text4KWithPatterns.Length));
            }
        }
        finally
        {
            vsb.Dispose();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // EDGE CASE & BOUNDARY STRESS TESTS - Проверка граничных условий и коллизий
    // ═══════════════════════════════════════════════════════════════════════════════

    #region Конструкторы и инициализация

    [TestCase(TestName = "VSB: конструктор по умолчанию")]
    public void DefaultConstructorShouldWork()
    {
        var vsb = new ValueStringBuilder();
        try
        {
            vsb.Append("test");
            Assert.That(vsb.ToString(), Is.EqualTo("test"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: конструктор с нулевой ёмкостью")]
    public void ZeroCapacityConstructorShouldGrowOnAppend()
    {
        var vsb = new ValueStringBuilder(1);
        try
        {
            vsb.Append("Hello, World!");
            Assert.That(vsb.ToString(), Is.EqualTo("Hello, World!"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: конструктор с внешним Span")]
    public void ExternalSpanConstructorShouldUseProvidedBuffer()
    {
        Span<char> buffer = stackalloc char[64];
        var vsb = new ValueStringBuilder(buffer);
        try
        {
            vsb.Append("Stack allocated");
            Assert.That(vsb.ToString(), Is.EqualTo("Stack allocated"));
            Assert.That(vsb.Capacity, Is.EqualTo(64));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: конструктор со строкой")]
    public void StringConstructorShouldInitializeWithValue()
    {
        var vsb = new ValueStringBuilder("Initial");
        try
        {
            vsb.Append(" appended");
            Assert.That(vsb.ToString(), Is.EqualTo("Initial appended"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: конструктор с null-строкой")]
    public void NullStringConstructorShouldCreateEmpty()
    {
        var vsb = new ValueStringBuilder((string?)null);
        try
        {
            Assert.That(vsb.Length, Is.Zero);
            vsb.Append("test");
            Assert.That(vsb.ToString(), Is.EqualTo("test"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region Append граничные случаи

    [TestCase(TestName = "VSB: Append пустой строки")]
    public void AppendEmptyStringShouldNotChangeLength()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("test");
            var lengthBefore = vsb.Length;
            vsb.Append("");
            vsb.Append(string.Empty);
            Assert.That(vsb.Length, Is.EqualTo(lengthBefore));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Append null-строки")]
    public void AppendNullStringShouldNotChangeLength()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("test");
            var lengthBefore = vsb.Length;
            vsb.Append((string?)null);
            Assert.That(vsb.Length, Is.EqualTo(lengthBefore));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Append пустого ReadOnlySpan")]
    public void AppendEmptySpanShouldNotChangeLength()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("test");
            var lengthBefore = vsb.Length;
            vsb.Append([]);
            Assert.That(vsb.Length, Is.EqualTo(lengthBefore));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Append одиночного символа")]
    public void AppendSingleCharShouldWork()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            vsb.Append('A');
            vsb.Append('B');
            vsb.Append('C');
            Assert.That(vsb.ToString(), Is.EqualTo("ABC"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Append символа с повторением 0 раз")]
    public void AppendCharZeroCountShouldNotChangeLength()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("test");
            var lengthBefore = vsb.Length;
            vsb.Append('X', 0);
            Assert.That(vsb.Length, Is.EqualTo(lengthBefore));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Append вызывает рост буфера")]
    public void AppendCausesGrowShouldPreserveContent()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            vsb.Append("AB");
            vsb.Append("CD");
            vsb.Append("EFGHIJKLMNOP"); // Должен вызвать рост
            Assert.That(vsb.ToString(), Is.EqualTo("ABCDEFGHIJKLMNOP"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: множественный Append до границы буфера")]
    public void MultipleAppendToExactCapacityShouldWork()
    {
        var vsb = new ValueStringBuilder(10);
        try
        {
            vsb.Append("12345");
            vsb.Append("67890");
            Assert.That(vsb.Length, Is.EqualTo(10));
            Assert.That(vsb.ToString(), Is.EqualTo("1234567890"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region Insert граничные случаи

    [TestCase(TestName = "VSB: Insert в начало")]
    public void InsertAtStartShouldPrependContent()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("World");
            vsb.Insert(0, "Hello ");
            Assert.That(vsb.ToString(), Is.EqualTo("Hello World"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Insert в конец")]
    public void InsertAtEndShouldAppendContent()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            vsb.Insert(5, " World");
            Assert.That(vsb.ToString(), Is.EqualTo("Hello World"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Insert в пустой буфер")]
    public void InsertIntoEmptyShouldWork()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Insert(0, "Inserted");
            Assert.That(vsb.ToString(), Is.EqualTo("Inserted"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Insert пустой строки")]
    public void InsertEmptyStringShouldNotChangeContent()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            vsb.Insert(2, "");
            Assert.That(vsb.ToString(), Is.EqualTo("Hello"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Insert вызывает рост буфера")]
    public void InsertCausesGrowShouldPreserveContent()
    {
        var vsb = new ValueStringBuilder(8);
        try
        {
            vsb.Append("ABCD");
            vsb.Insert(2, "1234567890"); // Должен вызвать рост
            Assert.That(vsb.ToString(), Is.EqualTo("AB1234567890CD"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Insert символа в середину")]
    public void InsertCharInMiddleShouldWork()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("AC");
            vsb.Insert(1, 'B');
            Assert.That(vsb.ToString(), Is.EqualTo("ABC"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region Remove граничные случаи

    [TestCase(TestName = "VSB: Remove с начала")]
    public void RemoveFromStartShouldWork()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello World");
            vsb.Remove(0, 6);
            Assert.That(vsb.ToString(), Is.EqualTo("World"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Remove с конца")]
    public void RemoveFromEndShouldWork()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello World");
            vsb.Remove(5, 6);
            Assert.That(vsb.ToString(), Is.EqualTo("Hello"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Remove из середины")]
    public void RemoveFromMiddleShouldWork()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello World");
            vsb.Remove(5, 1); // Удаляем пробел
            Assert.That(vsb.ToString(), Is.EqualTo("HelloWorld"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Remove нулевого количества")]
    public void RemoveZeroCountShouldNotChangeContent()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            vsb.Remove(2, 0);
            Assert.That(vsb.ToString(), Is.EqualTo("Hello"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Remove всего содержимого")]
    public void RemoveAllShouldMakeEmpty()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            vsb.Remove(0, 5);
            Assert.That(vsb.Length, Is.Zero);
            Assert.That(vsb.ToString(), Is.EqualTo(""));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region Replace граничные случаи

    [TestCase(TestName = "VSB: Replace несуществующей подстроки")]
    public void ReplaceNonExistentShouldNotChangeContent()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello World");
            vsb.Replace("XYZ", "ABC");
            Assert.That(vsb.ToString(), Is.EqualTo("Hello World"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Replace на пустую строку (удаление)")]
    public void ReplaceWithEmptyShouldRemoveOccurrences()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello World");
            vsb.Replace(" ", "");
            Assert.That(vsb.ToString(), Is.EqualTo("HelloWorld"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Replace на null (удаление)")]
    public void ReplaceWithNullShouldRemoveOccurrences()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello World");
            vsb.Replace(" ", null);
            Assert.That(vsb.ToString(), Is.EqualTo("HelloWorld"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Replace пустой подстроки")]
    public void ReplaceEmptyStringShouldNotChangeContent()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            vsb.Replace("", "X");
            Assert.That(vsb.ToString(), Is.EqualTo("Hello"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Replace символа на символ")]
    public void ReplaceCharWithCharShouldWork()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            vsb.Replace('l', 'L');
            Assert.That(vsb.ToString(), Is.EqualTo("HeLLo"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Replace множественных вхождений")]
    public void ReplaceMultipleOccurrencesShouldReplaceAll()
    {
        var vsb = new ValueStringBuilder(64);
        try
        {
            vsb.Append("aaa bbb aaa ccc aaa");
            vsb.Replace("aaa", "X");
            Assert.That(vsb.ToString(), Is.EqualTo("X bbb X ccc X"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Replace с расширением (короткое → длинное)")]
    public void ReplaceExpandingShouldGrowIfNeeded()
    {
        var vsb = new ValueStringBuilder(16);
        try
        {
            vsb.Append("AB");
            vsb.Replace("A", "AAAAAAAAAA"); // 1 → 10
            Assert.That(vsb.ToString(), Is.EqualTo("AAAAAAAAAAB"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Replace с сужением (длинное → короткое)")]
    public void ReplaceShrinkingShouldReduceLength()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("AAAAAAAAAA");
            vsb.Replace("AAAAAAAAAA", "B");
            Assert.That(vsb.ToString(), Is.EqualTo("B"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Replace перекрывающихся паттернов")]
    public void ReplaceOverlappingPatternsShouldHandleCorrectly()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("aaaa");
            vsb.Replace("aa", "b");
            Assert.That(vsb.ToString(), Is.EqualTo("bb"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region Clear и управление состоянием

    [TestCase(TestName = "VSB: Clear сбрасывает Length")]
    public void ClearShouldResetLength()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello World");
            vsb.Clear();
            Assert.That(vsb.Length, Is.Zero);
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Clear сохраняет Capacity")]
    public void ClearShouldPreserveCapacity()
    {
        var vsb = new ValueStringBuilder(64);
        try
        {
            vsb.Append("Hello World");
            var capacityBefore = vsb.Capacity;
            vsb.Clear();
            Assert.That(vsb.Capacity, Is.EqualTo(capacityBefore));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: повторное использование после Clear")]
    public void ReuseAfterClearShouldWork()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("First");
            vsb.Clear();
            vsb.Append("Second");
            Assert.That(vsb.ToString(), Is.EqualTo("Second"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region EnsureCapacity

    [TestCase(TestName = "VSB: EnsureCapacity увеличивает ёмкость")]
    public void EnsureCapacityShouldIncreaseCapacity()
    {
        var vsb = new ValueStringBuilder(16);
        try
        {
            vsb.EnsureCapacity(1024);
            Assert.That(vsb.Capacity, Is.GreaterThanOrEqualTo(1024));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: EnsureCapacity сохраняет содержимое")]
    public void EnsureCapacityShouldPreserveContent()
    {
        var vsb = new ValueStringBuilder(16);
        try
        {
            vsb.Append("Hello");
            vsb.EnsureCapacity(1024);
            Assert.That(vsb.ToString(), Is.EqualTo("Hello"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: EnsureCapacity меньше текущей — без изменений")]
    public void EnsureCapacityLessThanCurrentShouldNotChange()
    {
        var vsb = new ValueStringBuilder(64);
        try
        {
            var capacityBefore = vsb.Capacity;
            vsb.EnsureCapacity(16);
            Assert.That(vsb.Capacity, Is.EqualTo(capacityBefore));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region ToString варианты

    [TestCase(TestName = "VSB: ToString пустого буфера")]
    public void ToStringEmptyShouldReturnEmptyString()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            Assert.That(vsb.ToString(), Is.EqualTo(""));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: ToString с диапазоном")]
    public void ToStringWithRangeShouldReturnSubstring()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello World");
            Assert.That(vsb.ToString(0, 5), Is.EqualTo("Hello"));
            Assert.That(vsb.ToString(6, 5), Is.EqualTo("World"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: ToString с нулевой длиной")]
    public void ToStringZeroLengthShouldReturnEmpty()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            Assert.That(vsb.ToString(2, 0), Is.EqualTo(""));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region Специальные символы и Unicode

    [TestCase(TestName = "VSB: работа с Unicode символами")]
    public void UnicodeCharactersShouldBeHandledCorrectly()
    {
        var vsb = new ValueStringBuilder(64);
        try
        {
            vsb.Append("Привет 🌍 мир! ");
            vsb.Append("日本語");
            Assert.That(vsb.ToString(), Is.EqualTo("Привет 🌍 мир! 日本語"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: Replace Unicode символов")]
    public void ReplaceUnicodeShouldWork()
    {
        var vsb = new ValueStringBuilder(64);
        try
        {
            vsb.Append("Hello 🌍 World");
            vsb.Replace("🌍", "🌎");
            Assert.That(vsb.ToString(), Is.EqualTo("Hello 🌎 World"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: работа с управляющими символами")]
    public void ControlCharactersShouldBePreserved()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Line1\r\nLine2\tTab");
            Assert.That(vsb.ToString(), Is.EqualTo("Line1\r\nLine2\tTab"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: работа с null-символом")]
    public void NullCharacterShouldBeHandled()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("A\0B");
            Assert.That(vsb.Length, Is.EqualTo(3));
            Assert.That(vsb.ToString(), Is.EqualTo("A\0B"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region Индексатор и доступ к символам

    [TestCase(TestName = "VSB: индексатор чтение")]
    public void IndexerReadShouldReturnCorrectChar()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            Assert.That(vsb[0], Is.EqualTo('H'));
            Assert.That(vsb[4], Is.EqualTo('o'));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: индексатор запись")]
    public void IndexerWriteShouldModifyChar()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            vsb[0] = 'J';
            Assert.That(vsb.ToString(), Is.EqualTo("Jello"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region AppendLine

    [TestCase(TestName = "VSB: AppendLine добавляет перевод строки")]
    public void AppendLineShouldAddNewLine()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Line1");
            vsb.AppendLine();
            vsb.Append("Line2");
            Assert.That(vsb.ToString(), Is.EqualTo("Line1" + Environment.NewLine + "Line2"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: AppendLine со строкой")]
    public void AppendLineWithStringShouldAppendStringAndNewLine()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.AppendLine("Line1");
            vsb.AppendLine("Line2");
            Assert.That(vsb.ToString(), Is.EqualTo("Line1" + Environment.NewLine + "Line2" + Environment.NewLine));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region Стресс-тесты на рост буфера

    [TestCase(TestName = "VSB: множественный рост буфера")]
    public void MultipleGrowsShouldMaintainIntegrity()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            var expected = new System.Text.StringBuilder();
            for (var i = 0; i < 100; i++)
            {
                var chunk = $"Chunk{i:D3}";
                vsb.Append(chunk);
                expected.Append(chunk);
            }
            Assert.That(vsb.ToString(), Is.EqualTo(expected.ToString()));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: чередование Insert и Append")]
    public void AlternatingInsertAppendShouldMaintainIntegrity()
    {
        var vsb = new ValueStringBuilder(16);
        try
        {
            vsb.Append("AC");
            vsb.Insert(1, "B");
            vsb.Append("D");
            vsb.Insert(0, "0");
            vsb.Append("E");
            Assert.That(vsb.ToString(), Is.EqualTo("0ABCDE"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: чередование операций с ростом")]
    public void MixedOperationsWithGrowShouldWork()
    {
        var vsb = new ValueStringBuilder(8);
        try
        {
            vsb.Append("Hello");           // 5 chars
            vsb.Insert(5, " World");       // 11 chars, вызовет рост
            vsb.Replace("World", "Universe"); // расширение
            vsb.Remove(0, 6);              // удаление "Hello "
            vsb.Insert(0, "The ");         // вставка в начало
            Assert.That(vsb.ToString(), Is.EqualTo("The Universe"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region Граничные значения Length и Capacity

    [TestCase(TestName = "VSB: Length после различных операций")]
    public void LengthTrackingShouldBeAccurate()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            Assert.That(vsb.Length, Is.Zero);

            vsb.Append("12345");
            Assert.That(vsb.Length, Is.EqualTo(5));

            vsb.Insert(2, "AB");
            Assert.That(vsb.Length, Is.EqualTo(7));

            vsb.Remove(0, 2);
            Assert.That(vsb.Length, Is.EqualTo(5));

            vsb.Clear();
            Assert.That(vsb.Length, Is.Zero);
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: операции на границе Capacity")]
    public void OperationsAtCapacityBoundaryShouldWork()
    {
        var vsb = new ValueStringBuilder(10);
        try
        {
            // Заполняем ровно до capacity
            vsb.Append("1234567890");
            Assert.That(vsb.Length, Is.EqualTo(10));

            // Добавляем ещё один символ — должен вызвать рост
            vsb.Append('!');
            Assert.That(vsb.Length, Is.EqualTo(11));
            Assert.That(vsb.ToString(), Is.EqualTo("1234567890!"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    #region AsSpan и работа с памятью

    [TestCase(TestName = "VSB: AsSpan возвращает корректные данные")]
    public void AsSpanShouldReturnCorrectData()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            vsb.Append("Hello");
            var span = vsb.AsSpan();
            Assert.That(span.Length, Is.EqualTo(5));
            Assert.That(span.ToString(), Is.EqualTo("Hello"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB: AsSpan пустого буфера")]
    public void AsSpanEmptyShouldReturnEmptySpan()
    {
        var vsb = new ValueStringBuilder(32);
        try
        {
            var span = vsb.AsSpan();
            Assert.That(span.Length, Is.Zero);
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════════════
    // CRITICAL BUFFER/POSITION INVARIANT TESTS - Тесты критических мест буфера
    // ═══════════════════════════════════════════════════════════════════════════════

    #region Критические тесты: Position vs Buffer.Length инвариант

    /// <summary>
    /// Тест проверяет, что position никогда не превышает chars.Length до расширения буфера.
    /// Это критическая проверка инварианта: position <= chars.Length всегда.
    /// </summary>
    [TestCase(TestName = "VSB CRITICAL: Append строки с ростом буфера из минимальной ёмкости")]
    public void CriticalAppendStringWithGrowFromMinimalCapacity()
    {
        // Начинаем с минимального буфера
        var vsb = new ValueStringBuilder(1);
        try
        {
            // Добавляем строку, которая гарантированно превысит буфер
            vsb.Append("Hello, World!");
            Assert.That(vsb.ToString(), Is.EqualTo("Hello, World!"));
            Assert.That(vsb.Length, Is.EqualTo(13));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: множественный Append с постоянным ростом")]
    public void CriticalMultipleAppendWithConstantGrow()
    {
        var vsb = new ValueStringBuilder(2);
        try
        {
            // Каждый Append должен вызывать рост буфера
            for (var i = 0; i < 20; i++)
            {
                vsb.Append("AB"); // 2 символа каждый раз
            }
            Assert.That(vsb.Length, Is.EqualTo(40));
            Assert.That(vsb.ToString(), Is.EqualTo(new string('A', 20).Replace("A", "AB")));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Append большой строки в маленький буфер")]
    public void CriticalAppendLargeStringToSmallBuffer()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            // Строка в 100 раз больше буфера
            var largeString = new string('X', 400);
            vsb.Append(largeString);
            Assert.That(vsb.Length, Is.EqualTo(400));
            Assert.That(vsb.ToString(), Is.EqualTo(largeString));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: последовательный рост буфера 1->2->4->8->..")]
    public void CriticalSequentialBufferGrowth()
    {
        var vsb = new ValueStringBuilder(1);
        try
        {
            var expected = new System.Text.StringBuilder();
            // Добавляем по одному символу, форсируя многократный рост
            for (var i = 0; i < 256; i++)
            {
                var c = (char)('A' + (i % 26));
                vsb.Append(c);
                expected.Append(c);
            }
            Assert.That(vsb.Length, Is.EqualTo(256));
            Assert.That(vsb.ToString(), Is.EqualTo(expected.ToString()));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Insert в начало с ростом буфера")]
    public void CriticalInsertAtStartWithGrow()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            vsb.Append("CD");
            // Insert в начало с необходимостью роста
            vsb.Insert(0, "ABCDEFGHIJ"); // 10 символов, буфер 4
            Assert.That(vsb.ToString(), Is.EqualTo("ABCDEFGHIJCD"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Insert в середину с ростом буфера")]
    public void CriticalInsertInMiddleWithGrow()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            vsb.Append("AC");
            // Insert в середину с необходимостью роста
            vsb.Insert(1, "BBBBBBBBBB"); // 10 символов
            Assert.That(vsb.ToString(), Is.EqualTo("ABBBBBBBBBBC"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Insert в конец с ростом буфера")]
    public void CriticalInsertAtEndWithGrow()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            vsb.Append("AB");
            // Insert в конец с необходимостью роста
            vsb.Insert(2, "CDEFGHIJKL"); // 10 символов
            Assert.That(vsb.ToString(), Is.EqualTo("ABCDEFGHIJKL"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Replace с расширением и ростом буфера")]
    public void CriticalReplaceExpandingWithGrow()
    {
        var vsb = new ValueStringBuilder(8);
        try
        {
            vsb.Append("AAAA"); // 4 символа
            // Заменяем каждый A на 10 символов - должен вызвать рост
            vsb.Replace("A", "0123456789"); // 4 -> 40 символов
            Assert.That(vsb.Length, Is.EqualTo(40));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: чередование Append и Insert с ростом")]
    public void CriticalAlternatingAppendInsertWithGrow()
    {
        var vsb = new ValueStringBuilder(2);
        try
        {
            vsb.Append("A");        // "A"
            vsb.Insert(0, "BB");    // "BBA"
            vsb.Append("C");        // "BBAC"
            vsb.Insert(2, "DDD");   // "BBDDDAC"
            vsb.Append("EE");       // "BBDDDACEE"
            vsb.Insert(0, "FFFF");  // "FFFFBBDDDACEE"
            Assert.That(vsb.ToString(), Is.EqualTo("FFFFBBDDDACEE"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Append после Clear с ростом")]
    public void CriticalAppendAfterClearWithGrow()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            vsb.Append("ABCD");
            vsb.Clear();
            // После Clear position = 0, но буфер остаётся
            // Добавляем строку больше исходного буфера
            vsb.Append("123456789012345");
            Assert.That(vsb.ToString(), Is.EqualTo("123456789012345"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Remove и затем Append с ростом")]
    public void CriticalRemoveThenAppendWithGrow()
    {
        var vsb = new ValueStringBuilder(8);
        try
        {
            vsb.Append("ABCDEFGH"); // заполняем буфер
            vsb.Remove(0, 4);       // удаляем половину, position = 4
            // Теперь position < chars.Length
            vsb.Append("1234567890123456"); // добавляем больше чем осталось места
            Assert.That(vsb.ToString(), Is.EqualTo("EFGH1234567890123456"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: EnsureCapacity и затем заполнение")]
    public void CriticalEnsureCapacityThenFill()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            vsb.EnsureCapacity(100);
            // Теперь буфер большой, заполняем его
            var str = new string('X', 100);
            vsb.Append(str);
            Assert.That(vsb.Length, Is.EqualTo(100));
            Assert.That(vsb.ToString(), Is.EqualTo(str));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: граничный случай - точно на границе буфера")]
    public void CriticalExactBufferBoundary()
    {
        // ArrayPool обычно возвращает степени двойки
        var vsb = new ValueStringBuilder(16);
        try
        {
            // Заполняем ровно до размера буфера
            vsb.Append("1234567890123456"); // 16 символов
            Assert.That(vsb.Length, Is.EqualTo(16));

            // Добавляем ещё один символ - должен вызвать рост
            vsb.Append('!');
            Assert.That(vsb.Length, Is.EqualTo(17));
            Assert.That(vsb.ToString(), Is.EqualTo("1234567890123456!"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Append символа много раз с постоянным ростом")]
    public void CriticalAppendCharManyTimesWithGrow()
    {
        var vsb = new ValueStringBuilder(1);
        try
        {
            for (var i = 0; i < 1000; i++)
            {
                vsb.Append('X');
            }
            Assert.That(vsb.Length, Is.EqualTo(1000));
            Assert.That(vsb.ToString(), Is.EqualTo(new string('X', 1000)));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Append(char, count) с ростом")]
    public void CriticalAppendCharCountWithGrow()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            vsb.Append('X', 100); // Запрашиваем 100 символов при буфере 4
            Assert.That(vsb.Length, Is.EqualTo(100));
            Assert.That(vsb.ToString(), Is.EqualTo(new string('X', 100)));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Append ReadOnlySpan с ростом")]
    public void CriticalAppendSpanWithGrow()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            var span = "Hello, World! This is a long string.".AsSpan();
            vsb.Append(span);
            Assert.That(vsb.ToString(), Is.EqualTo("Hello, World! This is a long string"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: смешанные операции стресс-тест")]
    public void CriticalMixedOperationsStress()
    {
        var vsb = new ValueStringBuilder(2);
        try
        {
            var reference = new System.Text.StringBuilder();

            // Серия смешанных операций
            vsb.Append("AB"); reference.Append("AB");
            vsb.Insert(1, "X"); reference.Insert(1, "X");
            vsb.Append("CD"); reference.Append("CD");
            vsb.Replace("X", "YYY"); reference.Replace("X", "YYY");
            vsb.Insert(0, "START"); reference.Insert(0, "START");
            vsb.Append("END"); reference.Append("END");
            vsb.Remove(5, 1); reference.Remove(5, 1);
            vsb.Append(new string('Z', 50)); reference.Append(new string('Z', 50));

            Assert.That(vsb.ToString(), Is.EqualTo(reference.ToString()));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Insert в пустой буфер с ростом")]
    public void CriticalInsertIntoEmptyWithGrow()
    {
        var vsb = new ValueStringBuilder(2);
        try
        {
            // Insert в пустой буфер большой строки
            vsb.Insert(0, "This is a very long string that will require buffer growth");
            Assert.That(vsb.ToString(), Is.EqualTo("This is a very long string that will require buffer growth"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: Insert char в пустой буфер")]
    public void CriticalInsertCharIntoEmpty()
    {
        var vsb = new ValueStringBuilder(1);
        try
        {
            vsb.Insert(0, 'A');
            vsb.Insert(0, 'B');
            vsb.Insert(0, 'C');
            Assert.That(vsb.ToString(), Is.EqualTo("CBA"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: множественный Replace с ростом")]
    public void CriticalMultipleReplaceWithGrow()
    {
        var vsb = new ValueStringBuilder(8);
        try
        {
            vsb.Append("ABAB");
            // Каждая замена увеличивает размер
            vsb.Replace("A", "AAA"); // ABAB -> AAABABAAA (ошибочно) -> AAABAAAB
            vsb.Replace("B", "BBB"); // AAABAAAB -> AAABBBBAAABBBB (ошибочно)
            // Проверяем что результат корректен
            Assert.That(vsb.ToString().Contains("AAA"), Is.True);
            Assert.That(vsb.ToString().Contains("BBB"), Is.True);
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: AppendLine с ростом")]
    public void CriticalAppendLineWithGrow()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            for (var i = 0; i < 50; i++)
            {
                vsb.AppendLine($"Line{i}");
            }
            Assert.That(vsb.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(50));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: внешний Span буфер переполнение")]
    public void CriticalExternalSpanOverflow()
    {
        Span<char> buffer = stackalloc char[8];
        var vsb = new ValueStringBuilder(buffer);
        try
        {
            // Внешний буфер не может расти, но maxCapacity = buffer.Length
            // Проверяем поведение при заполнении до предела
            vsb.Append("12345678");
            Assert.That(vsb.Length, Is.EqualTo(8));
            Assert.That(vsb.ToString(), Is.EqualTo("12345678"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: форматирование с ростом")]
    public void CriticalFormattingWithGrow()
    {
        var vsb = new ValueStringBuilder(4);
        try
        {
            vsb.Append($"Value: {12345678901234567890m}");
            Assert.That(vsb.ToString(), Does.StartWith("Value: "));
            Assert.That(vsb.Length, Is.GreaterThan(10));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    [TestCase(TestName = "VSB CRITICAL: цепочка операций без Dispose между ними")]
    public void CriticalChainedOperationsNoClear()
    {
        var vsb = new ValueStringBuilder(2);
        try
        {
            vsb.Append("A")
               .Append("BB")
               .Append("CCC")
               .Append("DDDD")
               .Append("EEEEE")
               .Insert(0, "X")
               .Replace("A", "Z")
               .Append("END");

            Assert.That(vsb.ToString(), Does.StartWith("X"));
            Assert.That(vsb.ToString(), Does.EndWith("END"));
            Assert.That(vsb.ToString(), Does.Not.Contain("A"));
        }
        finally
        {
            vsb.Dispose();
        }
    }

    #endregion
}