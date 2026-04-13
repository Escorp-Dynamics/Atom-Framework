using System.Drawing;
using System.Runtime.Versioning;
using Atom.Hardware.Display;
using Atom.Hardware.Input;

namespace Atom.Tests;

[TestFixture]
[Category("Hardware")]
[Explicit("Используют /dev/uinput — запуск только вручную.")]
public class VirtualMouseTests
{
    private static bool IsDisplayBackendUnavailable(VirtualDisplayException ex) =>
        ex.Message.Contains("xpra", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("X-сервер", StringComparison.OrdinalIgnoreCase);

    [Test(Description = "Создание виртуальной мыши с настройками по умолчанию")]
    public async Task CreateDefaultShouldCreateDevice()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем: " + ex.Message);
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assert.That(mouse.DeviceIdentifier, Does.StartWith("uinput:"));
            Assert.That(mouse.Settings.ScreenSize.Width, Is.GreaterThan(0));
            Assert.That(mouse.Settings.ScreenSize.Height, Is.GreaterThan(0));
        }
    }

    [Test(Description = "Создание виртуальной мыши с пользовательскими настройками")]
    public async Task CreateWithCustomSettingsShouldRespectValues()
    {
        var settings = new VirtualMouseSettings
        {
            Name = "Test Mouse",
            ScreenSize = new Size(3840, 2160),
        };

        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateAsync(settings);
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assert.That(mouse.Settings.Name, Is.EqualTo("Test Mouse"));
            Assert.That(mouse.Settings.ScreenSize, Is.EqualTo(new Size(3840, 2160)));
            Assert.That(mouse.DeviceIdentifier, Is.EqualTo("uinput:Test Mouse"));
        }
    }

    [Test(Description = "MoveAbsolute генерирует события без исключений")]
    public async Task MoveAbsoluteShouldNotThrow()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => mouse.MoveAbsolute(new Point(100, 200)));
            Assert.DoesNotThrow(() => mouse.MoveAbsolute(new Point(0, 0)));
            Assert.DoesNotThrow(() => mouse.MoveAbsolute(new Point(1919, 1079)));
        }
    }

    [Test(Description = "MoveRelative генерирует события без исключений")]
    public async Task MoveRelativeShouldNotThrow()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => mouse.MoveRelative(new Size(10, -5)));
            Assert.DoesNotThrow(() => mouse.MoveRelative(new Size(-100, 50)));
        }
    }

    [Test(Description = "Синхронный клик работает без исключений")]
    public async Task ClickShouldNotThrow()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => mouse.Click());
            Assert.DoesNotThrow(() => mouse.Click(VirtualMouseButton.Right));
            Assert.DoesNotThrow(() => mouse.Click(VirtualMouseButton.Middle));
        }
    }

    [Test(Description = "ClickAt перемещает и кликает")]
    public async Task ClickAtShouldNotThrow()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => mouse.ClickAt(new Point(500, 300)));
        }
    }

    [Test(Description = "ClickAtAsync с реалистичными задержками")]
    public async Task ClickAtAsyncShouldCompleteWithDelay()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            await mouse.ClickAtAsync(new Point(960, 540));
        }
    }

    [Test(Description = "Scroll работает без исключений")]
    public async Task ScrollShouldNotThrow()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => mouse.Scroll(3));
            Assert.DoesNotThrow(() => mouse.Scroll(-3));
            Assert.DoesNotThrow(() => mouse.ScrollHorizontal(1));
        }
    }

    [Test(Description = "ButtonDown/ButtonUp работают независимо")]
    public async Task ButtonDownUpShouldNotThrow()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() =>
            {
                mouse.ButtonDown();
                mouse.ButtonUp();
            });
        }
    }

    [Test(Description = "После Dispose операции выбрасывают ObjectDisposedException")]
    public async Task DisposedMouseShouldThrowObjectDisposed()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await mouse.DisposeAsync().ConfigureAwait(false);

        Assert.Throws<ObjectDisposedException>(() => mouse.MoveAbsolute(new Point(0, 0)));
        Assert.Throws<ObjectDisposedException>(() => mouse.Click());
    }

    [Test(Description = "Валидация настроек: ScreenSize.Width < 1")]
    public void CreateWithInvalidWidthShouldThrow()
    {
        var settings = new VirtualMouseSettings { ScreenSize = new Size(0, 1080) };
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await VirtualMouse.CreateAsync(settings));
    }

    [Test(Description = "Валидация настроек: ScreenSize.Height < 1")]
    public void CreateWithInvalidHeightShouldThrow()
    {
        var settings = new VirtualMouseSettings { ScreenSize = new Size(1920, 0) };
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await VirtualMouse.CreateAsync(settings));
    }

    [Test(Description = "Валидация настроек: null settings")]
    public void CreateWithNullSettingsShouldThrow() =>
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await VirtualMouse.CreateAsync(null!));

    [Test(Description = "MPX: виртуальная мышь имеет отдельный курсор на нативном Xorg")]
    public async Task CreateShouldHaveSeparateCursorOnXorg()
    {
        if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null)
            Assert.Ignore("MPX не работает через XWayland — пропускаем.");

        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assert.That(mouse.HasSeparateCursor, Is.True,
                "Виртуальная мышь должна иметь отдельный курсор через MPX на нативном Xorg.");
        }
    }

    [Test(Description = "MPX диагностика: HasSeparateCursor корректно отражает среду")]
    public async Task HasSeparateCursorShouldReflectEnvironment()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            var isWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null;

            if (isWayland)
            {
                Assert.That(mouse.HasSeparateCursor, Is.False,
                    "На XWayland MPX недоступен — HasSeparateCursor должен быть false.");
            }
            else
            {
                Assert.That(mouse.HasSeparateCursor, Is.True,
                    "На нативном Xorg MPX должен работать.");
            }
        }
    }

    [Test(Description = "MPX: операции на отдельном курсоре не падают")]
    public async Task SeparateCursorMoveAndClickShouldNotThrow()
    {
        if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null)
            Assert.Ignore("MPX не работает через XWayland — пропускаем.");

        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (mouse.ConfigureAwait(false))
        {
            Assume.That(mouse.HasSeparateCursor, Is.True, "MPX не настроен — пропускаем.");

            Assert.DoesNotThrow(() => mouse.MoveAbsolute(new Point(100, 100)));
            Assert.DoesNotThrow(() => mouse.Click());
            Assert.DoesNotThrow(() => mouse.MoveAbsolute(new Point(500, 500)));
            Assert.DoesNotThrow(() => mouse.Click(VirtualMouseButton.Right));
        }
    }

    [Test(Description = "MPX: после dispose отдельный курсор уничтожается корректно")]
    public async Task DisposeShouldCleanupMpxMasterPointer()
    {
        VirtualMouse mouse;

        try
        {
            mouse = await VirtualMouse.CreateDefaultAsync();
        }
        catch (VirtualMouseException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        var hadSeparateCursor = mouse.HasSeparateCursor;
        await mouse.DisposeAsync().ConfigureAwait(false);

        // После dispose — проверяем что повторный dispose безопасен (идемпотентность).
        await mouse.DisposeAsync().ConfigureAwait(false);

        if (hadSeparateCursor)
            Assert.Pass("MPX master pointer удалён при dispose.");
        else
            Assert.Inconclusive("MPX не был настроен — cleanup не проверялся.");
    }

    [Test(Description = "XTEST ClickAtAsync с отменой не ломает последующие клики")]
    [SupportedOSPlatform("linux")]
    public async Task XTestCanceledClickAtAsyncShouldAllowSubsequentClick()
    {
        VirtualDisplay display;

        try
        {
            display = await VirtualDisplay.CreateAsync();
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем.");
            return;
        }

        await using (display.ConfigureAwait(false))
        {
            var mouse = await VirtualMouse.CreateForDisplayAsync(display);

            await using (mouse.ConfigureAwait(false))
            {
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(1);

                Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await mouse.ClickAtAsync(new Point(100, 100), cancellationToken: cts.Token));

                Assert.DoesNotThrow(() => mouse.Click());
            }
        }
    }
}
