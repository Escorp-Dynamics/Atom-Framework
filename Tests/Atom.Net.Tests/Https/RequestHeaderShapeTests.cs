using System.Linq;
using System.Net;
using System.Net.Http;
using Atom.Net.Https;
using Atom.Net.Https.Headers;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет форму блока заголовков запроса: целость значений и их порядок.
/// </summary>
/// <remarks>
/// Оба свойства наблюдаемы сервером и оба были нарушены, причём молча — соединение работало, а
/// сигнатура HTTP/2 их не покрывает: она про SETTINGS, окно и псевдозаголовки.
///
/// Первое нарушение: обычный обход <see cref="System.Net.Http.Headers.HttpHeaders"/> отдаёт
/// РАЗОБРАННЫЕ значения. Строка агента распадалась на пять кусков по пробелам, <c>accept</c> — на
/// восемь по запятым, и каждый кусок уезжал отдельным заголовком. Замер на зеркале показывал
/// двадцать шесть строк там, где настоящий Chrome отправляет тринадцать.
///
/// Второе: порядок заголовков задавался порядком их добавления, а не профилем. Политика,
/// описывающая порядок, применялась ТОЛЬКО к HTTP/1.1 — при том что основной путь идёт по HTTP/2.
///
/// Эталоны сняты с настоящих браузеров: Chrome 151 — через <c>--dump-dom</c> на tls.peet.ws,
/// Firefox 154 — из его собственного журнала (<c>MOZ_LOG=nsHttp:5</c>).
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class RequestHeaderShapeTests
{
    private const int TestTimeoutMs = 30000;

    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private const string ChromeAccept =
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8";

    [Test]
    public void MultiPartValuesStayOnASingleHeaderLine()
    {
        // ★ Ровно тот случай, который ломался: платформа разбирает эти значения на элементы, и
        // обычный обход отдаёт их по отдельности.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");

        request.Headers.TryAddWithoutValidation("user-agent", ChromeUserAgent);
        request.Headers.TryAddWithoutValidation("accept", ChromeAccept);
        request.Headers.TryAddWithoutValidation("accept-encoding", "gzip, deflate, br, zstd");
        request.Headers.TryAddWithoutValidation("accept-language", "en-US,en;q=0.9");

        var collected = new List<KeyValuePair<string, string>>();
        RequestHeaderReader.Collect(request, collected, lowercaseNames: true);

        Assert.Multiple(() =>
        {
            Assert.That(collected, Has.Exactly(1).Matches<KeyValuePair<string, string>>(static h => h.Key is "user-agent"));
            Assert.That(collected, Has.Exactly(1).Matches<KeyValuePair<string, string>>(static h => h.Key is "accept"));
            Assert.That(collected, Has.Exactly(1).Matches<KeyValuePair<string, string>>(static h => h.Key is "accept-encoding"));
            Assert.That(collected, Has.Exactly(1).Matches<KeyValuePair<string, string>>(static h => h.Key is "accept-language"));

            Assert.That(Value(collected, "user-agent"), Is.EqualTo(ChromeUserAgent), "строка агента обязана уехать целиком");
            Assert.That(Value(collected, "accept"), Is.EqualTo(ChromeAccept));
            Assert.That(Value(collected, "accept-encoding"), Is.EqualTo("gzip, deflate, br, zstd"));
        });
    }

    [Test]
    public void ContentHeadersAreCollectedToo()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/")
        {
            Content = new StringContent("тело", System.Text.Encoding.UTF8, "application/json"),
        };

        var collected = new List<KeyValuePair<string, string>>();
        RequestHeaderReader.Collect(request, collected, lowercaseNames: true);

        Assert.That(Value(collected, "content-type"), Does.StartWith("application/json"));
    }

    [Test]
    public void ChromeOrderMatchesTheMeasuredBrowser()
    {
        // Замер настоящего Chrome 151 на tls.peet.ws, запрос навигации.
        string[] expected =
        [
            "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
            "upgrade-insecure-requests",
            "user-agent", "accept",
            "sec-fetch-site", "sec-fetch-mode", "sec-fetch-user", "sec-fetch-dest",
            "accept-encoding", "accept-language", "priority",
        ];

        Assert.That(Order(HeadersFormattingPolicy.Chrome, expected), Is.EqualTo(expected).AsCollection);
    }

    [Test]
    public void FirefoxOrderMatchesTheMeasuredBrowser()
    {
        // Замер настоящего Firefox 154 из его журнала. Отличий от Chromium три: язык раньше
        // кодировок, признак навигации не в начале, обратный порядок sec-fetch-*.
        string[] expected =
        [
            "user-agent", "accept", "accept-language", "accept-encoding",
            "upgrade-insecure-requests",
            "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site", "sec-fetch-user",
            "priority",
        ];

        Assert.That(Order(HeadersFormattingPolicy.Firefox, expected), Is.EqualTo(expected).AsCollection);
    }

    [Test]
    public void BrowsersDoNotShareOneOrder()
    {
        // Страховка от возврата к общему порядку: прежде Firefox наследовал хромиумовский, и
        // расхождение с браузером было ровно в этом.
        string[] names = ["user-agent", "accept", "accept-language", "accept-encoding", "upgrade-insecure-requests"];

        Assert.That(Order(HeadersFormattingPolicy.Firefox, names), Is.Not.EqualTo(Order(HeadersFormattingPolicy.Chrome, names)).AsCollection);
    }

    /// <summary>
    /// Прогоняет набор имён через политику и возвращает получившийся порядок.
    /// </summary>
    /// <param name="policy">Политика браузера.</param>
    /// <param name="names">Имена заголовков.</param>
    /// <returns>Имена в порядке отправки.</returns>
    private static string[] Order(IHeadersFormattingPolicy policy, string[] names)
    {
        var input = names.ToDictionary(static name => name, static name => "значение", StringComparer.OrdinalIgnoreCase);

        return [.. policy.Format(input, HttpVersion.Version20, RequestKind.Navigation, useCookieCrumbling: false)
            .Select(static header => header.Key.ToLowerInvariant())];
    }

    private static string Value(List<KeyValuePair<string, string>> headers, string name)
        => headers.First(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}
