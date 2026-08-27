using System.Linq;
using System.Buffers.Binary;
using System.Text;
using Atom.Net.Https.Http2;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет наблюдаемую сервером часть HTTP/2: преамбулу, состав и порядок SETTINGS, приращение
/// окна, кадры приоритетов и порядок псевдозаголовков.
/// </summary>
/// <remarks>
/// Эти тесты закрепляют ОТПЕЧАТОК, а не корректность протокола. Сервер строит по началу соединения
/// сигнатуру (широко известную как «Akamai fingerprint»), и любая перестановка параметров меняет
/// её, даже когда соединение остаётся полностью работоспособным. Без такого закрепления любая
/// последующая правка профиля прошла бы незамеченной — и обнаружилась бы только отказами на
/// реальном сервисе.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class Http2FingerprintTests
{
    private const int TestTimeoutMs = 30000;

    /// <summary>Порядок псевдозаголовков, наблюдаемый у браузеров на движке Chromium.</summary>
    private static readonly string[] ChromiumPseudoHeaderOrder = [":method", ":authority", ":scheme", ":path"];

    private static readonly string[] CrumbledCookies = ["a=1", "b=2", "c=3"];

    private static readonly string[] SingleCookie = ["a=1; b=2"];

    [Test]
    public void ClientPrefaceMatchesSpecification()
    {
        var magic = Encoding.ASCII.GetString(Http2Preface.ClientMagic);

        Assert.That(magic, Is.EqualTo("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"));
    }

    [Test]
    public void ChromeSendsSettingsInObservedOrder()
    {
        var settings = Http2ProfileCatalog.CreateChrome();
        var buffer = new byte[Http2Preface.GetRequiredSize(settings)];

        var written = Http2Preface.WriteSettings(buffer, settings);
        var header = Http2FrameHeader.Read(buffer);

        var ids = new List<ushort>();
        var values = new List<uint>();
        for (var offset = Http2FrameHeader.Size; offset < written; offset += 6)
        {
            ids.Add(BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset, 2)));
            values.Add(BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset + 2, 4)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(header.Type, Is.EqualTo(Http2FrameType.Settings));
            Assert.That(header.StreamId, Is.Zero, "SETTINGS всегда идёт на уровне соединения");
            Assert.That(header.Length, Is.EqualTo(ids.Count * 6));

            // Порядок именно такой, какой наблюдается у Chrome: перестановка меняет отпечаток.
            Assert.That(ids, Is.EqualTo(new ushort[]
            {
                (ushort)Http2SettingId.HeaderTableSize,
                (ushort)Http2SettingId.EnablePush,
                (ushort)Http2SettingId.InitialWindowSize,
                (ushort)Http2SettingId.MaxHeaderListSize,
            }));

            Assert.That(values, Is.EqualTo(new uint[] { 65536, 0, 6291456, 262144 }));
        });
    }

    [Test]
    public void ChromeRaisesConnectionWindowByObservedIncrement()
    {
        var settings = Http2ProfileCatalog.CreateChrome();
        var buffer = new byte[Http2Preface.GetRequiredSize(settings)];

        var written = Http2Preface.Write(buffer, settings, Http2ProfileCatalog.ChromeConnectionWindowIncrement);

        // После преамбулы и SETTINGS обязан идти WINDOW_UPDATE уровня соединения.
        var offset = Http2Preface.ClientMagic.Length;
        var settingsHeader = Http2FrameHeader.Read(buffer.AsSpan(offset));
        offset += Http2FrameHeader.Size + settingsHeader.Length;

        var windowHeader = Http2FrameHeader.Read(buffer.AsSpan(offset));
        var increment = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset + Http2FrameHeader.Size, 4));

        Assert.Multiple(() =>
        {
            Assert.That(windowHeader.Type, Is.EqualTo(Http2FrameType.WindowUpdate));
            Assert.That(windowHeader.StreamId, Is.Zero);
            Assert.That(increment, Is.EqualTo(15663105u), "Chrome доводит окно соединения до 16 МБ именно этим приращением");
            Assert.That(written, Is.LessThanOrEqualTo(buffer.Length));
        });
    }

    [Test]
    public void ChromeDoesNotSendPriorityFrames()
    {
        var settings = Http2ProfileCatalog.CreateChrome();
        var buffer = new byte[Http2Preface.GetRequiredSize(settings)];

        var written = Http2Preface.Write(buffer, settings, Http2ProfileCatalog.ChromeConnectionWindowIncrement);
        var expected = Http2Preface.ClientMagic.Length
            + Http2FrameHeader.Size + (4 * 6)
            + Http2FrameHeader.Size + 4;

        // Лишние кадры так же заметны, как недостающие: Chrome дерево приоритетов не строит.
        Assert.That(written, Is.EqualTo(expected));
    }

    [Test]
    public void FirefoxSendsNoPriorityFrames()
    {
        // Дерево приоритетов из пяти служебных потоков Firefox когда-то строил, и первые версии
        // профиля его повторяли. Замер настоящего Firefox 154 показывает, что кадров приоритета
        // он больше не отправляет вовсе, — а лишний кадр в преамбуле виден серверу ровно так же,
        // как недостающий, и делает соединение непохожим на браузер при всех совпавших SETTINGS.
        var settings = Http2ProfileCatalog.CreateFirefox();
        var buffer = new byte[Http2Preface.GetRequiredSize(settings)];

        var written = Http2Preface.Write(buffer, settings, connectionWindowIncrement: 12517377);

        var offset = Http2Preface.ClientMagic.Length;
        var settingsHeader = Http2FrameHeader.Read(buffer.AsSpan(offset));
        offset += Http2FrameHeader.Size + settingsHeader.Length;
        offset += Http2FrameHeader.Size + 4;

        var frameTypes = new List<Http2FrameType>();
        while (offset < written)
        {
            var header = Http2FrameHeader.Read(buffer.AsSpan(offset));
            frameTypes.Add(header.Type);
            offset += Http2FrameHeader.Size + header.Length;
        }

        Assert.Multiple(() =>
        {
            Assert.That(settings.UsePriorityFrames, Is.False);
            Assert.That(settings.PriorityTree, Is.Empty);
            Assert.That(frameTypes, Does.Not.Contain(Http2FrameType.Priority), "Firefox 154 кадров приоритета не отправляет");
        });
    }

    [Test]
    public void PseudoHeadersFollowChromiumOrder()
    {
        var settings = Http2ProfileCatalog.CreateChrome();

        var headers = Http2RequestHeaders.Build("GET", "example.com", "/", [], settings);
        var names = headers.Take(4).Select(static header => header.Key).ToArray();

        // Порядок :method, :authority, :scheme, :path отличает Chromium от «естественного»
        // порядка спецификации, которым пользуется большинство библиотек.
        Assert.That(names, Is.EqualTo(ChromiumPseudoHeaderOrder));
    }

    [Test]
    public void ChromeCrumblesCookiesIntoSeparateHeaders()
    {
        var settings = Http2ProfileCatalog.CreateChrome();

        var headers = Http2RequestHeaders.Build(
            "GET",
            "example.com",
            "/",
            [new KeyValuePair<string, string>("cookie", "a=1; b=2; c=3")],
            settings);

        var cookies = headers.Where(static header => header.Key is "cookie").Select(static header => header.Value).ToArray();

        Assert.That(cookies, Is.EqualTo(CrumbledCookies));
    }

    [Test]
    public void FirefoxKeepsCookiesInSingleHeader()
    {
        var settings = Http2ProfileCatalog.CreateFirefox();

        var headers = Http2RequestHeaders.Build(
            "GET",
            "example.com",
            "/",
            [new KeyValuePair<string, string>("cookie", "a=1; b=2")],
            settings);

        var cookies = headers.Where(static header => header.Key is "cookie").Select(static header => header.Value).ToArray();

        Assert.That(cookies, Is.EqualTo(SingleCookie));
    }

    [Test]
    public void HeaderNamesAreLowercased()
    {
        var settings = Http2ProfileCatalog.CreateChrome();

        var headers = Http2RequestHeaders.Build(
            "GET",
            "example.com",
            "/",
            [new KeyValuePair<string, string>("User-Agent", "test")],
            settings);

        // Заглавные буквы в именах делают сообщение HTTP/2 некорректным, а не просто нестандартным.
        Assert.That(headers.Any(static header => header.Key is "user-agent"), Is.True);
    }

    [Test]
    public void FrameHeaderRoundTripsAllFields()
    {
        var buffer = new byte[Http2FrameHeader.Size];
        var original = new Http2FrameHeader(16384, Http2FrameType.Headers, flags: 0x05, streamId: int.MaxValue);

        original.Write(buffer);
        var restored = Http2FrameHeader.Read(buffer);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.EqualTo(original));
            Assert.That(restored.EndStream, Is.True);
            Assert.That(restored.EndHeaders, Is.True);
            Assert.That(restored.Length, Is.EqualTo(16384));
            Assert.That(restored.StreamId, Is.EqualTo(int.MaxValue));
        });
    }

    [Test]
    public void FrameHeaderClearsReservedBitOfStreamId()
    {
        var buffer = new byte[Http2FrameHeader.Size];

        // Старший бит идентификатора зарезервирован: если его не сбросить, сервер сочтёт кадр
        // некорректным и оборвёт соединение.
        new Http2FrameHeader(0, Http2FrameType.Data, flags: 0, streamId: unchecked((int)0xFFFFFFFF)).Write(buffer);

        Assert.That(buffer[5] & 0x80, Is.Zero);
    }

    [TestCase("Chrome", "masp", ":method,:authority,:scheme,:path")]
    [TestCase("Firefox", "mpas", ":method,:path,:authority,:scheme")]
    public void PseudoHeaderOrderFollowsTheProfile(string browser, string order, string expected)
    {
        // ★ Порядок псевдозаголовков — последнее поле сигнатуры HTTP/2, и он наблюдаем напрямую.
        // У браузеров он РАЗНЫЙ: Chrome шлёт m,a,s,p, Firefox — m,p,a,s. Раньше он был зашит
        // хромиумовским для всех, и профиль Firefox совпадал с браузером во всём, кроме этого
        // поля: ja3 и ja4 сходились, а сигнатура HTTP/2 — нет.
        //
        // Эталон Firefox снят с настоящего браузера 154 на tls.peet.ws:
        //   akamai = 1:65536;2:0;4:131072;5:16384|12517377|0|m,p,a,s
        var settings = browser is "Chrome" ? Http2ProfileCatalog.CreateChrome() : Http2ProfileCatalog.CreateFirefox();
        var headers = Http2RequestHeaders.Build("GET", "example.com", "/", [], settings);

        var pseudo = string.Join(',', headers.TakeWhile(static header => header.Key.StartsWith(':')).Select(static header => header.Key));

        Assert.Multiple(() =>
        {
            Assert.That(settings.PseudoHeaderOrder, Is.EqualTo(order));
            Assert.That(pseudo, Is.EqualTo(expected));
        });
    }

    [Test]
    public void EveryProfileNamesAllFourPseudoHeaders()
    {
        // Неполный порядок означал бы запрос без обязательного псевдозаголовка — то есть
        // малформированное сообщение, а не просто необычное.
        foreach (var settings in new[] { Http2ProfileCatalog.CreateChrome(), Http2ProfileCatalog.CreateFirefox(), Http2ProfileCatalog.CreateSafari() })
        {
            Assert.That(settings.PseudoHeaderOrder.Order(), Is.EqualTo("amps".Order()).AsCollection, settings.PseudoHeaderOrder);
        }
    }
}
