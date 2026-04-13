using System.Reflection;
using Atom.Net.Browsing.WebDriver;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[Category("PublicApi")]
public sealed class WebDriverBrowserProfileApiSurfaceTests
{
    [Test]
    public void BrowserProfilePresetAndPropertySurfaceSpecTest()
    {
        var profile = typeof(WebBrowserProfile);

        Assert.Multiple(() =>
        {
            AssertPresetProperty(profile, nameof(WebBrowserProfile.Chrome));
            AssertPresetProperty(profile, nameof(WebBrowserProfile.Edge));
            AssertPresetProperty(profile, nameof(WebBrowserProfile.Brave));
            AssertPresetProperty(profile, nameof(WebBrowserProfile.Opera));
            AssertPresetProperty(profile, nameof(WebBrowserProfile.Vivaldi));
            AssertPresetProperty(profile, nameof(WebBrowserProfile.Yandex));
            AssertPresetProperty(profile, nameof(WebBrowserProfile.Firefox));

            PublicApiAssert.RequireProperty(profile, nameof(WebBrowserProfile.Path));
            PublicApiAssert.RequireProperty(profile, nameof(WebBrowserProfile.BinaryPath));
            PublicApiAssert.RequireProperty(profile, nameof(WebBrowserProfile.Channel));
            PublicApiAssert.RequireProperty(profile, nameof(WebBrowserProfile.IsInstalled));
        });
    }

    [Test]
    public void BrowserProfileConstructorsSurfaceSpecTest()
    {
        Assert.Multiple(() =>
        {
            AssertConstructors(typeof(ChromeProfile));
            AssertConstructors(typeof(EdgeProfile));
            AssertConstructors(typeof(BraveProfile));
            AssertConstructors(typeof(OperaProfile));
            AssertConstructors(typeof(VivaldiProfile));
            AssertConstructors(typeof(YandexProfile));
            AssertConstructors(typeof(FirefoxProfile));
        });
    }

    private static void AssertPresetProperty(Type type, string propertyName)
    {
        var property = PublicApiAssert.RequireProperty(type, propertyName);
        Assert.That(property.PropertyType, Is.EqualTo(typeof(WebBrowserProfile)),
            $"Свойство '{type.Name}.{propertyName}' должно возвращать WebBrowserProfile.");
    }

    private static void AssertConstructors(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.That(constructors.Any(static constructor => HasParameters(constructor, nameof(String), nameof(WebBrowserChannel))), Is.True,
            $"У типа '{type.Name}' ожидался ctor (string, WebBrowserChannel).");
        Assert.That(constructors.Any(static constructor => HasParameters(constructor, nameof(String))), Is.True,
            $"У типа '{type.Name}' ожидался ctor (string).");
        Assert.That(constructors.Any(static constructor => HasParameters(constructor, nameof(WebBrowserChannel))), Is.True,
            $"У типа '{type.Name}' ожидался ctor (WebBrowserChannel).");
        Assert.That(constructors.Any(static constructor => constructor.GetParameters().Length == 0), Is.True,
            $"У типа '{type.Name}' ожидался ctor ().");
    }

    private static bool HasParameters(ConstructorInfo constructor, params string[] tokens)
        => constructor.GetParameters().Select(static parameter => parameter.ParameterType.Name).SequenceEqual(tokens);
}