#pragma warning disable CA1000, CA1707, CA1819, MA0048, NUnit2007, NUnit2045, S2368, S4144

using System.Diagnostics;

namespace Atom.Media.Codecs.Tests;

/// <summary>
/// Базовый абстрактный класс для тестов видеокодеков.
/// Поддерживает тестирование пар кодеков (WebM ↔ WebM, MP4 ↔ WebM и т.д.)
/// с различными разрешениями.
/// </summary>
/// <typeparam name="TEncoder">Тип кодека-энкодера.</typeparam>
/// <typeparam name="TDecoder">Тип кодека-декодера.</typeparam>
[TestFixture]
public abstract class VideoCodecTestBase<TEncoder, TDecoder>
    where TEncoder : class, IVideoCodec, new()
    where TDecoder : class, IVideoCodec, new()
{
    #region Constants

    /// <summary>Количество прогревов перед замером производительности.</summary>
    protected const int WarmupIterations = 3;

    /// <summary>Количество итераций для замера производительности.</summary>
    protected const int BenchmarkIterations = 20;

    #endregion

    #region Abstract Properties

    /// <summary>Имя пары конвертации (например, "WebM ↔ MP4").</summary>
    protected abstract string PairName { get; }

    /// <summary>Расширение файла для энкодера (например, ".webm").</summary>
    protected abstract string EncoderExtension { get; }

    /// <summary>Расширение файла для декодера (например, ".mp4").</summary>
    protected abstract string DecoderExtension { get; }

    /// <summary>Формат пикселей для тестов.</summary>
    protected virtual VideoPixelFormat TestPixelFormat => VideoPixelFormat.Rgba32;

    /// <summary>Ожидаемая точность round-trip (0 = lossless).</summary>
    protected virtual int RoundTripTolerance => 0;

    /// <summary>Папка для сохранения результатов конвертации.</summary>
    protected const string OutputFolder = "output";

    /// <summary>Сохранять результаты конвертации для визуального анализа.</summary>
    protected virtual bool SaveOutputForVisualInspection => true;

    #endregion

    #region Fields

    private string outputDirectory = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public virtual void OneTimeSetUp()
    {
        // Создаём директорию для выходных файлов
        // PairName используется напрямую как имя папки
        outputDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, OutputFolder, PairName);
        if (SaveOutputForVisualInspection)
        {
            Directory.CreateDirectory(outputDirectory);
            TestContext.Out.WriteLine($"Выходная директория: {outputDirectory}");
        }
    }

    #endregion

    #region Resolution Presets

    /// <summary>Разрешения для тестов.</summary>
    protected static readonly (string Name, int Width, int Height)[] Resolutions =
    [
        ("480p", 640, 480),
        ("720p", 1280, 720),
        ("1080p", 1920, 1080),
        ("4K", 3840, 2160),
    ];

    #endregion

    #region Test: Round-Trip Correctness

    /// <summary>
    /// Тест: Round-trip кодирование/декодирование RGBA32.
    /// </summary>
    [Test, Order(1)]
    [TestCase(64, 64, TestName = "Round-trip RGBA32 64x64")]
    [TestCase(128, 128, TestName = "Round-trip RGBA32 128x128")]
    [TestCase(320, 240, TestName = "Round-trip RGBA32 320x240")]
    public void RoundTripRgba32(int width, int height)
    {
        using var encoder = new TEncoder();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decoder = new TDecoder();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(originalBuffer, 0);

        // Оценка размера буфера
        var estimatedSize = EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[estimatedSize];

        // Encode
        var roFrame = originalBuffer.AsReadOnlyFrame();
        var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success), "Encode failed");
        Assert.That(bytesWritten, Is.GreaterThan(0), "No bytes written");

        // Decode
        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var decodedFrame = decodedBuffer.AsFrame();
        var decodeResult = decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success), "Decode failed");

        // Compare
        var originalData = originalBuffer.AsReadOnlyFrame().PackedData.Data;
        var decodedData = decodedBuffer.AsReadOnlyFrame().PackedData.Data;

        if (RoundTripTolerance == 0)
        {
            Assert.That(decodedData.SequenceEqual(originalData), Is.True,
                "Decoded data should match original (lossless)");
        }
        else
        {
            var maxDiff = CompareWithTolerance(originalData, decodedData);
            Assert.That(maxDiff, Is.LessThanOrEqualTo(RoundTripTolerance),
                $"Max pixel difference {maxDiff} exceeds tolerance {RoundTripTolerance}");
        }

        // Сохраняем закодированные данные в формате энкодера
        if (SaveOutputForVisualInspection)
        {
            SaveEncodedData(encoded.AsSpan(0, bytesWritten), $"rgba32_{width}x{height}");
        }

        TestContext.Out.WriteLine($"{PairName} round-trip {width}x{height}: {bytesWritten} bytes");
    }

    /// <summary>
    /// Тест: Round-trip кодирование/декодирование YUV420P.
    /// </summary>
    [Test, Order(2)]
    [TestCase(64, 64, TestName = "Round-trip YUV420P 64x64")]
    [TestCase(128, 128, TestName = "Round-trip YUV420P 128x128")]
    [TestCase(320, 240, TestName = "Round-trip YUV420P 320x240")]
    public void RoundTripYuv420P(int width, int height)
    {
        using var encoder = new TEncoder();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Yuv420P
        });

        using var decoder = new TDecoder();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Yuv420P
        });

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Yuv420P);
        FillTestPatternYuv420P(originalBuffer, 0);

        var estimatedSize = EstimateEncodedSize(width, height, VideoPixelFormat.Yuv420P);
        var encoded = new byte[estimatedSize];

        // Encode
        var roFrame = originalBuffer.AsReadOnlyFrame();
        var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
        Assert.That(encodeResult, Is.EqualTo(CodecResult.Success), "Encode failed");

        // Decode
        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Yuv420P);
        var decodedFrame = decodedBuffer.AsFrame();
        var decodeResult = decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
        Assert.That(decodeResult, Is.EqualTo(CodecResult.Success), "Decode failed");

        // Compare planes
        CompareYuvPlanes(originalBuffer, decodedBuffer, width, height);

        // YUV420P не сохраняем как PNG (нужна конвертация в RGB)

        TestContext.Out.WriteLine($"{PairName} YUV420P round-trip {width}x{height}: {bytesWritten} bytes");
    }

    #endregion

    #region Test: Multi-Frame Sequence

    /// <summary>
    /// Тест: Последовательность из 50 кадров.
    /// </summary>
    [Test, Order(3)]
    [TestCase(TestName = "Multi-frame sequence 50 frames")]
    public void MultiFrameSequence()
    {
        const int width = 320;
        const int height = 240;
        const int frameCount = 50;

        using var encoder = new TEncoder();
        encoder.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decoder = new TDecoder();
        decoder.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);

        var estimatedSize = EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var encoded = new byte[estimatedSize];

        var sw = Stopwatch.StartNew();

        for (var i = 0; i < frameCount; i++)
        {
            FillTestPatternRgba32(frameBuffer, i);

            var roFrame = frameBuffer.AsReadOnlyFrame();
            var encodeResult = encoder.Encode(roFrame, encoded, out var bytesWritten);
            Assert.That(encodeResult, Is.EqualTo(CodecResult.Success), $"Encode frame {i} failed");

            var decodedFrame = decodedBuffer.AsFrame();
            var decodeResult = decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
            Assert.That(decodeResult, Is.EqualTo(CodecResult.Success), $"Decode frame {i} failed");

            // Verify data
            var originalData = frameBuffer.AsReadOnlyFrame().PackedData.Data;
            var decodedData = decodedBuffer.AsReadOnlyFrame().PackedData.Data;
            Assert.That(decodedData.SequenceEqual(originalData), Is.True, $"Frame {i} data mismatch");
        }

        sw.Stop();
        var fps = frameCount * 1000.0 / sw.Elapsed.TotalMilliseconds;

        TestContext.Out.WriteLine($"{PairName} {frameCount} frames 320x240: {sw.Elapsed.TotalMilliseconds:F1} ms, {fps:F1} FPS");
    }

    #endregion

    #region Test: Performance Benchmark

    /// <summary>
    /// Тест: Производительность по разрешениям.
    /// </summary>
    [Test, Order(4)]
    [TestCase(TestName = "Performance benchmark multi-resolution")]
    public void PerformanceBenchmark()
    {
        TestContext.Out.WriteLine($"\n{PairName} Performance Benchmark:");
        TestContext.Out.WriteLine("┌───────────┬────────────────┬────────────────┬───────────────┐");
        TestContext.Out.WriteLine("│ Разреш.   │   Encode (мс)  │  Decode (мс)   │     FPS       │");
        TestContext.Out.WriteLine("├───────────┼────────────────┼────────────────┼───────────────┤");

        foreach (var (name, width, height) in Resolutions)
        {
            using var encoder = new TEncoder();
            encoder.InitializeEncoder(new VideoCodecParameters
            {
                Width = width,
                Height = height,
                PixelFormat = VideoPixelFormat.Rgba32
            });

            using var decoder = new TDecoder();
            decoder.InitializeDecoder(new VideoCodecParameters
            {
                Width = width,
                Height = height,
                PixelFormat = VideoPixelFormat.Rgba32
            });

            using var frameBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            FillTestPatternRgba32(frameBuffer, 0);
            var roFrame = frameBuffer.AsReadOnlyFrame();

            var estimatedSize = EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
            var encoded = new byte[estimatedSize];

            using var decodedBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
            var decodedFrame = decodedBuffer.AsFrame();

            // Warmup
            for (var i = 0; i < WarmupIterations; i++)
            {
                encoder.Encode(roFrame, encoded, out var bw);
                decoder.Decode(encoded.AsSpan(0, bw), ref decodedFrame);
            }

            // Measure Encode
            var swEncode = Stopwatch.StartNew();
            var bytesWritten = 0;
            for (var i = 0; i < BenchmarkIterations; i++)
            {
                encoder.Encode(roFrame, encoded, out bytesWritten);
            }
            swEncode.Stop();
            var encodeMs = swEncode.Elapsed.TotalMilliseconds / BenchmarkIterations;

            // Measure Decode
            var swDecode = Stopwatch.StartNew();
            for (var i = 0; i < BenchmarkIterations; i++)
            {
                decoder.Decode(encoded.AsSpan(0, bytesWritten), ref decodedFrame);
            }
            swDecode.Stop();
            var decodeMs = swDecode.Elapsed.TotalMilliseconds / BenchmarkIterations;

            var totalMs = encodeMs + decodeMs;
            var fps = 1000.0 / totalMs;

            TestContext.Out.WriteLine(
                $"│ {name,-9} │ {encodeMs,14:F2} │ {decodeMs,14:F2} │ {fps,13:F1} │");
        }

        TestContext.Out.WriteLine("└───────────┴────────────────┴────────────────┴───────────────┘");
    }

    #endregion

    #region Test: Cross-Conversion

    /// <summary>
    /// Тест: Двойная конвертация (A → B → A).
    /// </summary>
    [Test, Order(5)]
    [TestCase(TestName = "Double conversion round-trip")]
    public void DoubleConversionRoundTrip()
    {
        const int width = 128;
        const int height = 128;

        using var originalBuffer = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        FillTestPatternRgba32(originalBuffer, 42);

        var estimatedSize = EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);

        // First encode (TEncoder)
        using var encoder1 = new TEncoder();
        encoder1.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        var encoded1 = new byte[estimatedSize];
        encoder1.Encode(originalBuffer.AsReadOnlyFrame(), encoded1, out var size1);

        // Decode (TDecoder)
        using var decoder1 = new TDecoder();
        decoder1.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decodedBuffer1 = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame1 = decodedBuffer1.AsFrame();
        decoder1.Decode(encoded1.AsSpan(0, size1), ref frame1);

        // Second encode (TEncoder again)
        using var encoder2 = new TEncoder();
        encoder2.InitializeEncoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        var encoded2 = new byte[estimatedSize];
        encoder2.Encode(decodedBuffer1.AsReadOnlyFrame(), encoded2, out var size2);

        // Final decode
        using var decoder2 = new TDecoder();
        decoder2.InitializeDecoder(new VideoCodecParameters
        {
            Width = width,
            Height = height,
            PixelFormat = VideoPixelFormat.Rgba32
        });

        using var decodedBuffer2 = new VideoFrameBuffer(width, height, VideoPixelFormat.Rgba32);
        var frame2 = decodedBuffer2.AsFrame();
        decoder2.Decode(encoded2.AsSpan(0, size2), ref frame2);

        // Compare
        var original = originalBuffer.AsReadOnlyFrame().PackedData.Data;
        var final = decodedBuffer2.AsReadOnlyFrame().PackedData.Data;

        Assert.That(final.SequenceEqual(original), Is.True,
            "Double conversion should preserve data (lossless)");

        TestContext.Out.WriteLine($"{PairName} double conversion: {size1} → {size2} bytes");
    }

    #endregion

    #region Helper Methods

    /// <summary>Оценивает размер закодированных данных.</summary>
    protected virtual int EstimateEncodedSize(int width, int height, VideoPixelFormat format)
    {
        // AFRM header (44 bytes) + raw pixel data
        var pixelSize = format switch
        {
            VideoPixelFormat.Rgba32 or VideoPixelFormat.Bgra32 => width * height * 4,
            VideoPixelFormat.Rgb24 or VideoPixelFormat.Bgr24 => width * height * 3,
            VideoPixelFormat.Yuv420P => (width * height) + (width / 2 * (height / 2) * 2),
            VideoPixelFormat.Yuv422P => (width * height) + (width / 2 * height * 2),
            VideoPixelFormat.Yuv444P => width * height * 3,
            _ => width * height * 4
        };

        return 44 + pixelSize + 1024; // Header + data + margin
    }

    /// <summary>Заполняет буфер тестовым паттерном RGBA32.</summary>
    protected static void FillTestPatternRgba32(VideoFrameBuffer buffer, int seed)
    {
        var frame = buffer.AsFrame();
        var plane = frame.PackedData;
        var width = frame.Width;
        var height = frame.Height;

        for (var y = 0; y < height; y++)
        {
            var row = plane.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                var offset = x * 4;
                row[offset] = (byte)((x + seed) % 256);     // R
                row[offset + 1] = (byte)((y + seed) % 256); // G
                row[offset + 2] = (byte)((x + y + seed) % 256); // B
                row[offset + 3] = 255; // A
            }
        }
    }

    /// <summary>Заполняет буфер тестовым паттерном YUV420P.</summary>
    protected static void FillTestPatternYuv420P(VideoFrameBuffer buffer, int seed)
    {
        var frame = buffer.AsFrame();
        var width = frame.Width;
        var height = frame.Height;

        // Y plane
        var yPlane = frame.GetPlaneY();
        for (var y = 0; y < height; y++)
        {
            var row = yPlane.GetRow(y);
            for (var x = 0; x < width; x++)
            {
                row[x] = (byte)((x + y + seed) % 256);
            }
        }

        // U plane
        var uPlane = frame.GetPlaneU();
        var uvWidth = width / 2;
        var uvHeight = height / 2;
        for (var y = 0; y < uvHeight; y++)
        {
            var row = uPlane.GetRow(y);
            for (var x = 0; x < uvWidth; x++)
            {
                row[x] = (byte)(128 + ((x + seed) % 64));
            }
        }

        // V plane
        var vPlane = frame.GetPlaneV();
        for (var y = 0; y < uvHeight; y++)
        {
            var row = vPlane.GetRow(y);
            for (var x = 0; x < uvWidth; x++)
            {
                row[x] = (byte)(128 + ((y + seed) % 64));
            }
        }
    }

    /// <summary>Сравнивает YUV плоскости.</summary>
    protected void CompareYuvPlanes(VideoFrameBuffer original, VideoFrameBuffer decoded, int width, int height)
    {
        var origFrame = original.AsReadOnlyFrame();
        var decFrame = decoded.AsReadOnlyFrame();

        // Y plane
        var origY = origFrame.GetPlaneY();
        var decY = decFrame.GetPlaneY();
        for (var y = 0; y < height; y++)
        {
            Assert.That(decY.GetRow(y).SequenceEqual(origY.GetRow(y)), Is.True, $"Y plane row {y} mismatch");
        }

        // U plane
        var origU = origFrame.GetPlaneU();
        var decU = decFrame.GetPlaneU();
        var uvHeight = height / 2;
        for (var y = 0; y < uvHeight; y++)
        {
            Assert.That(decU.GetRow(y).SequenceEqual(origU.GetRow(y)), Is.True, $"U plane row {y} mismatch");
        }

        // V plane
        var origV = origFrame.GetPlaneV();
        var decV = decFrame.GetPlaneV();
        for (var y = 0; y < uvHeight; y++)
        {
            Assert.That(decV.GetRow(y).SequenceEqual(origV.GetRow(y)), Is.True, $"V plane row {y} mismatch");
        }
    }

    /// <summary>Сравнивает данные с допуском.</summary>
    protected static int CompareWithTolerance(ReadOnlySpan<byte> original, ReadOnlySpan<byte> decoded)
    {
        var maxDiff = 0;
        for (var i = 0; i < original.Length && i < decoded.Length; i++)
        {
            var diff = Math.Abs(original[i] - decoded[i]);
            if (diff > maxDiff) maxDiff = diff;
        }
        return maxDiff;
    }

    /// <summary>Сохраняет закодированные данные в файл с расширением энкодера.</summary>
    protected void SaveEncodedData(ReadOnlySpan<byte> data, string baseName)
    {
        if (!SaveOutputForVisualInspection) return;

        var fileName = $"{baseName}{EncoderExtension}";
        var filePath = Path.Combine(outputDirectory, fileName);
        File.WriteAllBytes(filePath, data.ToArray());
        TestContext.Out.WriteLine($"  Сохранено: {filePath}");
    }

    /// <summary>Сохраняет RGBA32 кадр как PNG для визуальной проверки.</summary>
    protected void SaveFrameAsPng(VideoFrameBuffer buffer, string baseName)
    {
        if (!SaveOutputForVisualInspection) return;

        var frame = buffer.AsReadOnlyFrame();
        var width = frame.Width;
        var height = frame.Height;

        // Кодируем в PNG
        using var pngCodec = new PngCodec();
        pngCodec.InitializeEncoder(new ImageCodecParameters(width, height, VideoPixelFormat.Rgba32));

        var estimatedSize = pngCodec.EstimateEncodedSize(width, height, VideoPixelFormat.Rgba32);
        var pngData = new byte[estimatedSize];

        var encodeResult = pngCodec.Encode(frame, pngData, out var bytesWritten);
        if (encodeResult != CodecResult.Success)
        {
            TestContext.Out.WriteLine($"  Ошибка сохранения PNG: {encodeResult}");
            return;
        }

        var fileName = $"{baseName}.png";
        var filePath = Path.Combine(outputDirectory, fileName);
        File.WriteAllBytes(filePath, pngData.AsSpan(0, bytesWritten).ToArray());
        TestContext.Out.WriteLine($"  Сохранено: {filePath}");
    }

    #endregion
}
