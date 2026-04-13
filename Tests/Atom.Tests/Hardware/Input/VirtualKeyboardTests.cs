using System.Runtime.Versioning;
using Atom.Hardware.Display;
using Atom.Hardware.Input;

namespace Atom.Tests;

[TestFixture]
[Category("Hardware")]
[Explicit("Используют /dev/uinput — запуск только вручную.")]
public class VirtualKeyboardTests
{
    private static bool IsDisplayBackendUnavailable(VirtualDisplayException ex) =>
        ex.Message.Contains("xpra", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("X-сервер", StringComparison.OrdinalIgnoreCase);

    [Test(Description = "Создание виртуальной клавиатуры с настройками по умолчанию")]
    public async Task CreateDefaultShouldCreateDevice()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем: " + ex.Message);
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.That(keyboard.DeviceIdentifier, Does.StartWith("uinput:"));
        }
    }

    [Test(Description = "Создание с пользовательскими настройками")]
    public async Task CreateWithCustomSettingsShouldRespectValues()
    {
        var settings = new VirtualKeyboardSettings { Name = "Test Keyboard" };

        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateAsync(settings);
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.That(keyboard.Settings.Name, Is.EqualTo("Test Keyboard"));
            Assert.That(keyboard.DeviceIdentifier, Is.EqualTo("uinput:Test Keyboard"));
        }
    }

    [Test(Description = "KeyDown/KeyUp для одиночной клавиши")]
    public async Task KeyDownUpShouldNotThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() =>
            {
                keyboard.KeyDown(ConsoleKey.A);
                keyboard.KeyUp(ConsoleKey.A);
            });
        }
    }

    [Test(Description = "Синхронный KeyPress для буквенных клавиш")]
    public async Task KeyPressLettersShouldNotThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.A));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.Z));
        }
    }

    [Test(Description = "Синхронный KeyPress для цифровых клавиш")]
    public async Task KeyPressDigitsShouldNotThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.D0));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.D9));
        }
    }

    [Test(Description = "KeyPress для функциональных клавиш")]
    public async Task KeyPressFunctionKeysShouldNotThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.F1));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.F12));
        }
    }

    [Test(Description = "KeyPress для клавиш навигации")]
    public async Task KeyPressNavigationKeysShouldNotThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.LeftArrow));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.RightArrow));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.UpArrow));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.DownArrow));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.Home));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.End));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.PageUp));
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.PageDown));
        }
    }

    [Test(Description = "KeyPress с модификатором Ctrl")]
    public async Task KeyPressWithCtrlShouldNotThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.C, ConsoleModifiers.Control));
        }
    }

    [Test(Description = "KeyPress с модификатором Shift")]
    public async Task KeyPressWithShiftShouldNotThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.A, ConsoleModifiers.Shift));
        }
    }

    [Test(Description = "KeyPress с модификатором Alt")]
    public async Task KeyPressWithAltShouldNotThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.F4, ConsoleModifiers.Alt));
        }
    }

    [Test(Description = "KeyPress с комбинацией модификаторов Ctrl+Shift")]
    public async Task KeyPressWithCtrlShiftShouldNotThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.DoesNotThrow(() => keyboard.KeyPress(
                ConsoleKey.Escape,
                ConsoleModifiers.Control | ConsoleModifiers.Shift));
        }
    }

    [Test(Description = "KeyPressAsync с реалистичными задержками")]
    public async Task KeyPressAsyncShouldCompleteWithDelay()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            await keyboard.KeyPressAsync(ConsoleKey.Enter);
        }
    }

    [Test(Description = "KeyPressAsync с модификаторами")]
    public async Task KeyPressAsyncWithModifiersShouldComplete()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            await keyboard.KeyPressAsync(ConsoleKey.V, ConsoleModifiers.Control);
        }
    }

    [Test(Description = "После Dispose операции выбрасывают ObjectDisposedException")]
    public async Task DisposedKeyboardShouldThrowObjectDisposed()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await keyboard.DisposeAsync().ConfigureAwait(false);

        Assert.Throws<ObjectDisposedException>(() => keyboard.KeyDown(ConsoleKey.A));
        Assert.Throws<ObjectDisposedException>(() => keyboard.KeyPress(ConsoleKey.A));
    }

    [Test(Description = "Валидация настроек: null settings")]
    public void CreateWithNullSettingsShouldThrow() =>
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await VirtualKeyboard.CreateAsync(null!));

    [Test(Description = "Неподдерживаемая клавиша выбрасывает исключение")]
    public async Task KeyPressUnsupportedKeyShouldThrow()
    {
        VirtualKeyboard keyboard;

        try
        {
            keyboard = await VirtualKeyboard.CreateDefaultAsync();
        }
        catch (VirtualKeyboardException ex) when (ex.Message.Contains("/dev/uinput"))
        {
            Assert.Ignore("Нет доступа к /dev/uinput — пропускаем.");
            return;
        }

        await using (keyboard.ConfigureAwait(false))
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => keyboard.KeyPress((ConsoleKey)9999));
        }
    }

    [Test(Description = "XTEST KeyPressAsync с отменой не ломает последующий ввод")]
    [SupportedOSPlatform("linux")]
    public async Task XTestCanceledKeyPressAsyncShouldAllowSubsequentInput()
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
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(1);

                Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await keyboard.KeyPressAsync(
                        ConsoleKey.V,
                        ConsoleModifiers.Control,
                        cts.Token));

                Assert.DoesNotThrow(() => keyboard.KeyPress(ConsoleKey.A));
            }
        }
    }
}
