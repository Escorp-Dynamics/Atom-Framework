namespace Atom.Algorithms.Text.Tests;

/// <summary>
/// Тесты для всех алгоритмов поиска подстроки.
/// </summary>
[TestFixture, Parallelizable(ParallelScope.All)]
public class TextAlgorithmsTests(ILogger logger) : BenchmarkTests<TextAlgorithmsTests>(logger)
{
    public TextAlgorithmsTests() : this(ConsoleLogger.Unicode) { }

    #region Test Data

    private static readonly string LongText = new string('a', 10000) + "needle" + new string('b', 10000);
    private static readonly string RepeatingText = string.Concat(Enumerable.Repeat("ab", 5000));
    private static readonly string UnicodeText = "Привет мир! こんにちは 🎉 Hello world! Привет мир!";

    #endregion

    #region KMP Algorithm Tests

    [TestCase(TestName = "KMP: поиск в пустой строке")]
    public void KmpEmptySource()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("", "test"), Is.Zero);
            Assert.That(algorithm.Contains("", "test"), Is.False);
        });
    }

    [TestCase(TestName = "KMP: пустой паттерн")]
    public void KmpEmptyPattern()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("test", ""), Is.Zero);
            Assert.That(algorithm.Contains("test", ""), Is.False);
        });
    }

    [TestCase(TestName = "KMP: паттерн длиннее источника")]
    public void KmpPatternLongerThanSource()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("ab", "abcdef"), Is.Zero);
            Assert.That(algorithm.Contains("ab", "abcdef"), Is.False);
        });
    }

    [TestCase(TestName = "KMP: одиночное вхождение")]
    public void KmpSingleOccurrence()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("hello world", "world"), Is.EqualTo(1));
            Assert.That(algorithm.Contains("hello world", "world"), Is.True);
        });
    }

    [TestCase(TestName = "KMP: множественные вхождения")]
    public void KmpMultipleOccurrences()
    {
        var algorithm = new KmpAlgorithm();
        Assert.That(algorithm.CountOf("abababab", "ab"), Is.EqualTo(4));
    }

    [TestCase(TestName = "KMP: перекрывающиеся вхождения")]
    public void KmpOverlappingOccurrences()
    {
        var algorithm = new KmpAlgorithm();
        Assert.That(algorithm.CountOf("aaaa", "aa"), Is.EqualTo(3));
    }

    [TestCase(TestName = "KMP: регистронезависимый поиск")]
    public void KmpCaseInsensitive()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("Hello WORLD hello", "hello", StringComparison.OrdinalIgnoreCase), Is.EqualTo(2));
            Assert.That(algorithm.Contains("Hello WORLD", "world", StringComparison.OrdinalIgnoreCase), Is.True);
        });
    }

    [TestCase(TestName = "KMP: паттерн в начале строки")]
    public void KmpPatternAtStart()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains("hello world", "hello"), Is.True);
            Assert.That(algorithm.CountOf("hello world", "hello"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "KMP: паттерн в конце строки")]
    public void KmpPatternAtEnd()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains("hello world", "world"), Is.True);
            Assert.That(algorithm.CountOf("hello world", "world"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "KMP: паттерн равен источнику")]
    public void KmpPatternEqualsSource()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains("test", "test"), Is.True);
            Assert.That(algorithm.CountOf("test", "test"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "KMP: паттерн отсутствует")]
    public void KmpPatternNotFound()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains("hello world", "xyz"), Is.False);
            Assert.That(algorithm.CountOf("hello world", "xyz"), Is.Zero);
        });
    }

    [TestCase(TestName = "KMP: длинный текст"), Benchmark]
    public void KmpLongText()
    {
        var algorithm = new KmpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains(LongText, "needle"), Is.True);
            Assert.That(algorithm.CountOf(LongText, "needle"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "KMP: повторяющийся текст"), Benchmark]
    public void KmpRepeatingText()
    {
        var algorithm = new KmpAlgorithm();
        Assert.That(algorithm.CountOf(RepeatingText, "ab"), Is.EqualTo(5000));
    }

    #endregion

    #region Boyer-Moore Algorithm Tests

    [TestCase(TestName = "BoyerMoore: поиск в пустой строке")]
    public void BoyerMooreEmptySource()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("", "test"), Is.Zero);
            Assert.That(algorithm.Contains("", "test"), Is.False);
        });
    }

    [TestCase(TestName = "BoyerMoore: пустой паттерн")]
    public void BoyerMooreEmptyPattern()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("test", ""), Is.Zero);
            Assert.That(algorithm.Contains("test", ""), Is.False);
        });
    }

    [TestCase(TestName = "BoyerMoore: паттерн длиннее источника")]
    public void BoyerMoorePatternLongerThanSource()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("ab", "abcdef"), Is.Zero);
            Assert.That(algorithm.Contains("ab", "abcdef"), Is.False);
        });
    }

    [TestCase(TestName = "BoyerMoore: одиночное вхождение")]
    public void BoyerMooreSingleOccurrence()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("hello world", "world"), Is.EqualTo(1));
            Assert.That(algorithm.Contains("hello world", "world"), Is.True);
        });
    }

    [TestCase(TestName = "BoyerMoore: множественные вхождения")]
    public void BoyerMooreMultipleOccurrences()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.That(algorithm.CountOf("abababab", "ab"), Is.EqualTo(4));
    }

    [TestCase(TestName = "BoyerMoore: перекрывающиеся вхождения")]
    public void BoyerMooreOverlappingOccurrences()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.That(algorithm.CountOf("aaaa", "aa"), Is.EqualTo(3));
    }

    [TestCase(TestName = "BoyerMoore: регистронезависимый поиск")]
    public void BoyerMooreCaseInsensitive()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("Hello WORLD hello", "hello", StringComparison.OrdinalIgnoreCase), Is.EqualTo(2));
            Assert.That(algorithm.Contains("Hello WORLD", "world", StringComparison.OrdinalIgnoreCase), Is.True);
        });
    }

    [TestCase(TestName = "BoyerMoore: паттерн равен источнику")]
    public void BoyerMoorePatternEqualsSource()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains("test", "test"), Is.True);
            Assert.That(algorithm.CountOf("test", "test"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "BoyerMoore: длинный текст"), Benchmark]
    public void BoyerMooreLongText()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains(LongText, "needle"), Is.True);
            Assert.That(algorithm.CountOf(LongText, "needle"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "BoyerMoore: повторяющийся текст"), Benchmark]
    public void BoyerMooreRepeatingText()
    {
        var algorithm = new BoyerMooreAlgorithm();
        Assert.That(algorithm.CountOf(RepeatingText, "ab"), Is.EqualTo(5000));
    }

    #endregion

    #region Rabin-Karp Algorithm Tests

    [TestCase(TestName = "RabinKarp: поиск в пустой строке")]
    public void RabinKarpEmptySource()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("", "test"), Is.Zero);
            Assert.That(algorithm.Contains("", "test"), Is.False);
        });
    }

    [TestCase(TestName = "RabinKarp: пустой паттерн")]
    public void RabinKarpEmptyPattern()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("test", ""), Is.Zero);
            Assert.That(algorithm.Contains("test", ""), Is.False);
        });
    }

    [TestCase(TestName = "RabinKarp: паттерн длиннее источника")]
    public void RabinKarpPatternLongerThanSource()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("ab", "abcdef"), Is.Zero);
            Assert.That(algorithm.Contains("ab", "abcdef"), Is.False);
        });
    }

    [TestCase(TestName = "RabinKarp: одиночное вхождение")]
    public void RabinKarpSingleOccurrence()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("hello world", "world"), Is.EqualTo(1));
            Assert.That(algorithm.Contains("hello world", "world"), Is.True);
        });
    }

    [TestCase(TestName = "RabinKarp: множественные вхождения")]
    public void RabinKarpMultipleOccurrences()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.That(algorithm.CountOf("abababab", "ab"), Is.EqualTo(4));
    }

    [TestCase(TestName = "RabinKarp: перекрывающиеся вхождения")]
    public void RabinKarpOverlappingOccurrences()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.That(algorithm.CountOf("aaaa", "aa"), Is.EqualTo(3));
    }

    [TestCase(TestName = "RabinKarp: регистронезависимый поиск")]
    public void RabinKarpCaseInsensitive()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("Hello WORLD hello", "hello", StringComparison.OrdinalIgnoreCase), Is.EqualTo(2));
            Assert.That(algorithm.Contains("Hello WORLD", "world", StringComparison.OrdinalIgnoreCase), Is.True);
        });
    }

    [TestCase(TestName = "RabinKarp: паттерн равен источнику")]
    public void RabinKarpPatternEqualsSource()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains("test", "test"), Is.True);
            Assert.That(algorithm.CountOf("test", "test"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "RabinKarp: длинный текст"), Benchmark]
    public void RabinKarpLongText()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains(LongText, "needle"), Is.True);
            Assert.That(algorithm.CountOf(LongText, "needle"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "RabinKarp: повторяющийся текст"), Benchmark]
    public void RabinKarpRepeatingText()
    {
        var algorithm = new RabinKarpAlgorithm();
        Assert.That(algorithm.CountOf(RepeatingText, "ab"), Is.EqualTo(5000));
    }

    [TestCase(TestName = "RabinKarp: хэш-коллизии")]
    public void RabinKarpHashCollisionHandling()
    {
        var algorithm = new RabinKarpAlgorithm();
        // Строка с потенциальными коллизиями хэшей
        var source = "abcabcabcabcabcabc";
        Assert.That(algorithm.CountOf(source, "abc"), Is.EqualTo(6));
    }

    #endregion

    #region Z-Algorithm Tests

    [TestCase(TestName = "ZAlgorithm: поиск в пустой строке")]
    public void ZAlgorithmEmptySource()
    {
        var algorithm = new ZAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("", "test"), Is.Zero);
            Assert.That(algorithm.Contains("", "test"), Is.False);
        });
    }

    [TestCase(TestName = "ZAlgorithm: пустой паттерн")]
    public void ZAlgorithmEmptyPattern()
    {
        var algorithm = new ZAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("test", ""), Is.Zero);
            Assert.That(algorithm.Contains("test", ""), Is.False);
        });
    }

    [TestCase(TestName = "ZAlgorithm: паттерн длиннее источника")]
    public void ZAlgorithmPatternLongerThanSource()
    {
        var algorithm = new ZAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("ab", "abcdef"), Is.Zero);
            Assert.That(algorithm.Contains("ab", "abcdef"), Is.False);
        });
    }

    [TestCase(TestName = "ZAlgorithm: одиночное вхождение")]
    public void ZAlgorithmSingleOccurrence()
    {
        var algorithm = new ZAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("hello world", "world"), Is.EqualTo(1));
            Assert.That(algorithm.Contains("hello world", "world"), Is.True);
        });
    }

    [TestCase(TestName = "ZAlgorithm: множественные вхождения")]
    public void ZAlgorithmMultipleOccurrences()
    {
        var algorithm = new ZAlgorithm();
        Assert.That(algorithm.CountOf("abababab", "ab"), Is.EqualTo(4));
    }

    [TestCase(TestName = "ZAlgorithm: перекрывающиеся вхождения")]
    public void ZAlgorithmOverlappingOccurrences()
    {
        var algorithm = new ZAlgorithm();
        Assert.That(algorithm.CountOf("aaaa", "aa"), Is.EqualTo(3));
    }

    [TestCase(TestName = "ZAlgorithm: регистронезависимый поиск")]
    public void ZAlgorithmCaseInsensitive()
    {
        var algorithm = new ZAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("Hello WORLD hello", "hello", StringComparison.OrdinalIgnoreCase), Is.EqualTo(2));
            Assert.That(algorithm.Contains("Hello WORLD", "world", StringComparison.OrdinalIgnoreCase), Is.True);
        });
    }

    [TestCase(TestName = "ZAlgorithm: паттерн равен источнику")]
    public void ZAlgorithmPatternEqualsSource()
    {
        var algorithm = new ZAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains("test", "test"), Is.True);
            Assert.That(algorithm.CountOf("test", "test"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "ZAlgorithm: длинный текст"), Benchmark]
    public void ZAlgorithmLongText()
    {
        var algorithm = new ZAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains(LongText, "needle"), Is.True);
            Assert.That(algorithm.CountOf(LongText, "needle"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "ZAlgorithm: повторяющийся текст"), Benchmark]
    public void ZAlgorithmRepeatingText()
    {
        var algorithm = new ZAlgorithm();
        Assert.That(algorithm.CountOf(RepeatingText, "ab"), Is.EqualTo(5000));
    }

    #endregion

    #region Aho-Corasick Algorithm Tests

    [TestCase(TestName = "AhoCorasick: поиск в пустой строке")]
    public void AhoCorasickEmptySource()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("", "test"), Is.Zero);
            Assert.That(algorithm.Contains("", "test"), Is.False);
        });
    }

    [TestCase(TestName = "AhoCorasick: пустой паттерн")]
    public void AhoCorasickEmptyPattern()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("test", ""), Is.Zero);
            Assert.That(algorithm.Contains("test", ""), Is.False);
        });
    }

    [TestCase(TestName = "AhoCorasick: паттерн длиннее источника")]
    public void AhoCorasickPatternLongerThanSource()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("ab", "abcdef"), Is.Zero);
            Assert.That(algorithm.Contains("ab", "abcdef"), Is.False);
        });
    }

    [TestCase(TestName = "AhoCorasick: одиночное вхождение")]
    public void AhoCorasickSingleOccurrence()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("hello world", "world"), Is.EqualTo(1));
            Assert.That(algorithm.Contains("hello world", "world"), Is.True);
        });
    }

    [TestCase(TestName = "AhoCorasick: множественные вхождения")]
    public void AhoCorasickMultipleOccurrences()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.That(algorithm.CountOf("abababab", "ab"), Is.EqualTo(4));
    }

    [TestCase(TestName = "AhoCorasick: перекрывающиеся вхождения")]
    public void AhoCorasickOverlappingOccurrences()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.That(algorithm.CountOf("aaaa", "aa"), Is.EqualTo(3));
    }

    [TestCase(TestName = "AhoCorasick: регистронезависимый поиск")]
    public void AhoCorasickCaseInsensitive()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.CountOf("Hello WORLD hello", "hello", StringComparison.OrdinalIgnoreCase), Is.EqualTo(2));
            Assert.That(algorithm.Contains("Hello WORLD", "world", StringComparison.OrdinalIgnoreCase), Is.True);
        });
    }

    [TestCase(TestName = "AhoCorasick: паттерн равен источнику")]
    public void AhoCorasickPatternEqualsSource()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains("test", "test"), Is.True);
            Assert.That(algorithm.CountOf("test", "test"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "AhoCorasick: длинный текст"), Benchmark]
    public void AhoCorasickLongText()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.Multiple(() =>
        {
            Assert.That(algorithm.Contains(LongText, "needle"), Is.True);
            Assert.That(algorithm.CountOf(LongText, "needle"), Is.EqualTo(1));
        });
    }

    [TestCase(TestName = "AhoCorasick: повторяющийся текст"), Benchmark]
    public void AhoCorasickRepeatingText()
    {
        var algorithm = new AhoCorasickAlgorithm();
        Assert.That(algorithm.CountOf(RepeatingText, "ab"), Is.EqualTo(5000));
    }

    #endregion

    #region Cross-Algorithm Consistency Tests

    [TestCase(TestName = "Консистентность: все алгоритмы возвращают одинаковые результаты")]
    public void AllAlgorithmsReturnConsistentResults()
    {
        var algorithms = new ITextAlgorithm[]
        {
            new KmpAlgorithm(),
            new BoyerMooreAlgorithm(),
            new RabinKarpAlgorithm(),
            new ZAlgorithm(),
            new AhoCorasickAlgorithm()
        };

        var testCases = new (string Source, string Pattern)[]
        {
            ("hello world", "world"),
            ("abababab", "ab"),
            ("aaaa", "aa"),
            ("the quick brown fox jumps over the lazy dog", "the"),
            ("abcdefghijklmnop", "xyz"),
            (LongText, "needle"),
        };

        foreach (var (source, pattern) in testCases)
        {
            var expectedCount = algorithms[0].CountOf(source, pattern);
            var expectedContains = algorithms[0].Contains(source, pattern);

            for (var i = 1; i < algorithms.Length; i++)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(algorithms[i].CountOf(source, pattern), Is.EqualTo(expectedCount),
                        $"CountOf mismatch for {algorithms[i].GetType().Name} with pattern '{pattern}'");
                    Assert.That(algorithms[i].Contains(source, pattern), Is.EqualTo(expectedContains),
                        $"Contains mismatch for {algorithms[i].GetType().Name} with pattern '{pattern}'");
                });
            }
        }
    }

    [TestCase(TestName = "Консистентность: регистронезависимый поиск для всех алгоритмов")]
    public void AllAlgorithmsCaseInsensitiveConsistency()
    {
        var algorithms = new ITextAlgorithm[]
        {
            new KmpAlgorithm(),
            new BoyerMooreAlgorithm(),
            new RabinKarpAlgorithm(),
            new ZAlgorithm(),
            new AhoCorasickAlgorithm()
        };

        var source = "Hello World HELLO world HeLLo WoRLD";
        var pattern = "hello";
        var comparison = StringComparison.OrdinalIgnoreCase;

        var expectedCount = algorithms[0].CountOf(source, pattern, comparison);

        for (var i = 1; i < algorithms.Length; i++)
        {
            Assert.That(algorithms[i].CountOf(source, pattern, comparison), Is.EqualTo(expectedCount),
                $"Case-insensitive CountOf mismatch for {algorithms[i].GetType().Name}");
        }
    }

    #endregion

    #region Unicode Tests

    [TestCase(TestName = "Unicode: кириллица")]
    public void UnicodeTestCyrillic()
    {
        var algorithms = new ITextAlgorithm[]
        {
            new KmpAlgorithm(),
            new BoyerMooreAlgorithm(),
            new RabinKarpAlgorithm(),
            new ZAlgorithm(),
            new AhoCorasickAlgorithm()
        };

        foreach (var algorithm in algorithms)
        {
            Assert.That(algorithm.CountOf(UnicodeText, "Привет"), Is.EqualTo(2),
                $"Unicode Cyrillic test failed for {algorithm.GetType().Name}");
        }
    }

    [TestCase(TestName = "Unicode: японские символы")]
    public void UnicodeTestJapanese()
    {
        var algorithms = new ITextAlgorithm[]
        {
            new KmpAlgorithm(),
            new BoyerMooreAlgorithm(),
            new RabinKarpAlgorithm(),
            new ZAlgorithm(),
            new AhoCorasickAlgorithm()
        };

        foreach (var algorithm in algorithms)
        {
            Assert.That(algorithm.Contains(UnicodeText, "こんにちは"), Is.True,
                $"Unicode Japanese test failed for {algorithm.GetType().Name}");
        }
    }

    #endregion

    #region Stress Tests

    [TestCase(TestName = "Стресс-тест: параллельный поиск"), Benchmark]
    public void ParallelSearchStressTest()
    {
        var algorithm = new KmpAlgorithm();
        const int iterations = 1000;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, iterations, i =>
        {
            try
            {
                var result = algorithm.CountOf(LongText, "needle");
                Assert.That(result, Is.EqualTo(1));
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.That(exceptions, Is.Empty, "Параллельный поиск вызвал исключения");
    }

    [TestCase(TestName = "Стресс-тест: множество коротких поисков"), Benchmark]
    public void ManyShortSearchesStressTest()
    {
        var algorithms = new ITextAlgorithm[]
        {
            new KmpAlgorithm(),
            new BoyerMooreAlgorithm(),
            new RabinKarpAlgorithm(),
            new ZAlgorithm(),
            new AhoCorasickAlgorithm()
        };

        var source = "the quick brown fox jumps over the lazy dog";

        foreach (var algorithm in algorithms)
        {
            for (var i = 0; i < 10000; i++)
            {
                _ = algorithm.CountOf(source, "the");
                _ = algorithm.Contains(source, "fox");
            }
        }

        Assert.Pass("Множество коротких поисков завершено успешно");
    }

    #endregion

    #region Edge Case Tests

    [TestCase(TestName = "Edge: односимвольный паттерн")]
    public void SingleCharacterPattern()
    {
        var algorithms = new ITextAlgorithm[]
        {
            new KmpAlgorithm(),
            new BoyerMooreAlgorithm(),
            new RabinKarpAlgorithm(),
            new ZAlgorithm(),
            new AhoCorasickAlgorithm()
        };

        foreach (var algorithm in algorithms)
        {
            Assert.That(algorithm.CountOf("abcabc", "a"), Is.EqualTo(2),
                $"Single char pattern failed for {algorithm.GetType().Name}");
        }
    }

    [TestCase(TestName = "Edge: паттерн из одинаковых символов")]
    public void RepeatingCharacterPattern()
    {
        var algorithms = new ITextAlgorithm[]
        {
            new KmpAlgorithm(),
            new BoyerMooreAlgorithm(),
            new RabinKarpAlgorithm(),
            new ZAlgorithm(),
            new AhoCorasickAlgorithm()
        };

        foreach (var algorithm in algorithms)
        {
            Assert.That(algorithm.CountOf("aaaaaa", "aaa"), Is.EqualTo(4),
                $"Repeating char pattern failed for {algorithm.GetType().Name}");
        }
    }

    [TestCase(TestName = "Edge: специальные символы")]
    public void SpecialCharactersPattern()
    {
        var algorithms = new ITextAlgorithm[]
        {
            new KmpAlgorithm(),
            new BoyerMooreAlgorithm(),
            new RabinKarpAlgorithm(),
            new ZAlgorithm(),
            new AhoCorasickAlgorithm()
        };

        var source = "hello\tworld\nnew\rline";

        foreach (var algorithm in algorithms)
        {
            Assert.Multiple(() =>
            {
                Assert.That(algorithm.Contains(source, "\t"), Is.True,
                    $"Tab character failed for {algorithm.GetType().Name}");
                Assert.That(algorithm.Contains(source, "\n"), Is.True,
                    $"Newline character failed for {algorithm.GetType().Name}");
            });
        }
    }

    #endregion
}
