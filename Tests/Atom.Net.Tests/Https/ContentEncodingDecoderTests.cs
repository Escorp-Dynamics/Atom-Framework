using System.IO;
using System.IO.Compression;
using System.Text;
using Atom.Net.Https.Headers;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет распаковку тела ответа по заголовку <c>content-encoding</c>.
/// </summary>
/// <remarks>
/// Отсутствие этой распаковки было настоящей дырой, и незаметной: на проводе всё выглядело
/// безупречно — браузер объявляет <c>accept-encoding: gzip, deflate, br, zstd</c>, и мы объявляли
/// то же самое, — но сжатый ответ уходил вызывающей стороне как есть. Обнаруживалось это только на
/// серверах, которые действительно сжимают: <c>www.cloudflare.com</c> отдавал тело, начинающееся
/// с <c>1F 8B</c>, а проверки отпечатка на <c>tls.peet.ws</c> проходили, потому что он не сжимает.
///
/// Поэтому здесь закреплены не только удачные случаи, но и все способы НЕ испортить тело:
/// незнакомая кодировка, повреждённые данные, обе формы <c>deflate</c>.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class ContentEncodingDecoderTests
{
    private const int TestTimeoutMs = 30000;

    private const string Sample = "<!doctype html><html><head><title>проверка</title></head><body>содержимое ответа</body></html>";

    [Test]
    public void GzipIsDecoded()
    {
        var compressed = Compress(Sample, static stream => new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true));

        Assert.That(ContentEncodingDecoder.TryDecode(compressed, "gzip", out var decoded), Is.True);
        Assert.That(Encoding.UTF8.GetString(decoded), Is.EqualTo(Sample));
    }

    [Test]
    public void BrotliIsDecoded()
    {
        var compressed = Compress(Sample, static stream => new BrotliStream(stream, CompressionLevel.Optimal, leaveOpen: true));

        Assert.That(ContentEncodingDecoder.TryDecode(compressed, "br", out var decoded), Is.True);
        Assert.That(Encoding.UTF8.GetString(decoded), Is.EqualTo(Sample));
    }

    [Test]
    public void DeflateWithZLibWrapperIsDecoded()
    {
        // Форма из RFC 9110: deflate внутри обёртки zlib.
        var compressed = Compress(Sample, static stream => new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true));

        Assert.That(ContentEncodingDecoder.TryDecode(compressed, "deflate", out var decoded), Is.True);
        Assert.That(Encoding.UTF8.GetString(decoded), Is.EqualTo(Sample));
    }

    [Test]
    public void RawDeflateIsDecodedToo()
    {
        // Форма, которую спецификация не разрешает, но которую отдаёт заметная часть серверов.
        // Браузеры принимают обе, иначе часть сети была бы для них недоступна.
        var compressed = Compress(Sample, static stream => new DeflateStream(stream, CompressionLevel.Optimal, leaveOpen: true));

        Assert.That(ContentEncodingDecoder.TryDecode(compressed, "deflate", out var decoded), Is.True);
        Assert.That(Encoding.UTF8.GetString(decoded), Is.EqualTo(Sample));
    }

    [Test]
    public void ZstdIsDecoded()
    {
        var compressed = Compress(Sample, static stream => new Atom.IO.Compression.ZstdStream(stream, CompressionMode.Compress, leaveOpen: true));

        Assert.That(ContentEncodingDecoder.TryDecode(compressed, "zstd", out var decoded), Is.True);
        Assert.That(Encoding.UTF8.GetString(decoded), Is.EqualTo(Sample));
    }

    [Test]
    public void ChainedEncodingsAreUnwrappedInReverseOrder()
    {
        // Заголовок перечисляет кодировки в порядке ПРИМЕНЕНИЯ, поэтому снимать их надо с конца.
        // Снятие в прямом порядке дало бы мусор, причём правдоподобного размера.
        var once = Compress(Sample, static stream => new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true));
        var twice = Compress(once, static stream => new BrotliStream(stream, CompressionLevel.Optimal, leaveOpen: true));

        Assert.That(ContentEncodingDecoder.TryDecode(twice, "gzip, br", out var decoded), Is.True);
        Assert.That(Encoding.UTF8.GetString(decoded), Is.EqualTo(Sample));
    }

    [Test]
    public void UnknownEncodingLeavesBodyUntouched()
    {
        // Испорченный результат хуже нераспакованного: вызывающая сторона хотя бы увидит, что
        // тело сжато, вместо того чтобы получить обрезанный мусор.
        var body = Encoding.UTF8.GetBytes(Sample);

        Assert.That(ContentEncodingDecoder.TryDecode(body, "exotic-codec", out var decoded), Is.False);
        Assert.That(decoded, Is.SameAs(body));
    }

    [Test]
    public void CorruptedBodyFailsLoudlyInsteadOfBeingPassedThrough()
    {
        // ★ Прежде такое тело возвращалось КАК ЕСТЬ, и это было ошибкой: вызывающая сторона
        // получала двоичный мусор под видом ответа — с кодом 200 и заголовком content-encoding,
        // то есть без единого признака беды. Замер на двух тысячах живых узлов показал, что так
        // молча портились ответы 77 сайтов, включая Facebook, Instagram и Box.
        //
        // Молчаливая порча хуже явного отказа: по отказу видно, что чинить, а мусор расходится
        // дальше по обработке и всплывает где угодно.
        var body = Encoding.UTF8.GetBytes("это совсем не gzip");

        var error = Assert.Throws<InvalidOperationException>(() => ContentEncodingDecoder.TryDecode(body, "gzip", out _));

        Assert.That(error.Message, Does.Contain("gzip"), "в сообщении обязана быть названа кодировка");
    }

    [Test]
    public void IdentityAndEmptyValuesChangeNothing()
    {
        var body = Encoding.UTF8.GetBytes(Sample);

        Assert.Multiple(() =>
        {
            Assert.That(ContentEncodingDecoder.TryDecode(body, "identity", out _), Is.True, "identity — это тоже разобранная кодировка");
            Assert.That(ContentEncodingDecoder.TryDecode(body, null, out var noHeader), Is.False);
            Assert.That(noHeader, Is.SameAs(body));
            Assert.That(ContentEncodingDecoder.TryDecode([], "gzip", out var empty), Is.False);
            Assert.That(empty, Is.Empty);
        });
    }

    [Test]
    public void EncodingNameIsCaseInsensitiveAndTrimmed()
    {
        var compressed = Compress(Sample, static stream => new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true));

        Assert.That(ContentEncodingDecoder.TryDecode(compressed, "  GZIP  ", out var decoded), Is.True);
        Assert.That(Encoding.UTF8.GetString(decoded), Is.EqualTo(Sample));
    }

    [TestCase("gzip", true)]
    [TestCase("br", true)]
    [TestCase("deflate", true)]
    [TestCase("zstd", true)]
    [TestCase("compress", false)]
    [TestCase(null, false)]
    public void SupportMatchesWhatWeAdvertise(string? encoding, bool expected)
    {
        // Объявлять в accept-encoding то, чего не умеем распаковывать, — ровно та ошибка, из-за
        // которой сжатые ответы уходили вызывающей стороне сырыми.
        Assert.That(ContentEncodingDecoder.IsSupported(encoding), Is.EqualTo(expected));
    }

    private static byte[] Compress(string text, Func<Stream, Stream> createEncoder)
        => Compress(Encoding.UTF8.GetBytes(text), createEncoder);

    private static byte[] Compress(byte[] payload, Func<Stream, Stream> createEncoder)
    {
        using var output = new MemoryStream();

        using (var encoder = createEncoder(output)) encoder.Write(payload, 0, payload.Length);

        return output.ToArray();
    }
}
