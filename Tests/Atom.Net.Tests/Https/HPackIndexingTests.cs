using System.Buffers;
using System.Linq;
using Atom.Net.Https.Headers;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет, что кодировщик HPACK пользуется динамической таблицей.
/// </summary>
/// <remarks>
/// ★ Прежде он не индексировал НИЧЕГО: для всех заголовков возвращался режим «без
/// индексирования», и таблица не заполнялась ни разу за всё соединение. Каждый запрос отправлял
/// свои заголовки целиком, тогда как браузер со второго запроса ссылается на записи одним байтом.
///
/// Наблюдаемо это напрямую: сервер видит клиента, который за всё соединение не проиндексировал ни
/// одной записи — сжатие заголовков у него как бы включено, а пользы от него никакой. Ни один
/// браузер так себя не ведёт, потому что ровно ради этого динамическая таблица и существует.
///
/// Вторая половина проверок — про безопасность такой экономии: заголовки с тайной индексировать
/// нельзя (RFC 7541 §7.1.3), а таблица не вправе быть больше объявленной ПАРТНЁРОМ.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class HPackIndexingTests
{
    private const int TestTimeoutMs = 30000;

    private static readonly KeyValuePair<string, string>[] BrowserHeaders =
    [
        new(":method", "GET"),
        new(":authority", "example.com"),
        new(":scheme", "https"),
        new(":path", "/page"),
        new("sec-ch-ua", "\"Not_A Brand\";v=\"24\", \"Chromium\";v=\"131\", \"Google Chrome\";v=\"131\""),
        new("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"),
        new("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8"),
        new("accept-language", "en-US,en;q=0.9"),
    ];

    [Test]
    public void RepeatedHeadersShrinkOnTheSecondRequest()
    {
        var encoder = new HPackEncoder(4096);

        var first = Encode(encoder, BrowserHeaders);
        var second = Encode(encoder, BrowserHeaders);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.LessThan(first / 2), $"второй блок обязан ссылаться на таблицу: было {first}, стало {second}");
            Assert.That(encoder.DynamicTableCount, Is.GreaterThan(0), "таблица должна заполниться");
        });
    }

    [Test]
    public void SensitiveHeadersNeverEnterTheTable()
    {
        // RFC 7541 §7.1.3: запрет относится не только к нам, но и к промежуточным узлам —
        // значение не должно осесть в таблице, живущей дольше запроса.
        var encoder = new HPackEncoder(4096);

        KeyValuePair<string, string>[] headers =
        [
            new("cookie", "session=секрет"),
            new("authorization", "Bearer секрет"),
        ];

        var first = Encode(encoder, headers);
        var second = Encode(encoder, headers);

        Assert.Multiple(() =>
        {
            Assert.That(encoder.DynamicTableCount, Is.Zero, "заголовки с тайной в таблицу попадать не должны");
            Assert.That(second, Is.EqualTo(first), "и на повторе они кодируются целиком");
        });
    }

    [Test]
    public void PseudoHeadersAreNotIndexed()
    {
        // :path у каждого запроса свой, и индексировать его значит вытеснять из таблицы то, что
        // действительно повторяется.
        var encoder = new HPackEncoder(4096);

        _ = Encode(encoder, [new KeyValuePair<string, string>(":path", "/first")]);
        _ = Encode(encoder, [new KeyValuePair<string, string>(":path", "/second")]);

        Assert.That(encoder.DynamicTableCount, Is.Zero);
    }

    [Test]
    public void TableNeverExceedsWhatThePeerAllows()
    {
        // ★ Размер таблицы кодировщика задаёт ПАРТНЁР, а не мы. Проиндексировав больше, чем он
        // готов хранить, мы разойдёмся с его декодировщиком — и сломаются все последующие
        // запросы этого соединения, а не только текущий.
        var encoder = new HPackEncoder(dynamicTableSize: 128);

        for (var index = 0; index < 50; index++)
            _ = Encode(encoder, [new KeyValuePair<string, string>($"x-header-{index}", $"значение-{index}")]);

        Assert.That(encoder.DynamicTableSizeInUse, Is.LessThanOrEqualTo(128));
    }

    [Test]
    public void DecoderFollowsTheEncoderExactly()
    {
        // Главная опасность индексирования — расхождение таблиц. Проверяем сквозным разбором:
        // всё, что закодировано, обязано разобраться обратно, и на повторах тоже.
        var encoder = new HPackEncoder(4096);
        var decoder = new HPackDecoder(4096);

        for (var round = 0; round < 5; round++)
        {
            var block = new ArrayBufferWriter<byte>(512);
            encoder.Encode(block, BrowserHeaders);

            var decoded = decoder.Decode(block.WrittenSpan).ToArray();

            Assert.That(decoded, Is.EqualTo(BrowserHeaders).AsCollection, $"круг {round}");
        }
    }

    private static int Encode(HPackEncoder encoder, IEnumerable<KeyValuePair<string, string>> headers)
    {
        var block = new ArrayBufferWriter<byte>(512);
        encoder.Encode(block, headers);

        return block.WrittenCount;
    }
}
