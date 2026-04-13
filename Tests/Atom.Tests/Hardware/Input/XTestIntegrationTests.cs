using System.Drawing;
using System.Runtime.Versioning;
using Atom.Hardware.Display;
using Atom.Hardware.Input;

namespace Atom.Tests;

[TestFixture]
[Category("Hardware")]
[SupportedOSPlatform("linux")]
public class XTestIntegrationTests
{
    private static bool IsDisplayBackendUnavailable(VirtualDisplayException ex) =>
        ex.Message.Contains("xpra", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("X-сервер", StringComparison.OrdinalIgnoreCase);

    [Test(Description = "Создание виртуальной мыши на виртуальном дисплее через XTEST")]
    public async Task CreateMouseForDisplayShouldWork()
    {
        VirtualDisplay display;

        try
        {
            display = await VirtualDisplay.CreateAsync();
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем: " + ex.Message);
            return;
        }

        await using (display.ConfigureAwait(false))
        {
            var mouse = await VirtualMouse.CreateForDisplayAsync(display);

            await using (mouse.ConfigureAwait(false))
            {
                Assert.That(mouse.HasSeparateCursor, Is.False,
                    "XTEST не создаёт отдельный MPX-курсор; он лишь инжектит события в целевой X-сервер.");
                Assert.That(mouse.DeviceIdentifier, Does.Contain("xtest"));
            }
        }
    }

    [Test(Description = "Мышь XTEST: MoveAbsolute на виртуальном дисплее")]
    public async Task XTestMouseMoveAbsoluteShouldNotThrow()
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
                Assert.DoesNotThrow(() => mouse.MoveAbsolute(new Point(100, 200)));
                Assert.DoesNotThrow(() => mouse.MoveAbsolute(new Point(0, 0)));
                Assert.DoesNotThrow(() => mouse.MoveAbsolute(new Point(1919, 1079)));
            }
        }
    }

    [Test(Description = "Мышь XTEST: MoveRelative на виртуальном дисплее")]
    public async Task XTestMouseMoveRelativeShouldNotThrow()
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
                Assert.DoesNotThrow(() => mouse.MoveRelative(new Size(10, -5)));
                Assert.DoesNotThrow(() => mouse.MoveRelative(new Size(-100, 50)));
            }
        }
    }

    [Test(Description = "Мышь XTEST: Click на виртуальном дисплее")]
    public async Task XTestMouseClickShouldNotThrow()
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
                Assert.DoesNotThrow(() => mouse.Click());
                Assert.DoesNotThrow(() => mouse.Click(VirtualMouseButton.Right));
                Assert.DoesNotThrow(() => mouse.Click(VirtualMouseButton.Middle));
            }
        }
    }

    [Test(Description = "Мышь XTEST: Scroll на виртуальном дисплее")]
    public async Task XTestMouseScrollShouldNotThrow()
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
                Assert.DoesNotThrow(() => mouse.Scroll(3));
                Assert.DoesNotThrow(() => mouse.Scroll(-3));
                Assert.DoesNotThrow(() => mouse.ScrollHorizontal(1));
            }
        }
    }

    [Test(Description = "Мышь XTEST: ClickAt на виртуальном дисплее")]
    public async Task XTestMouseClickAtShouldNotThrow()
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
                Assert.DoesNotThrow(() => mouse.ClickAt(new Point(500, 300)));
            }
        }
    }

    [Test(Description = "Мышь XTEST: пользовательские настройки")]
    public async Task XTestMouseWithCustomSettingsShouldWork()
    {
        var displaySettings = new VirtualDisplaySettings
        {
            Resolution = new Size(1280, 720),
        };

        VirtualDisplay display;

        try
        {
            display = await VirtualDisplay.CreateAsync(displaySettings);
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем.");
            return;
        }

        await using (display.ConfigureAwait(false))
        {
            var mouseSettings = new VirtualMouseSettings
            {
                ScreenSize = new Size(1280, 720),
            };

            var mouse = await VirtualMouse.CreateForDisplayAsync(display, mouseSettings);

            await using (mouse.ConfigureAwait(false))
            {
                Assert.That(mouse.Settings.ScreenSize, Is.EqualTo(new Size(1280, 720)));
                Assert.DoesNotThrow(() => mouse.MoveAbsolute(new Point(640, 360)));
            }
        }
    }

    [Test(Description = "Клавиатура XTEST: создание на виртуальном дисплее")]
    public async Task CreateKeyboardForDisplayShouldWork()
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
            var keyboard = await VirtualKeyboard.CreateForDisplayAsync(display);

            await using (keyboard.ConfigureAwait(false))
            {
                Assert.That(keyboard.DeviceIdentifier, Does.Contain("xtest"));
            }
        }
    }

    [Test(Description = "Клавиатура XTEST: KeyDown/KeyUp на виртуальном дисплее")]
    public async Task XTestKeyboardKeyDownUpShouldNotThrow()
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
            var keyboard = await VirtualKeyboard.CreateForDisplayAsync(display);

            await using (keyboard.ConfigureAwait(false))
            {
                Assert.DoesNotThrow(() =>
                {
                    keyboard.KeyDown(ConsoleKey.A);
                    keyboard.KeyUp(ConsoleKey.A);
                });
            }
        }
    }

    [Test(Description = "Клавиатура XTEST: KeyPress для букв")]
    public async Task XTestKeyboardKeyPressLettersShouldNotThrow()
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
            var keyboard = await VirtualKeyboard.CreateForDisplayAsync(display);

            await using (keyboard.ConfigureAwait(false))
            {
                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.H));
                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.E));
                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.L));
                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.L));
                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.O));
            }
        }
    }

    [Test(Description = "Клавиатура XTEST: модификаторы (Shift, Ctrl, Alt)")]
    public async Task XTestKeyboardModifiersShouldNotThrow()
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
            var keyboard = await VirtualKeyboard.CreateForDisplayAsync(display);

            await using (keyboard.ConfigureAwait(false))
            {
                Assert.DoesNotThrow(() =>
                    keyboard.KeyPress(ConsoleKey.A, ConsoleModifiers.Shift));
                Assert.DoesNotThrow(() =>
                    keyboard.KeyPress(ConsoleKey.C, ConsoleModifiers.Control));
                Assert.DoesNotThrow(() =>
                    keyboard.KeyPress(ConsoleKey.V, ConsoleModifiers.Control));
            }
        }
    }

    [Test(Description = "XTEST: расширенные функциональные и meta-клавиши поддерживаются")]
    public async Task XTestKeyboardExtendedFunctionAndMetaKeysShouldNotThrow()
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
            var keyboard = await VirtualKeyboard.CreateForDisplayAsync(display);

            await using (keyboard.ConfigureAwait(false))
            {
                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.F13));
                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.F24));
                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.LeftWindows));
                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.RightWindows));
            }
        }
    }

    [Test(Description = "XTEST: мышь и клавиатура одновременно на одном дисплее")]
    public async Task MouseAndKeyboardOnSameDisplayShouldWork()
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
                var keyboard = await VirtualKeyboard.CreateForDisplayAsync(display);

                await using (keyboard.ConfigureAwait(false))
                {
                    Assert.DoesNotThrow(() =>
                    {
                        mouse.MoveAbsolute(new Point(500, 300));
                        mouse.Click();
                        keyboard.KeyPress(ConsoleKey.A);
                        mouse.MoveRelative(new Size(10, 10));
                        keyboard.KeyPress(ConsoleKey.Enter);
                    });
                }
            }
        }
    }

    [Test(Description = "XTEST: null display бросает ArgumentNullException")]
    public void CreateMouseForNullDisplayShouldThrow() =>
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await VirtualMouse.CreateForDisplayAsync(null!));

    [Test(Description = "XTEST: null display для клавиатуры бросает ArgumentNullException")]
    public void CreateKeyboardForNullDisplayShouldThrow() =>
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await VirtualKeyboard.CreateForDisplayAsync(null!));

    [Test(Description = "XTEST: после Dispose мыши операции бросают ObjectDisposedException")]
    public async Task DisposedXTestMouseShouldThrow()
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
            await mouse.DisposeAsync().ConfigureAwait(false);

            Assert.Throws<ObjectDisposedException>(() => mouse.MoveAbsolute(new Point(0, 0)));
            Assert.Throws<ObjectDisposedException>(() => mouse.Click());
        }
    }

    [Test(Description = "XTEST: после Dispose клавиатуры операции бросают ObjectDisposedException")]
    public async Task DisposedXTestKeyboardShouldThrow()
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
            var keyboard = await VirtualKeyboard.CreateForDisplayAsync(display);
            await keyboard.DisposeAsync().ConfigureAwait(false);

            Assert.Throws<ObjectDisposedException>(() => keyboard.KeyPress(ConsoleKey.A));
        }
    }

    [Test(Description = "XTEST: неподдержанная клавиша бросает исключение вместо тихого игнорирования")]
    public async Task XTestKeyboardUnsupportedKeyShouldThrow()
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
            var keyboard = await VirtualKeyboard.CreateForDisplayAsync(display);

            await using (keyboard.ConfigureAwait(false))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => keyboard.KeyPress((ConsoleKey)9999));
            }
        }
    }
}
