using System.Net;
using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverPublicApiContractTests
{
    [Test]
    public void ElementSelectorFactoriesPopulateStrategyAndValue()
    {
        var css = ElementSelector.Css("body");
        var xpath = ElementSelector.XPath("//body");
        var id = ElementSelector.Id("root");
        var text = ElementSelector.Text("Hello");
        var name = ElementSelector.Name("email");
        var tag = ElementSelector.TagName("button");
        var typedCss = new CssSelector(".item");

        Assert.Multiple(() =>
        {
            AssertSelector(css, ElementSelectorStrategy.Css, "body");
            AssertSelector(xpath, ElementSelectorStrategy.XPath, "//body");
            AssertSelector(id, ElementSelectorStrategy.Id, "root");
            AssertSelector(text, ElementSelectorStrategy.Text, "Hello");
            AssertSelector(name, ElementSelectorStrategy.Name, "email");
            AssertSelector(tag, ElementSelectorStrategy.TagName, "button");
            Assert.That(typedCss, Is.TypeOf<CssSelector>());
            AssertSelector(typedCss, ElementSelectorStrategy.Css, ".item");
        });
    }

    [Test]
    public void DeviceAllReturnsIndependentPresetInstances()
    {
        var first = Device.All.ToArray();
        var second = Device.All.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Length.GreaterThanOrEqualTo(4));
            Assert.That(first[0].Name, Is.Not.Empty);
            Assert.That(ReferenceEquals(first[0], second[0]), Is.False);
            Assert.That(Device.MacBookPro14.IsMobile, Is.False);
            Assert.That(Device.iPhoneX.HasTouch, Is.True);
        });
    }

    [Test]
    public void WebBrowserProfileTracksBinaryInstallationState()
    {
        var binaryPath = Path.Combine(Path.GetTempPath(), $"atom-webdriver-firefox-{Guid.NewGuid():N}");
        File.WriteAllText(binaryPath, string.Empty);

        try
        {
            var profile = new FirefoxProfile(binaryPath, WebBrowserChannel.Beta)
            {
                Path = "profile-data",
            };
            var missingPath = Path.Combine(Path.GetTempPath(), $"atom-webdriver-missing-{Guid.NewGuid():N}");

            profile.BinaryPath = missingPath;
            profile.Channel = WebBrowserChannel.Dev;

            Assert.Multiple(() =>
            {
                Assert.That(new FirefoxProfile(binaryPath, WebBrowserChannel.Beta).IsInstalled, Is.True);
                Assert.That(profile.Path, Is.EqualTo("profile-data"));
                Assert.That(profile.BinaryPath, Is.EqualTo(missingPath));
                Assert.That(profile.Channel, Is.EqualTo(WebBrowserChannel.Dev));
                Assert.That(profile.IsInstalled, Is.False);
            });
        }
        finally
        {
            if (File.Exists(binaryPath))
                File.Delete(binaryPath);
        }
    }

    [Test]
    public void WebWindowSettingsKeepProxyOverrideState()
    {
        var proxy = new WebProxy("http://127.0.0.1:8080");
        var settings = new WebWindowSettings
        {
            Proxy = proxy,
            UseProxy = false,
            Device = Device.Pixel7,
        };

        Assert.Multiple(() =>
        {
            Assert.That(settings.Proxy, Is.SameAs(proxy));
            Assert.That(settings.UseProxy, Is.False);
            Assert.That(settings.Device, Is.SameAs(Device.Pixel7));
        });
    }

    private static void AssertSelector(ElementSelector selector, ElementSelectorStrategy strategy, string value)
    {
        Assert.That(selector.Strategy, Is.EqualTo(strategy));
        Assert.That(selector.Value, Is.EqualTo(value));
    }
}