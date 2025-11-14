using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.IO.Compression.Tests;

/// <summary>
/// Набор тестов для проверки корректности поведения ZstdStream на валидных данных.
/// </summary>
[TestFixture, CancelAfter(5000), Parallelizable(ParallelScope.All)]
public class ZstdStreamTests(ILogger logger) : BenchmarkTests<ZstdStreamTests>(logger)
{
    private const int DataLength = 1048576;
    private const int CompressionLevel = 3;

    private static readonly byte[] testData = DataFactory.GenerateText(DataLength);

    public override bool IsBenchmarkEnabled => default;

    private static IEnumerable<TestCaseData> CrossPairs
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var codecs = DataFactory.Codecs;

            foreach (var enc in codecs)
            {
                foreach (var dec in codecs)
                {
                    yield return new TestCaseData(enc, dec).SetName($"Кросс-тест ({enc.Name}-{dec.Name})");
                }
            }
        }
    }

    private static IEnumerable<TestCaseData> CompressionStreamTestData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetTestData("CompressionStreamTest");
    }

    private static IEnumerable<TestCaseData> ContentIntegrityHashTestData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetTestData("ContentIntegrityHashTest", default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdStreamTests() : this(ConsoleLogger.Unicode) { }

    [TestCaseSource(nameof(CompressionStreamTestData))]
    public void CompressionStreamTest(ICodec codec, int compressionLevel, int ioChunk, (string Name, byte[] Data) dataSet)
    {
        using var src = new MemoryStream(dataSet.Data, writable: false);
        using var mid = new MemoryStream();
        codec.CompressStream(src, mid, compressionLevel, ioChunk);

        using var dst = new MemoryStream();

        mid.Position = 0;

        codec.DecompressStream(mid, dst, ioChunk);
        var plain = dst.ToArray();

        if (!IsBenchmarkEnabled) Assert.That(plain, Is.EqualTo(dataSet.Data));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StreamTest<T>() where T : ICodec, new()
    {
        //var data = DataFactory.GenerateText(dataLength);
        var data = testData;
        var codec = new T();

        using var src = new MemoryStream(data, writable: false);
        using var mid = new MemoryStream();
        codec.CompressStream(src, mid, CompressionLevel, 4096);

        using var dst = new MemoryStream();

        mid.Position = 0;

        codec.DecompressStream(mid, dst, 4096);
        var plain = dst.ToArray();

        if (!IsBenchmarkEnabled) Assert.That(plain, Is.EqualTo(data));
    }

    [TestCase(TestName = "Atom"), Benchmark(Description = "Atom", Baseline = true)]
    public void AtomZstdStreamTest() => StreamTest<AtomZstdCodec>();

    [TestCase(TestName = "ZstdNet"), Benchmark(Description = "ZstdNet")]
    public void ZstdNetStreamTest() => StreamTest<ZstdNetCodec>();

    [TestCase(TestName = "ZstdSharp"), Benchmark(Description = "ZstdSharp")]
    public void ZstdSharpStreamTest() => StreamTest<ZstdSharpCodec>();

    [TestCaseSource(nameof(CrossPairs)), Ignore("")]
    public void CrossStreamedRobustnessTest(ICodec encoder, ICodec decoder)
    {
        foreach (var level in DataFactory.Levels)
        {
            foreach (var (name, data) in DataFactory.DataSets)
            {
                foreach (var ioEnc in DataFactory.IoChunks)
                {
                    foreach (var ioDec in DataFactory.IoChunks)
                    {
                        using var src = new MemoryStream(data, writable: false);
                        using var mid = new MemoryStream();
                        using var dst = new MemoryStream();

                        encoder.CompressStream(src, mid, level, ioEnc);
                        mid.Position = 0;
                        decoder.DecompressStream(mid, dst, ioDec);

                        var plain = dst.ToArray();
                        Assert.That(plain, Is.EqualTo(data),
                            $"Cross streamed {encoder.Name}→{decoder.Name} failed on data={name}, level={level}, encChunk={ioEnc}, decChunk={ioDec}. " +
                            $"compressed={mid.Length}, src={data.Length}");
                    }
                }
            }
        }
    }

    [TestCaseSource(nameof(ContentIntegrityHashTestData))]
    public void ContentIntegrityHashTest(ICodec codec, int compressionLevel, (string Name, byte[] Data) dataSet)
    {
        var hashSrc = SHA256.HashData(dataSet.Data);
        var cmp = codec.Compress(dataSet.Data, compressionLevel);
        var plain = codec.Decompress(cmp);
        var hashOut = SHA256.HashData(plain);

        Assert.That(hashOut, Is.EqualTo(hashSrc), $"Integrity failed (SHA256 mismatch) on data={dataSet.Name}, level={compressionLevel}");
    }

    [Test]
    public void SkippableBlocksToleranceTest()
    {
        // Проверка на устойчивость к skippable‑блокам:
        // Вставляем skippable‑фрейм перед и после корректного кадра и убеждаемся, что наш декодер вычитывает данные.
        var codec = new AtomZstdCodec();
        var payload = Enumerable.Range(0, 50_000).Select(i => (byte)(i * 31)).ToArray();

        var frame = codec.Compress(payload, level: 3);
        using var mix = new MemoryStream();

        // skippable: 0x184D2A5E + size(LE) + padding
        static void WriteSkippable(System.IO.Stream s, int size)
        {
            // magic
            s.WriteByte(0x5E); s.WriteByte(0x2A); s.WriteByte(0x4D); s.WriteByte(0x18);
            // length (LE)
            var len = BitConverter.GetBytes(size);
            if (!BitConverter.IsLittleEndian) Array.Reverse(len);
            s.Write(len, 0, 4);
            // body
            for (var i = 0; i < size; i++) s.WriteByte((byte)(i & 0xFF));
        }

        WriteSkippable(mix, 17);
        mix.Write(frame, 0, frame.Length);
        WriteSkippable(mix, 33);
        mix.Position = 0;

        // Разбор: наш декодер должен пропустить оба skippable и корректно распаковать реальный кадр.
        var plain = codec.Decompress(mix.ToArray());
        Assert.That(plain, Is.EqualTo(payload), "Skippable blocks handling failed");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IEnumerable<TestCaseData> GetTestData(string testName, bool ioChunksEnabled = true)
    {
        var codecs = DataFactory.Codecs;

        foreach (var enc in codecs)
        {
            foreach (var level in DataFactory.Levels)
            {
                if (ioChunksEnabled)
                {
                    foreach (var ioChunk in DataFactory.IoChunks)
                    {
                        foreach (var dataSet in DataFactory.DataSets)
                        {
                            yield return new TestCaseData(enc, level, ioChunk, dataSet).SetName($"{testName}{enc.Name} ({dataSet.Name}, размер {dataSet.Data.Length}, уровень сжатия: {level}, размер чанка: {ioChunk})");
                        }
                    }
                }
                else
                {
                    foreach (var dataSet in DataFactory.DataSets)
                    {
                        yield return new TestCaseData(enc, level, dataSet).SetName($"{testName}{enc.Name} ({dataSet.Name}, размер {dataSet.Data.Length}, уровень сжатия: {level})");
                    }
                }
            }
        }
    }
}