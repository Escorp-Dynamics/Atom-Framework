using System.Reflection;
using System.Runtime.Versioning;
using Atom.Hardware.Display;
using Atom.Hardware.Input;
using WebBrowser = Atom.Net.Browsing.WebDriver.Tests.WebDriverTestEnvironment;

namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
[NonParallelizable]
[Category("Hardware")]
public sealed class WebDriverInputDeviceCascadeTests
{
    [Test]
    public void SettingsExposeMouseAndKeyboardSurfaceAcrossBrowserWindowAndPage()
    {
        AssertSettingsSurface(typeof(WebBrowserSettings));
        AssertSettingsSurface(typeof(WebWindowSettings));
        AssertSettingsSurface(typeof(WebPageSettings));
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task PageFallsBackToBrowserInputDevicesWhenWindowAndPageDoNotOverrideThem()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed input cascade test рассчитан на Linux.");

        await using var display = await CreateDisplayOrIgnoreAsync().ConfigureAwait(false);
        await using var browserDevices = await CreateInputPairOrIgnoreAsync(display).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Display = display,
            UseHeadlessMode = true,
            Mouse = browserDevices.Mouse,
            Keyboard = browserDevices.Keyboard,
        }).ConfigureAwait(false);

        var page = (WebPage)browser.CurrentPage;
        var resolvedMouse = await InvokeValueTaskAsync<VirtualMouse>(page, "ResolveMouseAsync").ConfigureAwait(false);
        var resolvedKeyboard = await InvokeValueTaskAsync<VirtualKeyboard>(page, "ResolveKeyboardAsync").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(resolvedMouse, Is.SameAs(browserDevices.Mouse));
            Assert.That(resolvedKeyboard, Is.SameAs(browserDevices.Keyboard));
        });
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task PagePrefersPageOverridesThenWindowOverridesOverBrowserDevices()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed input cascade test рассчитан на Linux.");

        await using var display = await CreateDisplayOrIgnoreAsync().ConfigureAwait(false);
        await using var browserDevices = await CreateInputPairOrIgnoreAsync(display).ConfigureAwait(false);
        await using var windowDevices = await CreateInputPairOrIgnoreAsync(display).ConfigureAwait(false);
        await using var pageDevices = await CreateInputPairOrIgnoreAsync(display).ConfigureAwait(false);
        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Display = display,
            UseHeadlessMode = true,
            Mouse = browserDevices.Mouse,
            Keyboard = browserDevices.Keyboard,
        }).ConfigureAwait(false);

        var window = (WebWindow)await browser.OpenWindowAsync(new WebWindowSettings
        {
            Mouse = windowDevices.Mouse,
            Keyboard = windowDevices.Keyboard,
        }).ConfigureAwait(false);

        var inheritedPage = (WebPage)await window.OpenPageAsync().ConfigureAwait(false);
        var overriddenPage = (WebPage)await window.OpenPageAsync(new WebPageSettings
        {
            Mouse = pageDevices.Mouse,
            Keyboard = pageDevices.Keyboard,
        }).ConfigureAwait(false);

        var inheritedMouse = await InvokeValueTaskAsync<VirtualMouse>(inheritedPage, "ResolveMouseAsync").ConfigureAwait(false);
        var inheritedKeyboard = await InvokeValueTaskAsync<VirtualKeyboard>(inheritedPage, "ResolveKeyboardAsync").ConfigureAwait(false);
        var overriddenMouse = await InvokeValueTaskAsync<VirtualMouse>(overriddenPage, "ResolveMouseAsync").ConfigureAwait(false);
        var overriddenKeyboard = await InvokeValueTaskAsync<VirtualKeyboard>(overriddenPage, "ResolveKeyboardAsync").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(inheritedMouse, Is.SameAs(windowDevices.Mouse));
            Assert.That(inheritedKeyboard, Is.SameAs(windowDevices.Keyboard));
            Assert.That(overriddenMouse, Is.SameAs(pageDevices.Mouse));
            Assert.That(overriddenKeyboard, Is.SameAs(pageDevices.Keyboard));
        });
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task BrowserAutoCreatesInputDevicesOnFirstResolutionWhenTheyAreNotConfigured()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed input auto-create test рассчитан на Linux.");

        await using var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);

        try
        {
            var resolvedMouse = await InvokeValueTaskAsync<VirtualMouse>(browser, "ResolveMouseAsync").ConfigureAwait(false);
            var resolvedKeyboard = await InvokeValueTaskAsync<VirtualKeyboard>(browser, "ResolveKeyboardAsync").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(resolvedMouse, Is.Not.Null);
                Assert.That(resolvedKeyboard, Is.Not.Null);
            });
        }
        catch (VirtualMouseException ex)
        {
            Assert.Ignore("Virtual mouse backend недоступен — пропускаем: " + ex.Message);
        }
        catch (VirtualKeyboardException ex)
        {
            Assert.Ignore("Virtual keyboard backend недоступен — пропускаем: " + ex.Message);
        }
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task BrowserDisposeDoesNotDisposeExternallyAssignedInputDevices()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed input ownership test рассчитан на Linux.");

        await using var display = await CreateDisplayOrIgnoreAsync().ConfigureAwait(false);
        await using var devices = await CreateInputPairOrIgnoreAsync(display).ConfigureAwait(false);
        var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings
        {
            Display = display,
            UseHeadlessMode = true,
            Mouse = devices.Mouse,
            Keyboard = devices.Keyboard,
        }).ConfigureAwait(false);

        await browser.DisposeAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(IsVirtualMouseDisposed(devices.Mouse), Is.False);
            Assert.That(IsVirtualKeyboardDisposed(devices.Keyboard), Is.False);
        });
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public async Task BrowserDisposeReleasesAutoCreatedInputDevices()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Display-backed input ownership test рассчитан на Linux.");

        var browser = await WebBrowser.LaunchAsync(new WebBrowserSettings()).ConfigureAwait(false);

        try
        {
            var resolvedMouse = await InvokeValueTaskAsync<VirtualMouse>(browser, "ResolveMouseAsync").ConfigureAwait(false);
            var resolvedKeyboard = await InvokeValueTaskAsync<VirtualKeyboard>(browser, "ResolveKeyboardAsync").ConfigureAwait(false);

            await browser.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(IsVirtualMouseDisposed(resolvedMouse), Is.True);
                Assert.That(IsVirtualKeyboardDisposed(resolvedKeyboard), Is.True);
            });
        }
        catch (VirtualMouseException ex)
        {
            Assert.Ignore("Virtual mouse backend недоступен — пропускаем: " + ex.Message);
        }
        catch (VirtualKeyboardException ex)
        {
            Assert.Ignore("Virtual keyboard backend недоступен — пропускаем: " + ex.Message);
        }
    }

    private static void AssertSettingsSurface(Type settingsType)
    {
        var mouseProperty = settingsType.GetProperty("Mouse");
        var keyboardProperty = settingsType.GetProperty("Keyboard");

        Assert.Multiple(() =>
        {
            Assert.That(mouseProperty, Is.Not.Null);
            Assert.That(mouseProperty!.PropertyType, Is.EqualTo(typeof(VirtualMouse)));
            Assert.That(mouseProperty.CanWrite, Is.True);
            Assert.That(keyboardProperty, Is.Not.Null);
            Assert.That(keyboardProperty!.PropertyType, Is.EqualTo(typeof(VirtualKeyboard)));
            Assert.That(keyboardProperty.CanWrite, Is.True);
        });
    }

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
    private static async ValueTask<InputPair> CreateInputPairOrIgnoreAsync(VirtualDisplay display)
    {
        try
        {
            var mouse = await VirtualMouse.CreateForDisplayAsync(display).ConfigureAwait(false);
            try
            {
                var keyboard = await VirtualKeyboard.CreateForDisplayAsync(display).ConfigureAwait(false);
                return new InputPair(mouse, keyboard);
            }
            catch
            {
                await mouse.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (VirtualMouseException ex)
        {
            Assert.Ignore("Virtual mouse backend недоступен — пропускаем: " + ex.Message);
            throw;
        }
        catch (VirtualKeyboardException ex)
        {
            Assert.Ignore("Virtual keyboard backend недоступен — пропускаем: " + ex.Message);
            throw;
        }
    }

    private static async ValueTask<T> InvokeValueTaskAsync<T>(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, [typeof(CancellationToken)]);
        Assert.That(method, Is.Not.Null, $"Не найден internal method {methodName}(CancellationToken) on {target.GetType().Name}.");

        var result = method!.Invoke(target, [CancellationToken.None]);
        return result switch
        {
            ValueTask<T> valueTask => await valueTask.ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Method {target.GetType().Name}.{methodName} did not return ValueTask<{typeof(T).Name}>."),
        };
    }

    private static bool IsVirtualMouseDisposed(VirtualMouse mouse)
        => typeof(VirtualMouse).GetField("isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mouse) as bool? == true;

    private static bool IsVirtualKeyboardDisposed(VirtualKeyboard keyboard)
        => typeof(VirtualKeyboard).GetField("isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(keyboard) as bool? == true;

    private sealed record InputPair(VirtualMouse Mouse, VirtualKeyboard Keyboard) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Keyboard.DisposeAsync().ConfigureAwait(false);
            await Mouse.DisposeAsync().ConfigureAwait(false);
        }
    }
}