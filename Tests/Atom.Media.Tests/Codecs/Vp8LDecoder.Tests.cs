#pragma warning disable CA1861, MA0051

namespace Atom.Media.Tests;

/// <summary>
/// Тесты VP8L (WebP Lossless) декодера.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public sealed class Vp8LDecoderTests(ILogger logger) : BenchmarkTests<Vp8LDecoderTests>(logger)
{
    #region Constants

    /// <summary>Путь к VP8L тестовым файлам.</summary>
    private const string AssetsDir = "assets";

    #endregion

    #region Setup

    private WebpCodec? codec;

    public Vp8LDecoderTests() : this(ConsoleLogger.Unicode) { }

    [SetUp]
    public void SetUp() => codec = new WebpCodec();

    [TearDown]
    public void TearDown()
    {
        codec?.Dispose();
        codec = null;
    }

    #endregion

    #region Basic Functionality Tests

    [TestCase(TestName = "VP8L: декодирование 1x1 изображения")]
    public void Decode1x1ImageSuccess()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_1x1.webp"));

        // Получаем информацию об изображении
        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(1));
        Assert.That(info.Height, Is.EqualTo(1));
        Assert.That(info.IsLossless, Is.True);

        // Декодируем
        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        TestContext.Out.WriteLine($"VP8L 1x1: декодировано, alpha={info.HasAlpha}");
    }

    [TestCase(TestName = "VP8L: декодирование 4x4 изображения")]
    public void Decode4x4ImageSuccess()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_4x4.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(4));
        Assert.That(info.Height, Is.EqualTo(4));
        Assert.That(info.IsLossless, Is.True);

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        TestContext.Out.WriteLine($"VP8L 4x4: декодировано, lossless={info.IsLossless}");
    }

    [TestCase(TestName = "VP8L: декодирование 16x16 сплошного изображения")]
    public void Decode16x16SolidImageSuccess()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_16x16_solid.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(16));
        Assert.That(info.Height, Is.EqualTo(16));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Проверяем что все пиксели одинаковые (solid color)
        var pixelData = buffer.AsReadOnlyFrame().PackedData;
        var firstPixel = pixelData.GetRow(0)[..4].ToArray();
        for (var y = 0; y < info.Height; y++)
        {
            var row = pixelData.GetRow(y);
            for (var x = 0; x < info.Width; x++)
            {
                var pixel = row.Slice(x * 4, 4);
                Assert.That(pixel.SequenceEqual(firstPixel), Is.True,
                    $"Пиксель [{x},{y}] отличается от первого пикселя");
            }
        }

        TestContext.Out.WriteLine($"VP8L 16x16 solid: R={firstPixel[0]} G={firstPixel[1]} B={firstPixel[2]} A={firstPixel[3]}");
    }

    [TestCase(TestName = "VP8L: декодирование 8x8 градиентного изображения")]
    public void Decode8x8GradientImageSuccess()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_8x8_gradient.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(8));
        Assert.That(info.Height, Is.EqualTo(8));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        TestContext.Out.WriteLine($"VP8L 8x8 gradient: декодировано, alpha={info.HasAlpha}");
    }

    [TestCase(TestName = "VP8L: декодирование 32x32 plasma изображения")]
    public void Decode32x32PlasmaImageSuccess()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_32x32_plasma.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(32));
        Assert.That(info.Height, Is.EqualTo(32));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        TestContext.Out.WriteLine($"VP8L 32x32 plasma: декодировано, {info.Width}x{info.Height}");
    }

    [TestCase(TestName = "VP8L: декодирование в RGB24 формат")]
    public void DecodeToRgb24FormatSuccess()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_16x16_solid.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgb24));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgb24);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        TestContext.Out.WriteLine("VP8L → RGB24: декодировано");
    }

    #endregion

    #region Header Detection Tests

    [TestCase(TestName = "VP8L: GetInfo корректно парсит VP8L заголовок")]
    public void GetInfoParsesVp8LHeader()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_32x32_plasma.webp"));

        var result = codec!.GetInfo(data, out var info);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
        Assert.That(info.Width, Is.EqualTo(32));
        Assert.That(info.Height, Is.EqualTo(32));
        Assert.That(info.IsLossless, Is.True);
    }

    [TestCase(TestName = "VP8L: CanDecode определяет VP8L как WebP")]
    public void CanDecodeRecognizesVp8LAsWebp()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_1x1.webp"));

        var result = codec!.CanDecode(data);

        Assert.That(result, Is.True);
    }

    #endregion

    #region Edge Cases

    [TestCase(TestName = "VP8L: декодирование с неверными размерами frame возвращает InvalidData")]
    public void DecodeWithWrongFrameSizeReturnsInvalidData()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_4x4.webp"));

        // Инициализируем с неправильными размерами
        codec!.InitializeDecoder(new ImageCodecParameters(8, 8, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(8, 8, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "VP8L: декодирование пустых данных возвращает InvalidData")]
    public void DecodeEmptyDataReturnsInvalidData()
    {
        codec!.InitializeDecoder(new ImageCodecParameters(1, 1, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(1, 1, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode([], ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.InvalidData));
    }

    [TestCase(TestName = "VP8L: декодирование обрезанных данных возвращает InvalidData")]
    public void DecodeTruncatedDataReturnsInvalidData()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_32x32_plasma.webp"));
        // Обрезаем данные посередине
        var truncated = data[..(data.Length / 2)];

        // Пытаемся получить info для определения размеров
        var infoResult = codec!.GetInfo(data, out var info);
        if (infoResult != CodecResult.Success) return;

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(truncated, ref frame);

        // Ожидаем ошибку (InvalidData или другую)
        Assert.That(result, Is.Not.EqualTo(CodecResult.Success));
    }

    [TestCase(TestName = "VP8L: VP8 lossy файл декодируется через VP8 декодер")]
    public void DecodeVp8LossySucceeds()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test.webp"));

        // test.webp — VP8 lossy, теперь поддерживается через Vp8Decoder
        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);

        Assert.That(result, Is.EqualTo(CodecResult.Success));
    }

    #endregion

    #region Pixel Validation Tests

    [TestCase(TestName = "VP8L: RGBA32 значения пикселей в допустимых диапазонах")]
    public void DecodedPixelValuesInValidRange()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_8x8_gradient.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Проверяем что все байты не нулевые (gradient должен содержать данные)
        var pixelData = buffer.AsReadOnlyFrame().PackedData;
        var hasNonZero = false;
        for (var y = 0; y < info.Height && !hasNonZero; y++)
        {
            var row = pixelData.GetRow(y);
            for (var x = 0; x < row.Length; x++)
            {
                if (row[x] != 0)
                {
                    hasNonZero = true;
                    break;
                }
            }
        }

        Assert.That(hasNonZero, Is.True, "Декодированные пиксели не должны быть все нулевые");
    }

    [TestCase(TestName = "VP8L: декодирование 16x16 solid даёт красный цвет")]
    public void Decode16x16SolidIsRed()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_16x16_solid.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        codec.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));
        using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
        var frame = buffer.AsFrame();

        var result = codec.Decode(data, ref frame);
        Assert.That(result, Is.EqualTo(CodecResult.Success));

        // Проверяем первый пиксель — должен быть красный (RGBA)
        var row = buffer.AsReadOnlyFrame().PackedData.GetRow(0);
        var r = row[0];
        var g = row[1];
        var b = row[2];
        var a = row[3];

        TestContext.Out.WriteLine($"16x16 solid: R={r} G={g} B={b} A={a}");

        // Красный: R=255, G=0, B=0, A=255
        Assert.That(r, Is.EqualTo(255), "Red канал");
        Assert.That(g, Is.Zero, "Green канал");
        Assert.That(b, Is.Zero, "Blue канал");
        Assert.That(a, Is.EqualTo(255), "Alpha канал");
    }

    #endregion

    #region Stress Tests

    [TestCase(TestName = "VP8L: повторное декодирование одного файла не ломает состояние")]
    public void MultipleDecodesOfSameFileSucceed()
    {
        var data = File.ReadAllBytes(Path.Combine(AssetsDir, "test_vp8l_8x8_gradient.webp"));

        var infoResult = codec!.GetInfo(data, out var info);
        Assert.That(infoResult, Is.EqualTo(CodecResult.Success));

        for (var iteration = 0; iteration < 10; iteration++)
        {
            using var decoder = new WebpCodec();
            decoder.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));

            using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var frame = buffer.AsFrame();

            var result = decoder.Decode(data, ref frame);
            Assert.That(result, Is.EqualTo(CodecResult.Success), $"Итерация {iteration} не прошла");
        }

        TestContext.Out.WriteLine("VP8L: 10 повторных декодирований успешны");
    }

    [TestCase(TestName = "VP8L: декодирование всех тестовых VP8L файлов")]
    public void DecodeAllTestVp8LFilesSucceeds()
    {
        var files = Directory.GetFiles(AssetsDir, "test_vp8l_*.webp");
        Assert.That(files, Is.Not.Empty, "Тестовые VP8L файлы не найдены");

        foreach (var file in files)
        {
            var data = File.ReadAllBytes(file);
            var fileName = Path.GetFileName(file);

            using var decoder = new WebpCodec();
            var infoResult = decoder.GetInfo(data, out var info);
            Assert.That(infoResult, Is.EqualTo(CodecResult.Success), $"GetInfo не удался для {fileName}");

            decoder.InitializeDecoder(new ImageCodecParameters(info.Width, info.Height, VideoPixelFormat.Rgba32));

            using var buffer = new VideoFrameBuffer(info.Width, info.Height, VideoPixelFormat.Rgba32);
            var frame = buffer.AsFrame();

            var result = decoder.Decode(data, ref frame);
            Assert.That(result, Is.EqualTo(CodecResult.Success), $"Decode не удался для {fileName}");

            TestContext.Out.WriteLine($"  ✓ {fileName}: {info.Width}x{info.Height}, lossless={info.IsLossless}, alpha={info.HasAlpha}");
        }

        TestContext.Out.WriteLine($"Все {files.Length} VP8L файлов декодированы успешно");
    }

    #endregion
}
