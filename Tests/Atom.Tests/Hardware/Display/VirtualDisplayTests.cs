using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using Atom.Hardware.Display;

namespace Atom.Tests;

[TestFixture]
[Category("Hardware")]
[SupportedOSPlatform("linux")]
public class VirtualDisplayTests
{
    private static bool IsDisplayBackendUnavailable(VirtualDisplayException ex) =>
        ex.Message.Contains("xpra", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("X-сервер", StringComparison.OrdinalIgnoreCase);

    [Test(Description = "Создание виртуального дисплея с настройками по умолчанию")]
    public async Task CreateDefaultShouldStartXpraSession()
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
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
            return;
        }

        await using (display.ConfigureAwait(false))
        {
            Assert.That(display.Display, Does.StartWith(":"));
            Assert.That(display.DisplayNumber, Is.GreaterThanOrEqualTo(0));
            Assert.That(display.Resolution, Is.EqualTo(new Size(1920, 1080)));
        }
    }

    [Test(Description = "Создание виртуального дисплея с пользовательскими настройками")]
    public async Task CreateWithCustomSettingsShouldRespectValues()
    {
        var settings = new VirtualDisplaySettings
        {
            Resolution = new Size(1280, 720),
            ColorDepth = 24,
        };

        VirtualDisplay display;

        try
        {
            display = await VirtualDisplay.CreateAsync(settings);
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем.");
            return;
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
            return;
        }

        await using (display.ConfigureAwait(false))
        {
            Assert.That(display.Resolution, Is.EqualTo(new Size(1280, 720)));
            Assert.That(display.Settings.ColorDepth, Is.EqualTo(24));
        }
    }

    [Test(Description = "X11-сокет существует после создания дисплея")]
    public async Task CreatedDisplayShouldHaveSocket()
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
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
            return;
        }

        await using (display.ConfigureAwait(false))
        {
            var socketPath = "/tmp/.X11-unix/X" + display.DisplayNumber;
            Assert.That(File.Exists(socketPath), Is.True,
                "X11-сокет " + socketPath + " должен существовать.");
        }
    }

    [Test(Description = "После Dispose сокет удалён и процесс завершён")]
    public async Task DisposeShouldCleanupResources()
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
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
            return;
        }

        var number = display.DisplayNumber;
        await display.DisposeAsync().ConfigureAwait(false);

        // Даём время на очистку.
        await Task.Delay(200);

        var lockFile = "/tmp/.X" + number + "-lock";
        Assert.That(File.Exists(lockFile), Is.False,
            "Lock-файл " + lockFile + " должен быть удалён после dispose.");
        AssertManagedDisplayProcessesCleared(number, "После dispose не должно оставаться живых display-процессов.");
    }

    [Test(Description = "Повторный Dispose безопасен (идемпотентность)")]
    public async Task DoubleDisposeShouldNotThrow()
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
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
            return;
        }

        await display.DisposeAsync().ConfigureAwait(false);
        await display.DisposeAsync().ConfigureAwait(false);
    }

    [Test(Description = "Stress: несколько циклов create/dispose не оставляют lock/socket хвосты")]
    public async Task RepeatedCreateDisposeShouldNotLeaveArtifacts()
    {
        for (var i = 0; i < 3; i++)
        {
            VirtualDisplay display;

            try
            {
                display = await VirtualDisplay.CreateAsync(new VirtualDisplaySettings
                {
                    Resolution = new Size(1024, 768),
                });
            }
            catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
            {
                Assert.Ignore("Display backend недоступен — пропускаем: " + ex.Message);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
                return;
            }

            var displayNumber = display.DisplayNumber;
            var socketPath = "/tmp/.X11-unix/X" + displayNumber;
            var lockFile = "/tmp/.X" + displayNumber + "-lock";

            await using (display.ConfigureAwait(false))
            {
                Assert.That(File.Exists(socketPath), Is.True,
                    "X11-сокет должен существовать внутри активного цикла create/dispose.");
            }

            await Task.Delay(200);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(lockFile), Is.False,
                    "Lock-файл не должен оставаться после dispose цикла #" + i + ".");
                Assert.That(File.Exists(socketPath), Is.False,
                    "X11-сокет не должен оставаться после dispose цикла #" + i + ".");
            });

            AssertManagedDisplayProcessesCleared(displayNumber,
                "После dispose цикла #" + i + " не должно оставаться живых display-процессов.");
        }
    }

    [Test(Description = "Валидация: null settings")]
    public void CreateWithNullSettingsShouldThrow() =>
        Assert.ThrowsAsync<ArgumentNullException>(
            async () => await VirtualDisplay.CreateAsync(null!));

    [Test(Description = "Валидация: Resolution.Width < 1")]
    public void CreateWithInvalidWidthShouldThrow()
    {
        var settings = new VirtualDisplaySettings { Resolution = new Size(0, 1080) };
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await VirtualDisplay.CreateAsync(settings));
    }

    [Test(Description = "Валидация: Resolution.Height < 1")]
    public void CreateWithInvalidHeightShouldThrow()
    {
        var settings = new VirtualDisplaySettings { Resolution = new Size(1920, 0) };
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await VirtualDisplay.CreateAsync(settings));
    }

    [Test(Description = "Валидация: некорректный ColorDepth")]
    public void CreateWithInvalidColorDepthShouldThrow()
    {
        var settings = new VirtualDisplaySettings { ColorDepth = 15 };
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await VirtualDisplay.CreateAsync(settings));
    }

    [Test(Description = "Валидация: DisplayNumber отрицательный")]
    public void CreateWithNegativeDisplayNumberShouldThrow()
    {
        var settings = new VirtualDisplaySettings { DisplayNumber = -1 };
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await VirtualDisplay.CreateAsync(settings));
    }

    [Test(Description = "Внутренняя stale-проверка не считает отсутствующий lock-файл stale")]
    public void MissingLockFileShouldNotBeTreatedAsStale()
    {
        var method = typeof(VirtualDisplay).GetMethod(
            "IsStaleDisplay",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "Не удалось найти private static IsStaleDisplay.");

        var tempLockFile = Path.Combine(
            Path.GetTempPath(),
            "atom-missing-lock-" + Guid.NewGuid().ToString("N") + ".lock");

        var result = (bool)method!.Invoke(obj: null, [tempLockFile])!;
        Assert.That(result, Is.False,
            "Отсутствующий lock-файл не должен считаться stale-дисплеем автоматически.");
    }

    [Test(Description = "Внутренний парсер распознаёт осиротевший Xvfb-for-Xpra display")]
    public void ManagedDisplayParserShouldRecognizeOrphanedXvfbCommandLine()
    {
        var method = typeof(VirtualDisplay).GetMethod(
            "TryGetManagedDisplayNumber",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "Не удалось найти private static TryGetManagedDisplayNumber.");

        var arguments = new object?[] { "Xvfb-for-Xpra-100 -screen 0 1920x1080x24 -ac -nolisten tcp +extension XTEST", -1 };
        var result = (bool)method!.Invoke(obj: null, arguments)!;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True, "Осиротевший Xvfb-for-Xpra display должен распознаваться как занятый слот.");
            Assert.That(arguments[1], Is.EqualTo(100));
        });
    }

    [Test(Description = "Внутренний парсер распознаёт headless Xvfb display")]
    public void ManagedDisplayParserShouldRecognizeHeadlessXvfbCommandLine()
    {
        var method = typeof(VirtualDisplay).GetMethod(
            "TryGetManagedDisplayNumber",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "Не удалось найти private static TryGetManagedDisplayNumber.");

        var arguments = new object?[] { "Xvfb\0:101\0-screen\00\01920x1080x24\0-ac\0-nolisten\0tcp\0+extension\0XTEST", -1 };
        var result = (bool)method!.Invoke(obj: null, arguments)!;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True, "Headless Xvfb display должен распознаваться как занятый слот.");
            Assert.That(arguments[1], Is.EqualTo(101));
        });
    }

    [Test(Description = "Внутренний парсер распознаёт xpra start/attach display")]
    public void ManagedDisplayParserShouldRecognizeXpraCommandLine()
    {
        var method = typeof(VirtualDisplay).GetMethod(
            "TryGetManagedDisplayNumber",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "Не удалось найти private static TryGetManagedDisplayNumber.");

        var startArguments = new object?[] { "xpra\0start\0:120\0--daemon=no\0--attach=no", -1 };
        var startResult = (bool)method!.Invoke(obj: null, startArguments)!;

        var attachArguments = new object?[] { "xpra attach :121 --opengl=auto", -1 };
        var attachResult = (bool)method.Invoke(obj: null, attachArguments)!;

        Assert.Multiple(() =>
        {
            Assert.That(startResult, Is.True, "xpra start должен распознаваться как занятый display.");
            Assert.That(startArguments[1], Is.EqualTo(120));
            Assert.That(attachResult, Is.True, "xpra attach должен распознаваться как занятый display.");
            Assert.That(attachArguments[1], Is.EqualTo(121));
        });
    }

    [Test(Description = "xpra attach: создание видимого rootless-дисплея")]
    public async Task CreateVisibleShouldStartXpraAttach()
    {
        var settings = new VirtualDisplaySettings
        {
            Resolution = new Size(800, 600),
            IsVisible = true,
        };

        VirtualDisplay display;

        try
        {
            display = await VirtualDisplay.CreateAsync(settings);
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем: " + ex.Message);
            return;
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
            return;
        }

        await using (display.ConfigureAwait(false))
        {
            Assert.That(display.Display, Does.StartWith(":"));
            Assert.That(display.DisplayNumber, Is.GreaterThanOrEqualTo(0));
            Assert.That(display.Resolution, Is.EqualTo(new Size(800, 600)));
            Assert.That(display.Settings.IsVisible, Is.True);
        }
    }

    [Test(Description = "Видимая xpra-сессия создаёт X11-сокет")]
    public async Task VisibleDisplayShouldHaveSocket()
    {
        var settings = new VirtualDisplaySettings { IsVisible = true };

        VirtualDisplay display;

        try
        {
            display = await VirtualDisplay.CreateAsync(settings);
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем.");
            return;
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
            return;
        }

        await using (display.ConfigureAwait(false))
        {
            var socketPath = "/tmp/.X11-unix/X" + display.DisplayNumber;
            Assert.That(File.Exists(socketPath), Is.True,
                "X11-сокет " + socketPath + " должен существовать для xpra-сессии.");
        }
    }

    [Test(Description = "Видимая xpra-сессия очищает ресурсы после Dispose")]
    public async Task VisibleDisposeShouldCleanup()
    {
        var settings = new VirtualDisplaySettings { IsVisible = true };

        VirtualDisplay display;

        try
        {
            display = await VirtualDisplay.CreateAsync(settings);
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем.");
            return;
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
            return;
        }

        var number = display.DisplayNumber;
        await display.DisposeAsync().ConfigureAwait(false);

        await Task.Delay(200);

        var lockFile = "/tmp/.X" + number + "-lock";
        Assert.That(File.Exists(lockFile), Is.False,
            "Lock-файл " + lockFile + " должен быть удалён после dispose xpra-сессии.");
        AssertManagedDisplayProcessesCleared(number, "После dispose видимого дисплея не должно оставаться живых display-процессов.");
    }

    [Test(Description = "Невидимая Xvfb-сессия очищает процессы после Dispose")]
    public async Task HiddenDisposeShouldCleanupManagedProcesses()
    {
        var settings = new VirtualDisplaySettings { IsVisible = false };

        VirtualDisplay display;

        try
        {
            display = await VirtualDisplay.CreateAsync(settings);
        }
        catch (VirtualDisplayException ex) when (IsDisplayBackendUnavailable(ex))
        {
            Assert.Ignore("Display backend недоступен — пропускаем.");
            return;
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("VirtualDisplay поддерживается только на Linux.");
            return;
        }

        var number = display.DisplayNumber;
        await display.DisposeAsync().ConfigureAwait(false);

        await Task.Delay(200);

        var lockFile = "/tmp/.X" + number + "-lock";
        Assert.That(File.Exists(lockFile), Is.False,
            "Lock-файл " + lockFile + " должен быть удалён после dispose headless-сеанса.");
        AssertManagedDisplayProcessesCleared(number, "После dispose headless-сеанса не должно оставаться живых display-процессов.");
    }

    private static void AssertManagedDisplayProcessesCleared(int displayNumber, string message)
    {
        var remainingProcessIds = WaitForManagedDisplayProcessesToExit(displayNumber, TimeSpan.FromSeconds(2));
        Assert.That(remainingProcessIds, Is.Empty, message + " Остались PID: " + string.Join(",", remainingProcessIds));
    }

    private static HashSet<int> WaitForManagedDisplayProcessesToExit(int displayNumber, TimeSpan timeout)
    {
        var startTime = Stopwatch.GetTimestamp();

        while (true)
        {
            var remainingProcessIds = CaptureManagedDisplayProcessIds(displayNumber);
            if (remainingProcessIds.Count == 0)
                return remainingProcessIds;

            if (Stopwatch.GetElapsedTime(startTime) >= timeout)
                return remainingProcessIds;

            Thread.Sleep(100);
        }
    }

    private static HashSet<int> CaptureManagedDisplayProcessIds(int displayNumber)
    {
        var method = typeof(VirtualDisplay).GetMethod(
            "CaptureDisplayProcessIds",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "Не удалось найти private static CaptureDisplayProcessIds.");

        return new HashSet<int>((HashSet<int>)method!.Invoke(obj: null, [displayNumber])!);
    }

}
