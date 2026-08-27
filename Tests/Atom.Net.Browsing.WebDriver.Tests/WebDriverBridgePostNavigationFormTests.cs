using System.Text;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// POST-навигация уезжает в браузер как самоотправляющаяся форма (см. WaitRoom-реплей).
/// Форма обязана воспроизводить form-urlencoded тело и отказываться от того, что не воспроизводит.
/// </summary>
[TestFixture]
public sealed class WebDriverBridgePostNavigationFormTests
{
    private static readonly Uri Target = new("https://waitroom.test/challenge");

    [Test]
    public void TryBuildFormUrlEncodedBodyProducesAutoSubmittingPostFormWithFields()
    {
        var body = Encoding.UTF8.GetBytes("token=abc123&answer=42");

        var built = BridgePostNavigationForm.TryBuild(Target, body, "application/x-www-form-urlencoded", out var html);

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.True);
            Assert.That(html, Does.Contain("method=\"post\""));
            Assert.That(html, Does.Contain("action=\"https://waitroom.test/challenge\""));
            Assert.That(html, Does.Contain("name=\"token\""));
            Assert.That(html, Does.Contain("value=\"abc123\""));
            Assert.That(html, Does.Contain("name=\"answer\""));
            Assert.That(html, Does.Contain("value=\"42\""));
            Assert.That(html, Does.Contain(".submit()"));
        });
    }

    [Test]
    public void TryBuildMissingContentTypeTreatedAsFormUrlEncoded()
    {
        var body = Encoding.UTF8.GetBytes("a=1");

        var built = BridgePostNavigationForm.TryBuild(Target, body, contentType: null, out var html);

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.True);
            Assert.That(html, Does.Contain("name=\"a\""));
            Assert.That(html, Does.Contain("value=\"1\""));
        });
    }

    [Test]
    public void TryBuildDecodesPercentAndPlusFaithfully()
    {
        // 'q=a b&r=x/y' закодировано urlencoded; форма должна восстановить исходные значения.
        var body = Encoding.UTF8.GetBytes("q=a+b&r=x%2Fy");

        var built = BridgePostNavigationForm.TryBuild(Target, body, "application/x-www-form-urlencoded; charset=utf-8", out var html);

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.True);
            Assert.That(html, Does.Contain("value=\"a b\""));
            Assert.That(html, Does.Contain("value=\"x/y\""));
        });
    }

    [Test]
    public void TryBuildEscapesHtmlSensitiveFieldValues()
    {
        var body = Encoding.UTF8.GetBytes("payload=%22%3E%3Cscript%3E");

        var built = BridgePostNavigationForm.TryBuild(Target, body, "application/x-www-form-urlencoded", out var html);

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.True);
            Assert.That(html, Does.Not.Contain("<script>\""));
            Assert.That(html, Does.Contain("&lt;script&gt;").Or.Contain("&lt;script&gt").Or.Contain("&lt;"));
        });
    }

    [Test]
    public void TryBuildNonFormContentTypeIsRejectedSoCallerKeepsSyntheticPath()
    {
        var body = Encoding.UTF8.GetBytes("{\"token\":\"abc\"}");

        var built = BridgePostNavigationForm.TryBuild(Target, body, "application/json", out var html);

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.False);
            Assert.That(html, Is.Empty);
        });
    }
}
