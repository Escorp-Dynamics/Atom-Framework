using System.IO.Compression;

namespace Atom.IO.Compression.Tests.Deflate;

/// <summary>
/// Тесты DeflateStream (RFC 1951).
/// </summary>
[TestFixture, CancelAfter(5000), Parallelizable(ParallelScope.All)]
public sealed class DeflateStreamTests
{

    #region Basic Functionality Tests

    [TestCase(TestName = "DeflateStream: сжатие и распаковка пустого массива")]
    public void CompressDecompressEmpty()
    {
        var original = Array.Empty<byte>();
        var roundTripped = RoundTrip(original);

        Assert.That(roundTripped, Is.EqualTo(original));
    }

    [TestCase(TestName = "DeflateStream: сжатие и распаковка одного байта")]
    public void CompressDecompressSingleByte()
    {
        byte[] original = [0x42];
        var roundTripped = RoundTrip(original);

        Assert.That(roundTripped, Is.EqualTo(original));
    }

    [TestCase(TestName = "DeflateStream: сжатие и распаковка маленьких данных")]
    public void CompressDecompressSmallData()
    {
        var original = "Hello, Deflate!"u8.ToArray();
        var roundTripped = RoundTrip(original);

        Assert.That(roundTripped, Is.EqualTo(original));
    }

    [TestCase(TestName = "DeflateStream: сжатие и распаковка повторяющихся данных")]
    public void CompressDecompressRepeatingData()
    {
        var original = new byte[1000];
        Array.Fill(original, (byte)'A');

        var roundTripped = RoundTrip(original);

        Assert.That(roundTripped, Is.EqualTo(original));
    }

    [TestCase(TestName = "DeflateStream: сжатие и распаковка случайных данных")]
    public void CompressDecompressRandomData()
    {
        var original = new byte[4096];
        Random.Shared.NextBytes(original);

        var roundTripped = RoundTrip(original);

        Assert.That(roundTripped, Is.EqualTo(original));
    }

    [TestCase(TestName = "DeflateStream: сжатие данных с паттерном LZ77")]
    public void CompressDecompressLZ77Pattern()
    {
        // Данные с повторяющимся паттерном, хорошо сжимаемые LZ77
        var pattern = "ABCDEFGHIJ"u8.ToArray();
        var original = new byte[pattern.Length * 100];
        for (var i = 0; i < 100; i++)
        {
            pattern.CopyTo(original.AsSpan(i * pattern.Length));
        }

        var roundTripped = RoundTrip(original);

        Assert.That(roundTripped, Is.EqualTo(original));
    }

    #endregion

    #region Compatibility Tests

    [TestCase(TestName = "DeflateStream: совместимость с System.IO.Compression (распаковка)")]
    public void CompatibilityWithSystemDecompress()
    {
        var original = "Testing compatibility with System.IO.Compression.DeflateStream"u8.ToArray();

        // Сжимаем с помощью System.IO.Compression
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var systemDeflate = new System.IO.Compression.DeflateStream(ms, CompressionMode.Compress))
            {
                systemDeflate.Write(original);
            }

            compressed = ms.ToArray();
        }

        // Распаковываем с помощью нашего DeflateStream
        byte[] decompressed;
        using (var ms = new MemoryStream(compressed))
        using (var ourDeflate = new DeflateStream(ms, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            ourDeflate.CopyTo(output);
            decompressed = output.ToArray();
        }

        Assert.That(decompressed, Is.EqualTo(original));
    }

    [TestCase(TestName = "DeflateStream: совместимость с System.IO.Compression (сжатие)")]
    public void CompatibilityWithSystemCompress()
    {
        var original = "Testing compatibility - our compression, system decompression"u8.ToArray();

        // Сжимаем с помощью нашего DeflateStream
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var ourDeflate = new DeflateStream(ms, CompressionMode.Compress))
            {
                ourDeflate.Write(original);
            }

            compressed = ms.ToArray();
        }

        // Распаковываем с помощью System.IO.Compression
        byte[] decompressed;
        using (var ms = new MemoryStream(compressed))
        using (var systemDeflate = new System.IO.Compression.DeflateStream(ms, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            systemDeflate.CopyTo(output);
            decompressed = output.ToArray();
        }

        Assert.That(decompressed, Is.EqualTo(original));
    }

    #endregion

    #region Functional Equivalence Tests (Compatibility with System.IO.Compression)

    /// <summary>
    /// Эти тесты проверяют, что наша реализация Deflate производит корректный вывод,
    /// который может быть распакован как нашим декодером, так и System.IO.Compression.
    /// Также сравниваем размер сжатых данных (не должен отличаться более чем на 20%).
    /// </summary>
    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: функциональная эквивалентность с System.IO.Compression при NoCompression")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность с System.IO.Compression при Fastest")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность с System.IO.Compression при Optimal")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность с System.IO.Compression при SmallestSize")]
    public void FunctionalEquivalenceWithSystemCompressionSimpleText(CompressionLevel level)
    {
        var original = "Hello, World! This is a simple test string for deflate compression."u8.ToArray();

        var atomCompressed = Compress(original, level);
        var systemCompressed = CompressWithSystem(original, level);

        AssertFunctionalEquivalence(original, atomCompressed, systemCompressed, level, "простой текст");
    }

    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: функциональная эквивалентность при NoCompression — повторяющиеся данные")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность при Fastest — повторяющиеся данные")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность при Optimal — повторяющиеся данные")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность при SmallestSize — повторяющиеся данные")]
    public void FunctionalEquivalenceWithSystemCompressionRepeatingData(CompressionLevel level)
    {
        // Повторяющийся паттерн — идеальный случай для LZ77
        var original = new byte[1000];
        Array.Fill(original, (byte)'A');

        var atomCompressed = Compress(original, level);
        var systemCompressed = CompressWithSystem(original, level);

        AssertFunctionalEquivalence(original, atomCompressed, systemCompressed, level, "повторяющиеся данные");
    }

    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: функциональная эквивалентность при NoCompression — паттерн LZ77")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность при Fastest — паттерн LZ77")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность при Optimal — паттерн LZ77")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность при SmallestSize — паттерн LZ77")]
    public void FunctionalEquivalenceWithSystemCompressionLZ77Pattern(CompressionLevel level)
    {
        // Данные с повторяющимся паттерном, хорошо сжимаемые LZ77
        var pattern = "ABCDEFGHIJKLMNOP"u8.ToArray();
        var original = new byte[pattern.Length * 50];
        for (var i = 0; i < 50; i++)
        {
            pattern.CopyTo(original.AsSpan(i * pattern.Length));
        }

        var atomCompressed = Compress(original, level);
        var systemCompressed = CompressWithSystem(original, level);

        AssertFunctionalEquivalence(original, atomCompressed, systemCompressed, level, "паттерн LZ77");
    }

    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: функциональная эквивалентность при NoCompression — случайные данные")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность при Fastest — случайные данные")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность при Optimal — случайные данные")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность при SmallestSize — случайные данные")]
    public void FunctionalEquivalenceWithSystemCompressionRandomData(CompressionLevel level)
    {
        // Случайные данные — худший случай для сжатия
        var original = new byte[4096];
        new Random(42).NextBytes(original); // Фиксированный seed для воспроизводимости

        var atomCompressed = Compress(original, level);
        var systemCompressed = CompressWithSystem(original, level);

        AssertFunctionalEquivalence(original, atomCompressed, systemCompressed, level, "случайные данные");
    }

    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: функциональная эквивалентность при NoCompression — пустой массив")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность при Fastest — пустой массив")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность при Optimal — пустой массив")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность при SmallestSize — пустой массив")]
    public void FunctionalEquivalenceWithSystemCompressionEmpty(CompressionLevel level)
    {
        var original = Array.Empty<byte>();

        var atomCompressed = Compress(original, level);

        // Для пустых данных: проверяем только, что round-trip работает
        var atomDecompressed = Decompress(atomCompressed);
        var systemDecompressed = DecompressWithSystem(atomCompressed);

        Assert.That(atomDecompressed, Is.EqualTo(original),
            $"Atom round-trip некорректен для пустого массива при {level}");
        Assert.That(systemDecompressed, Is.EqualTo(original),
            $"System не может распаковать Atom вывод для пустого массива при {level}");
    }

    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: функциональная эквивалентность при NoCompression — один байт")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность при Fastest — один байт")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность при Optimal — один байт")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность при SmallestSize — один байт")]
    public void FunctionalEquivalenceWithSystemCompressionSingleByte(CompressionLevel level)
    {
        byte[] original = [0x42];

        var atomCompressed = Compress(original, level);
        var systemCompressed = CompressWithSystem(original, level);

        AssertFunctionalEquivalence(original, atomCompressed, systemCompressed, level, "один байт");
    }

    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: функциональная эквивалентность при NoCompression — большие данные")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность при Fastest — большие данные")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность при Optimal — большие данные")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность при SmallestSize — большие данные")]
    public void FunctionalEquivalenceWithSystemCompressionLargeData(CompressionLevel level)
    {
        // Большие данные с различными паттернами
        var original = GenerateCompressibleData(64 * 1024); // 64 KB

        var atomCompressed = Compress(original, level);
        var systemCompressed = CompressWithSystem(original, level);

        AssertFunctionalEquivalence(original, atomCompressed, systemCompressed, level, "большие данные (64 KB)");
    }

    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность при Fastest — очень большие данные 1MB")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность при Optimal — очень большие данные 1MB")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность при SmallestSize — очень большие данные 1MB")]
    public void FunctionalEquivalenceWithSystemCompressionVeryLargeData(CompressionLevel level)
    {
        var original = GenerateCompressibleData(1024 * 1024);

        // Тест 1: Наш decoder на system bitstream (изолирует баг decoder)
        var systemCompressed = CompressWithSystem(original, level);
        var atomFromSystem = Decompress(systemCompressed);
        Assert.That(atomFromSystem, Is.EqualTo(original), $"Atom decoder failed on system bitstream: got {atomFromSystem.Length} bytes, expected {original.Length}");

        // Тест 2: System decoder на нашем bitstream (изолирует баг encoder)
        var atomCompressed = Compress(original, level);
        var systemDecompressed = DecompressWithSystem(atomCompressed);
        Assert.That(systemDecompressed, Is.EqualTo(original), "System decoder failed on our bitstream");

        // Тест 3: Наш roundtrip
        var atomDecompressed = Decompress(atomCompressed);
        Assert.That(atomDecompressed, Is.EqualTo(original), $"Atom roundtrip failed: got {atomDecompressed.Length} bytes, expected {original.Length}");
    }

    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: функциональная эквивалентность при NoCompression — длинные матчи")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность при Fastest — длинные матчи")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность при Optimal — длинные матчи")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность при SmallestSize — длинные матчи")]
    public void FunctionalEquivalenceWithSystemCompressionLongMatches(CompressionLevel level)
    {
        // Данные с длинными матчами (близко к MaxMatch = 258)
        var original = new byte[10000];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 10);
        }

        var atomCompressed = Compress(original, level);
        var systemCompressed = CompressWithSystem(original, level);

        AssertFunctionalEquivalence(original, atomCompressed, systemCompressed, level, "длинные матчи");
    }

    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: функциональная эквивалентность при NoCompression — все байты")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: функциональная эквивалентность при Fastest — все байты")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: функциональная эквивалентность при Optimal — все байты")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: функциональная эквивалентность при SmallestSize — все байты")]
    public void FunctionalEquivalenceWithSystemCompressionAllBytes(CompressionLevel level)
    {
        // Все 256 байтовых значений
        var original = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            original[i] = (byte)i;
        }

        var atomCompressed = Compress(original, level);
        var systemCompressed = CompressWithSystem(original, level);

        AssertFunctionalEquivalence(original, atomCompressed, systemCompressed, level, "все байты 0-255");
    }

    /// <summary>
    /// Проверяет функциональную эквивалентность:
    /// 1. Atom сжатие → Atom декомпрессия = оригинал (round-trip)
    /// 2. Atom сжатие → System декомпрессия = оригинал (совместимость)
    /// 3. System сжатие → Atom декомпрессия = оригинал (совместимость)
    /// 4. Размер сжатых данных не должен отличаться более чем на 20%
    /// </summary>
    private static void AssertFunctionalEquivalence(
        byte[] original,
        byte[] atomCompressed,
        byte[] systemCompressed,
        CompressionLevel level,
        string dataDescription)
    {
        // 1. Atom round-trip
        var atomDecompressed = Decompress(atomCompressed);
        Assert.That(atomDecompressed, Is.EqualTo(original),
            $"Atom round-trip некорректен для {dataDescription} при {level}");

        // 2. Atom → System декомпрессия
        var systemDecompressedAtom = DecompressWithSystem(atomCompressed);
        Assert.That(systemDecompressedAtom, Is.EqualTo(original),
            $"System не может корректно распаковать Atom вывод для {dataDescription} при {level}");

        // 3. System → Atom декомпрессия
        var atomDecompressedSystem = Decompress(systemCompressed);
        Assert.That(atomDecompressedSystem, Is.EqualTo(original),
            $"Atom не может корректно распаковать System вывод для {dataDescription} при {level}");

        // 4. Сравнение размеров (не строгое, но разумное)
        // Для NoCompression размеры должны быть примерно одинаковы
        // Для других уровней допускаем 20% разницу
        if (original.Length > 0)
        {
            var sizeDiff = Math.Abs(atomCompressed.Length - systemCompressed.Length);
            var maxAllowedDiff = level == CompressionLevel.NoCompression
                ? original.Length * 0.05  // 5% для NoCompression
                : original.Length * 0.20; // 20% для остальных

            Assert.That(sizeDiff, Is.LessThanOrEqualTo(maxAllowedDiff),
                $"Размер сжатых данных отличается слишком сильно для {dataDescription} при {level}:\n" +
                $"  Atom: {atomCompressed.Length} байт\n" +
                $"  System: {systemCompressed.Length} байт\n" +
                $"  Разница: {sizeDiff} байт (максимум: {maxAllowedDiff:F0})");
        }
    }

    #endregion

    #region Stream API Tests

    [TestCase(TestName = "DeflateStream: CanRead возвращает true при распаковке")]
    public void CanReadTrueWhenDecompressing()
    {
        using var ms = new MemoryStream([0x03, 0x00]); // Empty deflate block
        using var ds = new DeflateStream(ms, CompressionMode.Decompress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ds.CanRead, Is.True);
            Assert.That(ds.CanWrite, Is.False);
        }

    }

    [TestCase(TestName = "DeflateStream: CanWrite возвращает true при сжатии")]
    public void CanWriteTrueWhenCompressing()
    {
        using var ms = new MemoryStream();
        using var ds = new DeflateStream(ms, CompressionMode.Compress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ds.CanWrite, Is.True);
            Assert.That(ds.CanRead, Is.False);
        }

    }

    [TestCase(TestName = "DeflateStream: CanSeek всегда false")]
    public void CanSeekAlwaysFalse()
    {
        using var ms = new MemoryStream();
        using var ds = new DeflateStream(ms, CompressionMode.Compress);

        Assert.That(ds.CanSeek, Is.False);
    }

    [TestCase(TestName = "DeflateStream: Seek выбрасывает NotSupportedException")]
    public void SeekThrowsNotSupportedException()
    {
        using var ms = new MemoryStream();
        using var ds = new DeflateStream(ms, CompressionMode.Compress);

        Assert.Throws<NotSupportedException>(() => ds.Seek(0, SeekOrigin.Begin));
    }

    [TestCase(TestName = "DeflateStream: BaseStream возвращает базовый поток")]
    public void BaseStreamReturnsUnderlyingStream()
    {
        using var ms = new MemoryStream();
        using var ds = new DeflateStream(ms, CompressionMode.Compress);

        Assert.That(ds.BaseStream, Is.SameAs(ms));
    }

    #endregion

    #region Compression Level Tests

    [TestCase(TestName = "DeflateStream: NoCompression уровень сохраняет данные")]
    public void NoCompressionLevelPreservesData()
    {
        var original = new byte[100];
        Random.Shared.NextBytes(original);

        var roundTripped = RoundTrip(original, CompressionLevel.NoCompression);

        Assert.That(roundTripped, Is.EqualTo(original));
    }

    [TestCase(TestName = "DeflateStream: Fastest уровень сжимает данные")]
    public void FastestLevelCompressesData()
    {
        var original = new byte[1000];
        Array.Fill(original, (byte)'X');

        var compressed = Compress(original, CompressionLevel.Fastest);

        Assert.That(compressed.Length, Is.LessThan(original.Length));
    }

    [TestCase(TestName = "DeflateStream: Optimal уровень сжимает лучше Fastest")]
    public void OptimalLevelCompressesBetterThanFastest()
    {
        var original = new byte[10000];
        for (var i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 256);
        }

        var fastestCompressed = Compress(original, CompressionLevel.Fastest);
        var optimalCompressed = Compress(original, CompressionLevel.Optimal);

        // Optimal должен сжать не хуже Fastest
        Assert.That(optimalCompressed.Length, Is.LessThanOrEqualTo(fastestCompressed.Length));
    }

    #endregion

    #region Helper Methods

    private static byte[] RoundTrip(byte[] original, CompressionLevel level = CompressionLevel.Optimal)
    {
        var compressed = Compress(original, level);
        return Decompress(compressed);
    }

    private static byte[] Compress(byte[] data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, level))
        {
            ds.Write(data);
        }

        return ms.ToArray();
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using var ms = new MemoryStream(compressed);
        using var ds = new DeflateStream(ms, CompressionMode.Decompress);
        using var output = new MemoryStream();

        ds.CopyTo(output);
        return output.ToArray();
    }

    #endregion

    #region Performance Comparison

    /// <summary>
    /// Минимальное ускорение относительно System.IO.Compression (нативный zlib).
    /// Atom должен быть минимум на 35% быстрее во всех режимах.
    /// </summary>
    private const double MinSpeedup = 1.35;

    /// <summary>
    /// Количество прогревочных итераций для JIT.
    /// </summary>
    private const int WarmupIterations = 10;

    /// <summary>
    /// Количество замерных итераций.
    /// </summary>
    private const int BenchmarkIterations = 20;

    /// <summary>
    /// Размеры данных для тестирования: маленькие, средние, большие.
    /// </summary>
    private static readonly int[] TestDataSizes = [1024, 64 * 1024, 1024 * 1024]; // 1KB, 64KB, 1MB

    [TestCase(TestName = "DeflateStream: бенчмарк сжатия vs System.IO.Compression")]
    public void BenchmarkCompression()
    {
        // Генерируем тестовые данные с хорошей сжимаемостью
        var data = GenerateCompressibleData(1024 * 1024); // 1 MB

        // Warmup
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = Compress(data, CompressionLevel.Fastest);
            _ = CompressWithSystem(data, CompressionLevel.Fastest);
        }

        // Atom.IO.Compression
        var atomSw = System.Diagnostics.Stopwatch.StartNew();
        byte[] atomCompressed = null!;
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            atomCompressed = Compress(data, CompressionLevel.Fastest);
        }
        atomSw.Stop();

        // System.IO.Compression
        var systemSw = System.Diagnostics.Stopwatch.StartNew();
        byte[] systemCompressed = null!;
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            systemCompressed = CompressWithSystem(data, CompressionLevel.Fastest);
        }
        systemSw.Stop();

        var atomMs = atomSw.Elapsed.TotalMilliseconds / BenchmarkIterations;
        var systemMs = systemSw.Elapsed.TotalMilliseconds / BenchmarkIterations;
        var speedup = systemMs / atomMs;
        var sizeRatio = (double)atomCompressed.Length / systemCompressed.Length;

        TestContext.Out.WriteLine($"=== Сжатие 1 MB данных (Fastest) ===");
        TestContext.Out.WriteLine($"Atom.IO.Compression: {atomMs:F2} ms, размер: {atomCompressed.Length:N0} байт");
        TestContext.Out.WriteLine($"System.IO.Compression: {systemMs:F2} ms, размер: {systemCompressed.Length:N0} байт");
        TestContext.Out.WriteLine($"Ускорение: {speedup:F2}x (требуется: >={MinSpeedup:F2}x)");
        TestContext.Out.WriteLine($"Размер Atom/System: {sizeRatio:F3}x (требуется: <=1.0x)");

        // Проверяем корректность
        Assert.That(Decompress(atomCompressed), Is.EqualTo(data), "Ошибка roundtrip");

        // Проверяем производительность
        Assert.That(speedup, Is.GreaterThanOrEqualTo(MinSpeedup),
            $"Сжатие слишком медленное: {speedup:F2}x < {MinSpeedup:F2}x");

        // Проверяем размер
        Assert.That(atomCompressed.Length, Is.LessThanOrEqualTo(systemCompressed.Length),
            $"Размер сжатых данных больше чем у System: {atomCompressed.Length} > {systemCompressed.Length}");
    }

    [TestCase(TestName = "DeflateStream: бенчмарк распаковки vs System.IO.Compression")]
    public void BenchmarkDecompression()
    {
        var data = GenerateCompressibleData(1024 * 1024); // 1 MB

        // Сжимаем системным компрессором для честного сравнения
        var compressed = CompressWithSystem(data, CompressionLevel.Fastest);

        // Warmup
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = Decompress(compressed);
            _ = DecompressWithSystem(compressed);
        }

        // Atom.IO.Compression
        var atomSw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            _ = Decompress(compressed);
        }
        atomSw.Stop();

        // System.IO.Compression
        var systemSw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            _ = DecompressWithSystem(compressed);
        }
        systemSw.Stop();

        var atomMs = atomSw.Elapsed.TotalMilliseconds / BenchmarkIterations;
        var systemMs = systemSw.Elapsed.TotalMilliseconds / BenchmarkIterations;
        var speedup = systemMs / atomMs;

        TestContext.Out.WriteLine($"=== Распаковка 1 MB данных ===");
        TestContext.Out.WriteLine($"Atom.IO.Compression: {atomMs:F2} ms");
        TestContext.Out.WriteLine($"System.IO.Compression: {systemMs:F2} ms");
        TestContext.Out.WriteLine($"Ускорение: {speedup:F2}x (требуется: >={MinSpeedup:F2}x)");

        // Проверяем корректность
        Assert.That(Decompress(compressed), Is.EqualTo(data), "Ошибка roundtrip");

        // Проверяем производительность
        Assert.That(speedup, Is.GreaterThanOrEqualTo(MinSpeedup),
            $"Распаковка слишком медленная: {speedup:F2}x < {MinSpeedup:F2}x");
    }

    [TestCase(CompressionLevel.NoCompression, TestName = "DeflateStream: полный бенчмарк NoCompression (все размеры)")]
    [TestCase(CompressionLevel.Fastest, TestName = "DeflateStream: полный бенчмарк Fastest (все размеры)")]
    [TestCase(CompressionLevel.Optimal, TestName = "DeflateStream: полный бенчмарк Optimal (все размеры)")]
    [TestCase(CompressionLevel.SmallestSize, TestName = "DeflateStream: полный бенчмарк SmallestSize (все размеры)")]
    public void BenchmarkCompressionLevel(CompressionLevel level)
    {
        var levelName = level switch
        {
            CompressionLevel.NoCompression => "NoCompression",
            CompressionLevel.Fastest => "Fastest",
            CompressionLevel.Optimal => "Optimal",
            CompressionLevel.SmallestSize => "SmallestSize",
            _ => level.ToString()
        };

        TestContext.Out.WriteLine($"=== Бенчмарк {levelName} ===");
        TestContext.Out.WriteLine();

        foreach (var size in TestDataSizes)
        {
            var sizeName = size switch
            {
                1024 => "1 KB (маленькие)",
                64 * 1024 => "64 KB (средние)",
                1024 * 1024 => "1 MB (большие)",
                _ => $"{size / 1024} KB"
            };

            var data = GenerateCompressibleData(size);
            BenchmarkSingleConfiguration(data, level, sizeName);
        }
    }

    private static void BenchmarkSingleConfiguration(byte[] data, CompressionLevel level, string sizeName)
    {
        var levelName = level switch
        {
            CompressionLevel.NoCompression => "NoCompression",
            CompressionLevel.Fastest => "Fastest",
            CompressionLevel.Optimal => "Optimal",
            CompressionLevel.SmallestSize => "SmallestSize",
            _ => level.ToString()
        };

        // Warmup
        for (var i = 0; i < WarmupIterations; i++)
        {
            var c1 = Compress(data, level);
            _ = Decompress(c1);
            var c2 = CompressWithSystem(data, level);
            _ = DecompressWithSystem(c2);
        }

        // Benchmark сжатия Atom
        var atomCompressSw = System.Diagnostics.Stopwatch.StartNew();
        byte[] atomCompressed = null!;
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            atomCompressed = Compress(data, level);
        }
        atomCompressSw.Stop();

        // Benchmark сжатия System
        var systemCompressSw = System.Diagnostics.Stopwatch.StartNew();
        byte[] systemCompressed = null!;
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            systemCompressed = CompressWithSystem(data, level);
        }
        systemCompressSw.Stop();

        // Benchmark распаковки Atom (используем systemCompressed для честности)
        var atomDecompressSw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            _ = Decompress(systemCompressed);
        }
        atomDecompressSw.Stop();

        // Benchmark распаковки System
        var systemDecompressSw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < BenchmarkIterations; i++)
        {
            _ = DecompressWithSystem(systemCompressed);
        }
        systemDecompressSw.Stop();

        // Расчёт метрик
        var atomCompressMs = atomCompressSw.Elapsed.TotalMilliseconds / BenchmarkIterations;
        var systemCompressMs = systemCompressSw.Elapsed.TotalMilliseconds / BenchmarkIterations;
        var atomDecompressMs = atomDecompressSw.Elapsed.TotalMilliseconds / BenchmarkIterations;
        var systemDecompressMs = systemDecompressSw.Elapsed.TotalMilliseconds / BenchmarkIterations;

        var compressSpeedup = systemCompressMs / atomCompressMs;
        var decompressSpeedup = systemDecompressMs / atomDecompressMs;
        var sizeRatio = (double)atomCompressed.Length / systemCompressed.Length;

        TestContext.Out.WriteLine($"--- {sizeName} ---");
        TestContext.Out.WriteLine($"  Сжатие:     Atom {atomCompressMs:F3}ms vs System {systemCompressMs:F3}ms → {compressSpeedup:F2}x");
        TestContext.Out.WriteLine($"  Распаковка: Atom {atomDecompressMs:F3}ms vs System {systemDecompressMs:F3}ms → {decompressSpeedup:F2}x");
        TestContext.Out.WriteLine($"  Размер:     Atom {atomCompressed.Length:N0} vs System {systemCompressed.Length:N0} → {sizeRatio:F3}x");
        TestContext.Out.WriteLine();

        // Проверяем корректность
        Assert.That(Decompress(atomCompressed), Is.EqualTo(data),
            $"Ошибка roundtrip для {levelName} {sizeName}");

        // Проверяем производительность сжатия
        Assert.That(compressSpeedup, Is.GreaterThanOrEqualTo(MinSpeedup),
            $"Сжатие {levelName} {sizeName} слишком медленное: {compressSpeedup:F2}x < {MinSpeedup:F2}x");

        // Проверяем производительность распаковки
        Assert.That(decompressSpeedup, Is.GreaterThanOrEqualTo(MinSpeedup),
            $"Распаковка {levelName} {sizeName} слишком медленная: {decompressSpeedup:F2}x < {MinSpeedup:F2}x");

        // Проверяем размер
        Assert.That(atomCompressed.Length, Is.LessThanOrEqualTo(systemCompressed.Length),
            $"Размер {levelName} {sizeName} больше чем у System: {atomCompressed.Length} > {systemCompressed.Length}");
    }

    [TestCase(TestName = "DeflateStream: бенчмарк всех уровней сжатия vs System.IO.Compression")]
    public void BenchmarkAllCompressionLevels()
    {
        var data = GenerateCompressibleData(1024 * 1024); // 1 MB
        CompressionLevel[] levels =
        [
            CompressionLevel.NoCompression,
            CompressionLevel.Fastest,
            CompressionLevel.Optimal,
            CompressionLevel.SmallestSize
        ];

        // Собираем результаты
        var results = new List<(CompressionLevel Level, double AtomComp, double AtomDecomp, int AtomSize, double SysComp, double SysDecomp, int SysSize)>();
        var failures = new List<string>();

        foreach (var level in levels)
        {
            var (CompressMs, DecompressMs, CompressedSize) = BenchmarkLevel(data, level, WarmupIterations, BenchmarkIterations, isAtom: true);
            var sys = BenchmarkLevel(data, level, WarmupIterations, BenchmarkIterations, isAtom: false);
            results.Add((level, CompressMs, DecompressMs, CompressedSize, sys.CompressMs, sys.DecompressMs, sys.CompressedSize));
        }

        // Вывод таблицы
        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine($"┌──────────────────┬───────────────────────────────────────┬───────────────────────────────────────┬──────────────────────┐");
        TestContext.Out.WriteLine($"│                  │             ATOM (managed)            │         SYSTEM (native zlib)          │    ATOM / SYSTEM     │");
        TestContext.Out.WriteLine($"│      УРОВЕНЬ     ├───────────┬───────────┬───────┬───────┼───────────┬───────────┬───────┬───────┼──────────┬───────────┤");
        TestContext.Out.WriteLine($"│                  │  Сжатие   │ Распак.   │ Всего │ Коэф. │  Сжатие   │ Распак.   │ Всего │ Коэф. │  Сжатие  │  Распак.  │");
        TestContext.Out.WriteLine($"├──────────────────┼───────────┼───────────┼───────┼───────┼───────────┼───────────┼───────┼───────┼──────────┼───────────┤");

        foreach (var (Level, AtomComp, AtomDecomp, AtomSize, SysComp, SysDecomp, SysSize) in results)
        {
            var atomTotal = AtomComp + AtomDecomp;
            var sysTotal = SysComp + SysDecomp;
            var atomRatio = (double)data.Length / AtomSize;
            var sysRatio = (double)data.Length / SysSize;
            var compSpeedup = SysComp / AtomComp;
            var decompSpeedup = SysDecomp / AtomDecomp;

            var levelName = Level switch
            {
                CompressionLevel.NoCompression => "NoCompression",
                CompressionLevel.Fastest => "Fastest",
                CompressionLevel.Optimal => "Optimal",
                CompressionLevel.SmallestSize => "SmallestSize",
                _ => Level.ToString()
            };

            TestContext.Out.WriteLine(
                $"│ {levelName,-16} │ {AtomComp,7:F1} ms │ {AtomDecomp,7:F1} ms │ {atomTotal,5:F0} ms│ {atomRatio,5:F2}│ " +
                $"{SysComp,7:F1} ms │ {SysDecomp,7:F1} ms │ {sysTotal,5:F0} ms│ {sysRatio,5:F2}│ " +
                $"{compSpeedup,7:F2}x  │ {decompSpeedup,8:F2}x  │");

            // Проверяем производительность
            if (compSpeedup < MinSpeedup)
            {
                failures.Add($"Сжатие {levelName}: {compSpeedup:F2}x < {MinSpeedup:F2}x");
            }

            if (decompSpeedup < MinSpeedup)
            {
                failures.Add($"Распаковка {levelName}: {decompSpeedup:F2}x < {MinSpeedup:F2}x");
            }

            // Проверяем размер
            if (AtomSize > SysSize)
            {
                failures.Add($"Размер {levelName}: Atom {AtomSize} > System {SysSize}");
            }
        }

        TestContext.Out.WriteLine($"└──────────────────┴───────────┴───────────┴───────┴───────┴───────────┴───────────┴───────┴───────┴──────────┴───────────┘");
        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine($"Размер исходных данных: {data.Length:N0} байт ({data.Length / 1024} KB)");
        TestContext.Out.WriteLine($"Требуемое ускорение: >= {MinSpeedup:F2}x");
        TestContext.Out.WriteLine();

        // Финальная проверка корректности
        foreach (var level in levels)
        {
            var compressed = Compress(data, level);
            Assert.That(Decompress(compressed), Is.EqualTo(data), $"Ошибка roundtrip для уровня {level}");
        }

        // Выводим все ошибки производительности
        if (failures.Count > 0)
        {
            TestContext.Out.WriteLine("=== ПРОВАЛЕНО ===");
            foreach (var f in failures)
            {
                TestContext.Out.WriteLine($"  ❌ {f}");
            }
            Assert.Fail($"Производительность не соответствует требованиям:\n{string.Join("\n", failures)}");
        }
    }

    private static (double CompressMs, double DecompressMs, int CompressedSize) BenchmarkLevel(
        byte[] data,
        CompressionLevel level,
        int warmupIterations,
        int iterations,
        bool isAtom)
    {
        byte[] compressed = null!;

        // Warmup
        for (var i = 0; i < warmupIterations; i++)
        {
            compressed = isAtom ? Compress(data, level) : CompressWithSystem(data, level);
            _ = isAtom ? Decompress(compressed) : DecompressWithSystem(compressed);
        }

        // Замер сжатия
        var compressSw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            compressed = isAtom ? Compress(data, level) : CompressWithSystem(data, level);
        }
        compressSw.Stop();

        // Замер распаковки
        var decompressSw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            _ = isAtom ? Decompress(compressed) : DecompressWithSystem(compressed);
        }
        decompressSw.Stop();

        return (
            compressSw.Elapsed.TotalMilliseconds / iterations,
            decompressSw.Elapsed.TotalMilliseconds / iterations,
            compressed.Length
        );
    }

    private static byte[] GenerateCompressibleData(int size)
    {
        var data = new byte[size];
        var rnd = new Random(42);

        // Смесь паттернов для реалистичного сжатия
        for (var i = 0; i < size; i++)
        {
            // 70% повторяющиеся паттерны, 30% случайные
            data[i] = rnd.Next(100) < 70
                ? (byte)((i % 64) + 32)
                : (byte)rnd.Next(256);
        }

        return data;
    }

    private static byte[] CompressWithSystem(byte[] data, CompressionLevel level)
    {
        using var ms = new MemoryStream();
        using (var ds = new System.IO.Compression.DeflateStream(ms, level))
        {
            ds.Write(data);
        }
        return ms.ToArray();
    }

    private static byte[] DecompressWithSystem(byte[] compressed)
    {
        using var ms = new MemoryStream(compressed);
        using var ds = new System.IO.Compression.DeflateStream(ms, CompressionMode.Decompress);
        using var output = new MemoryStream();
        ds.CopyTo(output);
        return output.ToArray();
    }

    #endregion
}
