using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Text.Tests;

public class ContainsTests(ILogger logger) : BenchmarkTest<ContainsTests>(logger)
{
    private const string SearchPattern = "Поиск";
    private static string? shortText;
    private static string? longText;

    public override bool IsBenchmarkDisabled => true;

    public ContainsTests() : this(ConsoleLogger.Unicode) { }

    public override void OneTimeSetUp()
    {
        SetUp();
        base.OneTimeSetUp();
    }

    public override void GlobalSetUp()
    {
        SetUp();
        base.GlobalSetUp();
    }

    private void ShortTextTest(SubstringSearchAlgorithm algorithm)
    {
        if (IsTest)
            Assert.That(shortText, Is.Not.Null);
        else
            ArgumentException.ThrowIfNullOrEmpty(shortText);

        var isFound = shortText!.Contains(SearchPattern, algorithm);
        if (IsTest) Assert.That(isFound, Is.True);

        isFound = shortText!.Contains(SearchPattern, StringComparison.InvariantCultureIgnoreCase, algorithm);
        if (IsTest) Assert.That(isFound, Is.True);
    }

    private void ShortTextTest() => ShortTextTest(default);

    private void ShortTextDotnetTest()
    {
        if (IsTest)
            Assert.That(shortText, Is.Not.Null);
        else
            ArgumentException.ThrowIfNullOrEmpty(shortText);

        var isFound = shortText!.Contains(SearchPattern);
        if (IsTest) Assert.That(isFound, Is.True);

        isFound = shortText!.Contains(SearchPattern, StringComparison.InvariantCultureIgnoreCase);
        if (IsTest) Assert.That(isFound, Is.True);
    }

    private void LongTextTest(SubstringSearchAlgorithm algorithm)
    {
        if (IsTest)
            Assert.That(longText, Is.Not.Null);
        else
            ArgumentException.ThrowIfNullOrEmpty(longText);

        var isFound = longText!.Contains(SearchPattern, algorithm);
        if (IsTest) Assert.That(isFound, Is.True);

        isFound = longText!.Contains(SearchPattern, StringComparison.InvariantCultureIgnoreCase, algorithm);
        if (IsTest) Assert.That(isFound, Is.True);
    }

    private void LongTextTest() => LongTextTest(default);

    private void LongTextDotnetTest()
    {
        if (IsTest)
            Assert.That(longText, Is.Not.Null);
        else
            ArgumentException.ThrowIfNullOrEmpty(longText);

        var isFound = shortText!.Contains(SearchPattern);
        if (IsTest) Assert.That(isFound, Is.True);

        isFound = shortText!.Contains(SearchPattern, StringComparison.InvariantCultureIgnoreCase);
        if (IsTest) Assert.That(isFound, Is.True);
    }


    [TestCase(TestName = "Тест наличия вхождений подстроки (.NET, 300 символов)"), Benchmark(Description = ".NET 300", Baseline = true)]
    public void CountOfDotnetTest() => ShortTextDotnetTest();

    [TestCase(TestName = "Тест наличия вхождений подстроки (Рабин-Карп, 300 символов)"), Benchmark(Description = "Рабин-Карп 300")]
    public void CountOfRabinKarpTest() => ShortTextTest();

    [TestCase(TestName = "Тест наличия вхождений подстроки (Бойер-Мур, 300 символов)"), Benchmark(Description = "Бойер-Мур 300")]
    public void CountOfBoyerMooreTest() => ShortTextTest(SubstringSearchAlgorithm.BoyerMoore);

    [TestCase(TestName = "Тест наличия вхождений подстроки (KMP, 300 символов)"), Benchmark(Description = "KMP 300")]
    public void CountOfKmpTest() => ShortTextTest(SubstringSearchAlgorithm.KMP);

    [TestCase(TestName = "Тест наличия вхождений подстроки (Ахо-Корасик, 300 символов)"), Benchmark(Description = "Ахо-Корасик 300")]
    public void CountOfAhoCorasickTest() => ShortTextTest(SubstringSearchAlgorithm.AhoCorasick);

    [TestCase(TestName = "Тест наличия вхождений подстроки (Z-алгоритм, 300 символов)"), Benchmark(Description = "Z-алгоритм 300")]
    public void CountOfZTest() => ShortTextTest(SubstringSearchAlgorithm.Z);

    [TestCase(TestName = "Тест наличия вхождений подстроки (.NET, 10 000 символов)"), Benchmark(Description = ".NET 10 000")]
    public void CountOfDotnetLongTest() => LongTextDotnetTest();

    [TestCase(TestName = "Тест наличия вхождений подстроки (Рабин-Карп, 10 000 символов)"), Benchmark(Description = "Рабин-Карп 10 000")]
    public void CountOfRabinKarpLongTest() => LongTextTest();

    [TestCase(TestName = "Тест наличия вхождений подстроки (Бойер-Мур, 10 000 символов)"), Benchmark(Description = "Бойер-Мур 10 000")]
    public void CountOfBoyerMooreLongTest() => LongTextTest(SubstringSearchAlgorithm.BoyerMoore);

    [TestCase(TestName = "Тест наличия вхождений подстроки (KMP, 10 000 символов)"), Benchmark(Description = "KMP 10 000")]
    public void CountOfKmpLongTest() => LongTextTest(SubstringSearchAlgorithm.KMP);

    [TestCase(TestName = "Тест наличия вхождений подстроки (Ахо-Корасик, 10 000 символов)"), Benchmark(Description = "Ахо-Корасик 10 000")]
    public void CountOfAhoCorasickLongTest() => LongTextTest(SubstringSearchAlgorithm.AhoCorasick);

    [TestCase(TestName = "Тест наличия вхождений подстроки (Z-алгоритм, 10 000 символов)"), Benchmark(Description = "Z-алгоритм 10 000")]
    public void CountOfZLongTest() => LongTextTest(SubstringSearchAlgorithm.Z);
    
    private static void SetUp()
    {
        if (string.IsNullOrEmpty(shortText) && File.Exists("assets/text/short.txt"))
            shortText = File.ReadAllText("assets/text/short.txt");

        if (string.IsNullOrEmpty(longText) && File.Exists("assets/text/long.txt"))
            longText = File.ReadAllText("assets/text/long.txt");
    }
}