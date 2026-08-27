using System.Linq;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Http2;

namespace Atom.Net.Tests.Https;

/// <summary>
/// Проверяет, что заголовки соединения не попадают в блок HTTP/2 и HTTP/3.
/// </summary>
/// <remarks>
/// RFC 9113 §8.2.2 и RFC 9114 §4.2 объявляют сообщение с такими заголовками МАЛФОРМИРОВАННЫМ, и
/// получатель обязан сбросить поток с кодом PROTOCOL_ERROR.
///
/// Проверка появилась по конкретному поводу. Профиль браузера добавляет
/// <c>connection: keep-alive</c> — для HTTP/1.1 это правильно, — и без фильтра он доезжал до
/// кадра HEADERS. Cloudflare такой запрос обслуживал как ни в чём не бывало, а
/// <c>www.google.com</c> отвечал сбросом потока с кодом 1. То есть отказ выглядел сетевым, был
/// избирательным по серверам, и ни один локальный прогон его не показывал.
///
/// Отдельно закреплено, что правило ОБЩЕЕ для обеих версий: два независимых списка неизбежно
/// разойдутся, а разойдясь, дадут ту же ошибку ровно на одном из протоколов.
/// </remarks>
[CancelAfter(TestTimeoutMs)]
public sealed class ProhibitedHeaderTests
{
    private const int TestTimeoutMs = 30000;

    /// <summary>Заголовки соединения из RFC 9113 §8.2.2.</summary>
    private static readonly string[] ConnectionSpecific =
    [
        "connection", "keep-alive", "proxy-connection", "transfer-encoding", "upgrade",
    ];

    [TestCaseSource(nameof(ConnectionSpecific))]
    public void ConnectionHeadersNeverReachTheHttp2Block(string name)
    {
        var headers = Http2RequestHeaders.Build(
            "GET",
            "example.com",
            "/",
            [new KeyValuePair<string, string>(name, "keep-alive"), new KeyValuePair<string, string>("accept", "*/*")],
            Http2ProfileCatalog.CreateChrome());

        Assert.Multiple(() =>
        {
            Assert.That(headers.Select(static header => header.Key), Does.Not.Contain(name));
            Assert.That(headers.Select(static header => header.Key), Contains.Item("accept"), "остальные заголовки должны уцелеть");
        });
    }

    [Test]
    public void LegacyHostHeaderIsDroppedBesideAuthority()
    {
        // Устаревший host рядом с :authority — тоже некорректное сообщение (RFC 9113 §8.3.1).
        var headers = Http2RequestHeaders.Build(
            "GET",
            "example.com",
            "/",
            [new KeyValuePair<string, string>("host", "example.com")],
            Http2ProfileCatalog.CreateChrome());

        Assert.Multiple(() =>
        {
            Assert.That(headers.Select(static header => header.Key), Does.Not.Contain("host"));
            Assert.That(headers.Select(static header => header.Key), Contains.Item(":authority"));
        });
    }

    [Test]
    public void TrailersIsTheOnlyAllowedTransferEncodingHint()
    {
        // te — единственное исключение из списка соединения, и допустимо ровно с одним значением.
        var allowed = Http2RequestHeaders.Build(
            "GET", "example.com", "/",
            [new KeyValuePair<string, string>("te", "trailers")],
            Http2ProfileCatalog.CreateChrome());

        var rejected = Http2RequestHeaders.Build(
            "GET", "example.com", "/",
            [new KeyValuePair<string, string>("te", "gzip")],
            Http2ProfileCatalog.CreateChrome());

        Assert.Multiple(() =>
        {
            Assert.That(allowed.Select(static header => header.Key), Contains.Item("te"));
            Assert.That(rejected.Select(static header => header.Key), Does.Not.Contain("te"));
        });
    }

    [TestCaseSource(nameof(ConnectionSpecific))]
    public void RuleIsSharedByBothVersions(string name)
    {
        // Правило одно на HTTP/2 и HTTP/3 намеренно: собственный список у каждой версии рано или
        // поздно разойдётся, и запрос сломается ровно на одном протоколе.
        Assert.That(ConnectionHeaderRules.IsConnectionSpecific(name), Is.True);
        Assert.That(ConnectionHeaderRules.IsProhibited(name.ToUpperInvariant(), "значение"), Is.True, "регистр имени роли не играет");
    }

    [TestCase("accept")]
    [TestCase("user-agent")]
    [TestCase("cookie")]
    [TestCase("content-type")]
    public void OrdinaryHeadersAreNotTouched(string name)
    {
        Assert.That(ConnectionHeaderRules.IsProhibited(name, "значение"), Is.False);
    }
}
