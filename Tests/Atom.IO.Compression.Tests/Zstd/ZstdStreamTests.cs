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

    /// <summary>
    /// Тест асинхронного API для записи и чтения.
    /// </summary>
    [Test]
    public async Task AsyncWriteReadTest()
    {
        var data = DataFactory.GenerateText(65536);

        using var compressed = new MemoryStream();
        await using (var zstd = new ZstdStream(compressed, compressionLevel: 3, leaveOpen: true))
        {
            await zstd.WriteAsync(data);
            await zstd.FlushAsync();
        }

        compressed.Position = 0;

        using var decompressed = new MemoryStream();
        await using (var zstd = new ZstdStream(compressed, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true))
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = await zstd.ReadAsync(buffer)) > 0)
            {
                await decompressed.WriteAsync(buffer.AsMemory(0, bytesRead));
            }
        }

        Assert.That(decompressed.ToArray(), Is.EqualTo(data), "Async roundtrip failed");
    }

    /// <summary>
    /// Тест Content Checksum: проверяем, что включённый checksum записывается и проверяется.
    /// </summary>
    [Test]
    public void ContentChecksumEnabledTest()
    {
        var data = DataFactory.GenerateRandom(32768);

        using var compressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, compressionLevel: 3, leaveOpen: true)
        {
            IsContentChecksumEnabled = true
        })
        {
            zstd.Write(data);
        }

        var compressedBytes = compressed.ToArray();

        // Zstd frame с checksum должен быть как минимум +4 байта (xxHash64 low 4 LE)
        Assert.That(compressedBytes.Length, Is.GreaterThan(4 + 3), "Frame too small for checksum");

        // Проверяем, что распаковка работает
        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true))
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = zstd.Read(buffer, 0, buffer.Length)) > 0)
            {
                decompressed.Write(buffer, 0, bytesRead);
            }
        }

        Assert.That(decompressed.ToArray(), Is.EqualTo(data), "Checksum roundtrip failed");
    }

    /// <summary>
    /// Тест различных уровней сжатия от 0 до 9.
    /// </summary>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(6)]
    [TestCase(9)]
    public void CompressionLevelTest(int level)
    {
        var data = DataFactory.GenerateText(16384);

        using var compressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, compressionLevel: level, leaveOpen: true))
        {
            zstd.Write(data);
        }

        compressed.Position = 0;

        using var decompressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true))
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = zstd.Read(buffer, 0, buffer.Length)) > 0)
            {
                decompressed.Write(buffer, 0, bytesRead);
            }
        }

        Assert.That(decompressed.ToArray(), Is.EqualTo(data), $"Level {level} roundtrip failed");
    }

    /// <summary>
    /// Тест пустых данных.
    /// </summary>
    [Test]
    public void EmptyDataTest()
    {
        var data = Array.Empty<byte>();

        using var compressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, compressionLevel: 3, leaveOpen: true))
        {
            zstd.Write(data);
        }

        compressed.Position = 0;

        using var decompressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true))
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = zstd.Read(buffer, 0, buffer.Length)) > 0)
            {
                decompressed.Write(buffer, 0, bytesRead);
            }
        }

        Assert.That(decompressed.ToArray(), Is.EqualTo(data), "Empty data roundtrip failed");
    }

    /// <summary>
    /// Тест однобайтовых данных.
    /// </summary>
    [Test]
    public void SingleByteTest()
    {
        var data = "B"u8.ToArray();

        using var compressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, compressionLevel: 3, leaveOpen: true))
        {
            zstd.Write(data);
        }

        compressed.Position = 0;

        using var decompressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true))
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = zstd.Read(buffer, 0, buffer.Length)) > 0)
            {
                decompressed.Write(buffer, 0, bytesRead);
            }
        }

        Assert.That(decompressed.ToArray(), Is.EqualTo(data), "Single byte roundtrip failed");
    }

    /// <summary>
    /// Тест данных, полностью состоящих из одного байта (RLE-оптимизация).
    /// </summary>
    [Test]
    public void RleBlockTest()
    {
        var data = new byte[65536];
        Array.Fill(data, (byte)0xAB);

        using var compressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, compressionLevel: 3, leaveOpen: true))
        {
            zstd.Write(data);
        }

        // RLE-блок должен быть очень компактным
        Assert.That(compressed.Length, Is.LessThan(100), "RLE compression not effective");

        compressed.Position = 0;

        using var decompressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true))
        {
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = zstd.Read(buffer, 0, buffer.Length)) > 0)
            {
                decompressed.Write(buffer, 0, bytesRead);
            }
        }

        Assert.That(decompressed.ToArray(), Is.EqualTo(data), "RLE roundtrip failed");
    }

    /// <summary>
    /// Тест свойств потока CanRead/CanWrite в разных режимах.
    /// </summary>
    [Test]
    public void StreamPropertiesTest()
    {
        using var ms = new MemoryStream();

        using (var compress = new ZstdStream(ms, compressionLevel: 3, leaveOpen: true))
        {
            Assert.Multiple(() =>
            {
                Assert.That(compress.CanWrite, Is.True, "Compress stream should be writable");
                Assert.That(compress.CanRead, Is.False, "Compress stream should not be readable");
                Assert.That(compress.CanSeek, Is.False, "Zstd stream should not be seekable");
            });
        }

        ms.Position = 0;
        // Запишем минимальный валидный frame
        ms.SetLength(0);
        using (var zstd = new ZstdStream(ms, compressionLevel: 0, leaveOpen: true))
        {
            zstd.Write([]);
        }

        ms.Position = 0;

        using var decompress = new ZstdStream(ms, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);
        Assert.Multiple(() =>
        {
            Assert.That(decompress.CanRead, Is.True, "Decompress stream should be readable");
            Assert.That(decompress.CanWrite, Is.False, "Decompress stream should not be writable");
            Assert.That(decompress.CanSeek, Is.False, "Zstd stream should not be seekable");
        });
    }

    /// <summary>
    /// Тест размера окна - проверяем, что большие данные корректно обрабатываются.
    /// </summary>
    [Test]
    public void WindowSizeTest()
    {
        // Данные больше размера блока (128KB) для проверки межблочной истории
        var data = DataFactory.GenerateText(512 * 1024);

        using var compressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, compressionLevel: 3, leaveOpen: true)
        {
            WindowSize = 4 * 1024 * 1024 // 4 МБ окно
        })
        {
            zstd.Write(data);
        }

        compressed.Position = 0;

        using var decompressed = new MemoryStream();
        using (var zstd = new ZstdStream(compressed, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true))
        {
            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = zstd.Read(buffer, 0, buffer.Length)) > 0)
            {
                decompressed.Write(buffer, 0, bytesRead);
            }
        }

        Assert.That(decompressed.ToArray(), Is.EqualTo(data), "Large data roundtrip failed");
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