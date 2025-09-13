using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.IO.Compression.Tests;

/// <summary>
/// Набор тестов для проверки корректности поведения ZstdStream на валидных данных.
/// </summary>
[TestFixture]
public class ZstdStreamTests(ILogger logger) : BenchmarkTests<ZstdStreamTests>(logger)
{
    private const int DataLength = 1048576;
    private const int CompressionLevel = 19;

    public override bool IsBenchmarkEnabled => default;

    private static IEnumerable<TestCaseData> CrossPairs
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var codecs = CodecFactory.Codecs;

            foreach (var enc in codecs)
            {
                foreach (var dec in codecs)
                {
                    yield return new TestCaseData(enc, dec).SetName($"Кросс-тест ({enc.Name}-{dec.Name})");
                }
            }
        }
    }

    private static IEnumerable<TestCaseData> TestData
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var codecs = CodecFactory.Codecs;

            foreach (var enc in codecs)
            {
                foreach (var level in CodecFactory.Levels)
                {
                    foreach (var ioChunk in CodecFactory.IoChunks)
                    {
                        foreach (var (Name, Data) in CodecFactory.DataSets)
                        {
                            yield return new TestCaseData(enc, level, ioChunk, Data).SetName($"{enc.Name} ({Name}, размер {Data.Length}, уровень сжатия: {level}, размер чанка: {ioChunk})");
                        }
                    }
                }
            }
        }
    }

    public ZstdStreamTests() : this(ConsoleLogger.Unicode) { }

    [TestCaseSource(nameof(TestData))]
    public void CompressionStreamTest(ICodec codec, int compressionLevel, int ioChunk, byte[] data)
    {
        using var src = new MemoryStream(data, writable: false);
        using var mid = new MemoryStream();
        codec.CompressStream(src, mid, compressionLevel, ioChunk);

        using var dst = new MemoryStream();

        mid.Position = 0;

        codec.DecompressStream(mid, dst, ioChunk);
        var plain = dst.ToArray();

        if (!IsBenchmarkEnabled) Assert.That(plain, Is.EqualTo(data));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StreamTest<T>(int dataLength = DataLength, int compressionLevel = CompressionLevel, int ioChunk = 4096) where T : ICodec, new()
    {
        var data = CodecFactory.GenerateText(dataLength);
        var codec = new T();

        using var src = new MemoryStream(data, writable: false);
        using var mid = new MemoryStream();
        codec.CompressStream(src, mid, compressionLevel, ioChunk);

        using var dst = new MemoryStream();

        mid.Position = 0;

        codec.DecompressStream(mid, dst, ioChunk);
        var plain = dst.ToArray();

        if (!IsBenchmarkEnabled) Assert.That(plain, Is.EqualTo(data));
    }

    [TestCase(TestName = "Atom"), Benchmark(Description = "Atom", Baseline = true)]
    public void AtomZstdStreamTest() => StreamTest<AtomZstdCodec>();

    [TestCase(TestName = "ZstdNet"), Benchmark(Description = "ZstdNet")]
    public void ZstdNetStreamTest() => StreamTest<ZstdNetCodec>();

    [TestCase(TestName = "ZstdSharp"), Benchmark(Description = "ZstdSharp")]
    public void ZstdSharpStreamTest() => StreamTest<ZstdSharpCodec>();

    [TestCaseSource(nameof(CrossPairs))]
    public void CrossStreamedRobustness(ICodec encoder, ICodec decoder)
    {
        foreach (var level in CodecFactory.Levels)
        {
            foreach (var (name, data) in CodecFactory.DataSets)
            {
                foreach (var ioEnc in CodecFactory.IoChunks)
                {
                    foreach (var ioDec in CodecFactory.IoChunks)
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

    [Test]
    public void ContentIntegrityHash()
    {
        // Дополнительная верификация: SHA256 исходника == SHA256(распакованного).
        var codec = new AtomZstdCodec();

        foreach (var level in CodecFactory.Levels)
        {
            foreach (var (name, data) in CodecFactory.DataSets)
            {
                var hashSrc = SHA256.HashData(data);
                var cmp = codec.Compress(data, level);
                var plain = codec.Decompress(cmp);
                var hashOut = SHA256.HashData(plain);

                Assert.That(hashOut, Is.EqualTo(hashSrc),
                    $"Integrity failed (SHA256 mismatch) on data={name}, level={level}");
            }
        }
    }

    [Test]
    public void SkippableBlocksTolerance()
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
}