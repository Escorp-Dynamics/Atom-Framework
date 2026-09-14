namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Закрепляет отбор запросов по шаблонам перехвата.
/// </summary>
/// <remarks>
/// Цена ошибки здесь несимметрична. Пропустить нужный запрос — значит молча остаться без данных:
/// прежний шаблон вторичного токена Turnstile не совпадал ни с чем, и это заметили спустя недели.
/// Захватить лишний — хуже: каждый перехваченный запрос идёт раунд-трипом через мост и
/// переотправляется нашим стеком, а для потока телеметрии Cloudflare такая задержка означает
/// отказ решения (замер: КПД 100% → 0%, «Bot behavior detected» на всех задачах).
/// </remarks>
[TestFixture]
public sealed class WebDriverRequestInterceptionPatternTests
{
    private const string ChallengeSecondaryTokenUrl = "https://visa.vfsglobal.com/cdn-cgi/challenge-platform/h/g/c/a21bfdfc498dba93";
    private const string ChallengeTelemetryUrl = "https://challenges.cloudflare.com/cdn-cgi/challenge-platform/h/g/fo/1885897970:1785157314:UzqB/a21bfd6c/lgDyogb";

    [Test]
    public void MethodPrefixNarrowsPatternToRequestedVerb()
    {
        var state = RequestInterceptionState.Create(enabled: true, ["POST https://**/cdn-cgi/challenge-platform/h/*/c/*"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Matches(ChallengeSecondaryTokenUrl, "POST"), Is.True);
            Assert.That(state.Matches(ChallengeSecondaryTokenUrl, "GET"), Is.False, "метод обязан отсекать");
        }
    }

    /// <summary>
    /// Телеметрия челленджа под шаблон вторичного токена не подпадает.
    /// </summary>
    /// <remarks>
    /// Оба адреса начинаются одинаково — <c lang="text">/cdn-cgi/challenge-platform/h/g/</c>, — и
    /// различаются лишь сегментом после версии: <c lang="text">c</c> против <c lang="text">fo</c>.
    /// Именно на этом различии держится безопасность шаблона.
    /// </remarks>
    [Test]
    public void SecondaryTokenPatternDoesNotCaptureChallengeTelemetry()
    {
        var state = RequestInterceptionState.Create(enabled: true, ["POST https://**/cdn-cgi/challenge-platform/h/*/c/*"]);

        Assert.That(state.Matches(ChallengeTelemetryUrl, "POST"), Is.False);
    }

    [Test]
    public void PatternWithoutMethodPrefixMatchesAnyVerb()
    {
        var state = RequestInterceptionState.Create(enabled: true, ["https://**/cdn-cgi/challenge-platform/h/*/c/*"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Matches(ChallengeSecondaryTokenUrl, "POST"), Is.True);
            Assert.That(state.Matches(ChallengeSecondaryTokenUrl, "GET"), Is.True);
            Assert.That(state.Matches(ChallengeSecondaryTokenUrl, method: null), Is.True);
        }
    }

    /// <summary>
    /// Адрес со схемой не принимается за шаблон с префиксом метода.
    /// </summary>
    /// <remarks>
    /// Разбор ищет пробел до первого двоеточия и требует, чтобы голова состояла только из букв:
    /// иначе <c lang="text">"https://host/a b"</c> расценивался бы как метод <c lang="text">https</c>.
    /// </remarks>
    [Test]
    public void SchemeIsNotMistakenForMethodPrefix()
    {
        var state = RequestInterceptionState.Create(enabled: true, ["https://example.test/a*"]);

        Assert.That(state.Matches("https://example.test/api/items", "GET"), Is.True);
    }

    [Test]
    public void DisabledStateMatchesNothing()
    {
        var state = RequestInterceptionState.Create(enabled: false, ["POST https://**/*"]);

        Assert.That(state.Matches(ChallengeSecondaryTokenUrl, "POST"), Is.False);
    }

    [Test]
    public void EmptyPatternListMatchesEverything()
    {
        var state = RequestInterceptionState.Create(enabled: true, urlPatterns: null);

        Assert.That(state.Matches(ChallengeTelemetryUrl, "POST"), Is.True);
    }
}
