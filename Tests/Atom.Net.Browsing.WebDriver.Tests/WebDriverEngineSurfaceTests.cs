using Atom.Net.Browsing.WebDriver;

namespace Tests;

/// <summary>
/// Поверхности личности, существующие только у Chromium, не должны доставаться другим движкам.
/// </summary>
/// <remarks>
/// Профиль Firefox получал строки WebGL программного рендерера Chromium (ANGLE/SwiftShader) и
/// <c lang="text">navigator.deviceMemory</c>, которого у Gecko нет вовсе, — Cloudflare отвергал
/// такую личность. Гейт срезает эти поверхности только там, где движок ТОЧНО другой.
/// </remarks>
[TestFixture]
public class WebDriverEngineSurfaceTests
{
    [TestCase("Mozilla/5.0 (X11; Linux x86_64; rv:157.0) Gecko/20100101 Firefox/157.0", true, TestName = "Firefox — не Chromium")]
    [TestCase("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1", true, TestName = "Safari iOS — не Chromium")]
    [TestCase("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36", false, TestName = "Chrome — Chromium")]
    [TestCase("Mozilla/5.0 Test Agent", false, TestName = "Строка без движка — не срезается")]
    [TestCase("", false, TestName = "Пустая строка — не срезается")]
    public void DeclaresNonChromiumEngineRecognizesOnlyKnownOtherEngines(string userAgent, bool expected)
        => Assert.That(WebBrowser.DeclaresNonChromiumEngine(userAgent), Is.EqualTo(expected));
}
