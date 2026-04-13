using System.Reflection;
using System.Runtime.Versioning;
using Atom.Hardware.Display;
using Atom.Hardware.Input;
using WebBrowser = Atom.Net.Browsing.WebDriver.Tests.WebDriverTestEnvironment;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[NonParallelizable]
[Category("Hardware")]
public sealed class WebDriverElementTrustedInputTests
{
    [Test]
    [SupportedOSPlatform("linux")]
    public async Task ElementClickAsyncResolvesMouseThroughBrowserCascade()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed element trusted input test рассчитан на Linux.");

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Device = Device.Pixel7,
        }).ConfigureAwait(false);

        var element = new Element((WebPage)browser.CurrentPage);

        try
        {
            await element.ClickAsync().ConfigureAwait(false);
            Assert.That(GetCurrentMouse(browser), Is.Not.Null);
        }
        catch (VirtualMouseException ex)
        {
            Assert.Ignore("Virtual mouse backend недоступен — пропускаем: " + ex.Message);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task ElementPressAsyncResolvesKeyboardThroughBrowserCascade()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed element trusted input test рассчитан на Linux.");

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);
        var element = new Element((WebPage)browser.CurrentPage);

        try
        {
            await element.PressAsync(ConsoleKey.Enter).ConfigureAwait(false);
            Assert.That(GetCurrentKeyboard(browser), Is.Not.Null);
        }
        catch (VirtualKeyboardException ex)
        {
            Assert.Ignore("Virtual keyboard backend недоступен — пропускаем: " + ex.Message);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task ElementTypeAsyncUsesPageAssignedKeyboardWithoutCreatingBrowserKeyboard()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed element trusted input test рассчитан на Linux.");

        await using var display = await CreateDisplayOrIgnoreAsync().ConfigureAwait(false);
        await using var keyboard = await CreateKeyboardOrIgnoreAsync(display).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Display = display,
            UseHeadlessMode = true,
        }).ConfigureAwait(false);
        var page = (WebPage)await browser.CurrentWindow.OpenPageAsync(new WebPageSettings
        {
            Keyboard = keyboard,
        }).ConfigureAwait(false);
        var element = new Element(page);

        await element.TypeAsync("abc").ConfigureAwait(false);

        Assert.That(GetCurrentKeyboard(browser), Is.Null);
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task ElementTypeAsyncAcceptsSupportedAsciiSymbolMix()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed element trusted input test рассчитан на Linux.");

        await using var display = await CreateDisplayOrIgnoreAsync().ConfigureAwait(false);
        await using var keyboard = await CreateKeyboardOrIgnoreAsync(display).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Display = display,
            UseHeadlessMode = true,
        }).ConfigureAwait(false);
        var page = (WebPage)await browser.CurrentWindow.OpenPageAsync(new WebPageSettings
        {
            Keyboard = keyboard,
        }).ConfigureAwait(false);
        var element = new Element(page);

        Assert.That(async () => await element.TypeAsync("Ab1_!?/[]{}").ConfigureAwait(false), Throws.Nothing);
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task ElementTypeAsyncRejectsUnsupportedCharacter()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed element trusted input test рассчитан на Linux.");

        await using var display = await CreateDisplayOrIgnoreAsync().ConfigureAwait(false);
        await using var keyboard = await CreateKeyboardOrIgnoreAsync(display).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Display = display,
            UseHeadlessMode = true,
        }).ConfigureAwait(false);
        var page = (WebPage)await browser.CurrentWindow.OpenPageAsync(new WebPageSettings
        {
            Keyboard = keyboard,
        }).ConfigureAwait(false);
        var element = new Element(page);

        Assert.That(async () => await element.TypeAsync("price=10€").ConfigureAwait(false), Throws.TypeOf<NotSupportedException>());
    }

    private static VirtualMouse? GetCurrentMouse(Atom.Net.Browsing.WebDriver.WebBrowser browser)
        => typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetProperty("CurrentMouse", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(browser) as VirtualMouse;

    private static VirtualKeyboard? GetCurrentKeyboard(Atom.Net.Browsing.WebDriver.WebBrowser browser)
        => typeof(Atom.Net.Browsing.WebDriver.WebBrowser).GetProperty("CurrentKeyboard", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(browser) as VirtualKeyboard;

    [SupportedOSPlatform("linux")]
    private static async ValueTask<VirtualDisplay> CreateDisplayOrIgnoreAsync()
    {
        try
        {
            return await VirtualDisplay.CreateAsync().ConfigureAwait(false);
        }
        catch (VirtualDisplayException ex)
        {
            Assert.Ignore("Virtual display backend недоступен — пропускаем: " + ex.Message);
            throw;
        }
    }

    [SupportedOSPlatform("linux")]
    private static async ValueTask<VirtualKeyboard> CreateKeyboardOrIgnoreAsync(VirtualDisplay display)
    {
        try
        {
            return await VirtualKeyboard.CreateForDisplayAsync(display).ConfigureAwait(false);
        }
        catch (VirtualKeyboardException ex)
        {
            Assert.Ignore("Virtual keyboard backend недоступен — пропускаем: " + ex.Message);
            throw;
        }
    }
}