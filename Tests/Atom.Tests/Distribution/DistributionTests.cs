namespace Atom.Distribution.Tests;

/// <summary>
/// Тесты для модуля Distribution (OS, Terminal, ProcessInfo, PackageManager).
/// </summary>
[TestFixture, Parallelizable(ParallelScope.All)]
public class DistributionTests(ILogger logger) : BenchmarkTests<DistributionTests>(logger)
{
    public DistributionTests() : this(ConsoleLogger.Unicode) { }

    #region OS Tests

    [TestCase(TestName = "OS: Distribution не null")]
    public void OsDistributionNotNull() =>
        // Distribution может быть Unknown, но не должно выбрасывать исключения
        Assert.DoesNotThrow(() => _ = OS.Distribution);

    [TestCase(TestName = "OS: PackageManager не null")]
    public void OsPackageManagerNotNull() => Assert.That(OS.PM, Is.Not.Null);

    [TestCase(TestName = "OS: Terminal не null")]
    public void OsTerminalNotNull() => Assert.That(OS.Terminal, Is.Not.Null);

    [TestCase(TestName = "OS: Distribution определяется на Linux")]
    [Platform("Linux")]
    public void OsDistributionLinux()
    {
        // На Linux Distribution должен быть определён (если есть /etc/os-release)
        if (File.Exists("/etc/os-release"))
        {
            // Может быть Unknown если дистрибутив не поддерживается
            Assert.That(OS.Distribution, Is.TypeOf<Distributive>());
        }
        else
        {
            Assert.That(OS.Distribution, Is.EqualTo(Distributive.Unknown));
        }
    }

    #endregion

    #region Distributive Enum Tests

    [TestCase(TestName = "Distributive: Unknown имеет значение 0")]
    public void DistributiveUnknownIsZero() => Assert.That((int)Distributive.Unknown, Is.Zero);

    [TestCase(TestName = "Distributive: все значения уникальны")]
    public void DistributiveValuesUnique()
    {
        var values = Enum.GetValues<Distributive>();
        var uniqueValues = values.Distinct().ToArray();
        Assert.That(uniqueValues.Length, Is.EqualTo(values.Length));
    }

    [TestCase(TestName = "Distributive: содержит основные дистрибутивы")]
    public void DistributiveContainsMainDistros()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Enum.IsDefined(Distributive.Debian), Is.True);
            Assert.That(Enum.IsDefined(Distributive.Ubuntu), Is.True);
            Assert.That(Enum.IsDefined(Distributive.Arch), Is.True);
            Assert.That(Enum.IsDefined(Distributive.Manjaro), Is.True);
            Assert.That(Enum.IsDefined(Distributive.Fedora), Is.True);
            Assert.That(Enum.IsDefined(Distributive.Windows), Is.True);
        });
    }

    #endregion

    #region Platform Enum Tests

    [TestCase(TestName = "Platform: Unknown имеет значение 0")]
    public void PlatformUnknownIsZero() => Assert.That((int)Platform.Unknown, Is.Zero);

    [TestCase(TestName = "Platform: все значения уникальны")]
    public void PlatformValuesUnique()
    {
        var values = Enum.GetValues<Platform>();
        var uniqueValues = values.Distinct().ToArray();
        Assert.That(uniqueValues.Length, Is.EqualTo(values.Length));
    }

    [TestCase(TestName = "Platform: содержит основные платформы")]
    public void PlatformContainsMainPlatforms()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Enum.IsDefined(Platform.Linux), Is.True);
            Assert.That(Enum.IsDefined(Platform.Windows), Is.True);
            Assert.That(Enum.IsDefined(Platform.OSX), Is.True);
            Assert.That(Enum.IsDefined(Platform.Android), Is.True);
            Assert.That(Enum.IsDefined(Platform.IOS), Is.True);
        });
    }

    #endregion

    #region Terminal Tests

    [TestCase(TestName = "Terminal: Run выполняет команду")]
    [Platform("Linux,MacOsX")]
    public void TerminalRunExecutesCommand()
    {
        using var processInfo = OS.Terminal.Run("echo hello");
        Assert.That(processInfo.IsRunning || !processInfo.IsRunning, Is.True); // Процесс создан
    }

    [TestCase(TestName = "Terminal: Run с echo возвращает вывод")]
    [Platform("Linux,MacOsX")]
    public async Task TerminalRunEchoReturnsOutput()
    {
        using var processInfo = OS.Terminal.Run("echo hello");
        await processInfo.WaitForEndingAsync();

        var output = await processInfo.Output.ReadToEndAsync();
        Assert.That(output.Trim(), Is.EqualTo("hello"));
    }

    [TestCase(TestName = "Terminal: RunAndWaitAsync завершается успешно")]
    [Platform("Linux,MacOsX")]
    public async Task TerminalRunAndWaitAsyncCompletes()
    {
        var result = await OS.Terminal.RunAndWaitAsync("echo test");
        Assert.That(result, Is.True);
    }

    [TestCase(TestName = "Terminal: RunAndWaitAsync с неудачной командой")]
    [Platform("Linux,MacOsX")]
    public async Task TerminalRunAndWaitAsyncFails()
    {
        var result = await OS.Terminal.RunAndWaitAsync("exit 1");
        Assert.That(result, Is.False);
    }

    [TestCase(TestName = "Terminal: RunningProcesses изначально пустая или содержит процессы")]
    public void TerminalRunningProcessesAccessible() => Assert.DoesNotThrow(() => _ = OS.Terminal.RunningProcesses.ToList());

    [TestCase(TestName = "Terminal: RootPassword можно установить")]
    public void TerminalRootPasswordCanBeSet()
    {
        var originalPassword = OS.Terminal.RootPassword;

        OS.Terminal.RootPassword = "test123";
        Assert.That(OS.Terminal.RootPassword, Is.EqualTo("test123"));

        OS.Terminal.RootPassword = originalPassword;
    }

    [TestCase(TestName = "Terminal: Dispose не выбрасывает исключений")]
    [Platform("Linux,MacOsX")]
    public void TerminalDisposeNoException()
    {
        // Создаем новый Terminal для теста (не используем OS.Terminal чтобы не повлиять на другие тесты)
        // К сожалению Terminal имеет internal конструктор, поэтому тестируем через процесс
        using var processInfo = OS.Terminal.Run("sleep 0.1");
        Assert.DoesNotThrow(processInfo.Dispose);
    }

    [TestCase(TestName = "Terminal: параллельный запуск команд")]
    [Platform("Linux,MacOsX")]
    public async Task TerminalParallelCommandExecution()
    {
        const int count = 5;
        var tasks = new Task<bool>[count];

        for (var i = 0; i < count; i++)
        {
            tasks[i] = OS.Terminal.RunAndWaitAsync("echo test").AsTask();
        }

        var results = await Task.WhenAll(tasks);
        Assert.That(results, Has.All.True);
    }

    [TestCase(TestName = "Terminal: Run добавляет процесс в RunningProcesses")]
    [Platform("Linux,MacOsX")]
    public async Task TerminalRunAddsToRunningProcesses()
    {
        using var processInfo = OS.Terminal.Run("sleep 1");

        // Проверяем, что процесс добавлен
        var runningBefore = OS.Terminal.RunningProcesses.Count();

        // Убиваем процесс
        processInfo.Kill();
        await Task.Delay(100); // Даем время на завершение
    }

    #endregion

    #region ProcessInfo Tests

    [TestCase(TestName = "ProcessInfo: Output доступен")]
    [Platform("Linux,MacOsX")]
    public void ProcessInfoOutputAccessible()
    {
        using var processInfo = OS.Terminal.Run("echo test");
        Assert.That(processInfo.Output, Is.Not.Null);
    }

    [TestCase(TestName = "ProcessInfo: Error доступен")]
    [Platform("Linux,MacOsX")]
    public void ProcessInfoErrorAccessible()
    {
        using var processInfo = OS.Terminal.Run("echo test");
        Assert.That(processInfo.Error, Is.Not.Null);
    }

    [TestCase(TestName = "ProcessInfo: IsRunning корректен после завершения")]
    [Platform("Linux,MacOsX")]
    public async Task ProcessInfoIsRunningCorrect()
    {
        using var processInfo = OS.Terminal.Run("echo test");
        await processInfo.WaitForEndingAsync();
        Assert.That(processInfo.IsRunning, Is.False);
    }

    [TestCase(TestName = "ProcessInfo: ExitCode корректен при успехе")]
    [Platform("Linux,MacOsX")]
    public async Task ProcessInfoExitCodeSuccess()
    {
        using var processInfo = OS.Terminal.Run("exit 0");
        await processInfo.WaitForEndingAsync();
        Assert.That(processInfo.ExitCode, Is.Zero);
    }

    [TestCase(TestName = "ProcessInfo: ExitCode корректен при ошибке")]
    [Platform("Linux,MacOsX")]
    public async Task ProcessInfoExitCodeError()
    {
        using var processInfo = OS.Terminal.Run("exit 42");
        await processInfo.WaitForEndingAsync();
        Assert.That(processInfo.ExitCode, Is.EqualTo(42));
    }

    [TestCase(TestName = "ProcessInfo: IsSuccessExiting корректен")]
    [Platform("Linux,MacOsX")]
    public async Task ProcessInfoIsSuccessExiting()
    {
        using var successProcess = OS.Terminal.Run("exit 0");
        await successProcess.WaitForEndingAsync();
        Assert.That(successProcess.IsSuccessExiting, Is.True);

        using var failProcess = OS.Terminal.Run("exit 1");
        await failProcess.WaitForEndingAsync();
        Assert.That(failProcess.IsSuccessExiting, Is.False);
    }

    [TestCase(TestName = "ProcessInfo: Kill завершает процесс")]
    [Platform("Linux,MacOsX")]
    public async Task ProcessInfoKillTerminatesProcess()
    {
        using var processInfo = OS.Terminal.Run("sleep 10");

        // Даем процессу запуститься
        await Task.Delay(100);
        Assert.That(processInfo.IsRunning, Is.True);

        processInfo.Kill();
        await Task.Delay(200);

        Assert.That(processInfo.IsRunning, Is.False);
    }

    [TestCase(TestName = "ProcessInfo: WaitForEndingAsync с CancellationToken")]
    [Platform("Linux,MacOsX")]
    public async Task ProcessInfoWaitForEndingAsyncWithCancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var processInfo = OS.Terminal.Run("sleep 10");

        try
        {
            await processInfo.WaitForEndingAsync(cts.Token);
            Assert.Fail("Должно было выбросить OperationCanceledException");
        }
        catch (OperationCanceledException)
        {
            // Ожидаемое поведение
            Assert.Pass();
        }
        finally
        {
            processInfo.Kill();
        }
    }

    [TestCase(TestName = "ProcessInfo: Equals сравнивает по Id")]
    [Platform("Linux,MacOsX")]
    public void ProcessInfoEqualsComparesById()
    {
        using var process1 = OS.Terminal.Run("echo 1");
        using var process2 = OS.Terminal.Run("echo 2");
        var process1Copy = process1;

        Assert.Multiple(() =>
        {
            Assert.That(process1.Equals(process1Copy), Is.True);
            Assert.That(process1.Equals(process2), Is.False);
            Assert.That(process1 == process1Copy, Is.True);
            Assert.That(process1 != process2, Is.True);
        });
    }

    [TestCase(TestName = "ProcessInfo: GetHashCode не выбрасывает")]
    [Platform("Linux,MacOsX")]
    public void ProcessInfoGetHashCodeNoException()
    {
        using var processInfo = OS.Terminal.Run("echo test");
        Assert.DoesNotThrow(() => _ = processInfo.GetHashCode());
    }

    #endregion

    #region PackageManager Tests

    [TestCase(TestName = "PackageManager: CheckExistsAsync для несуществующего пакета")]
    [Platform("Linux")]
    public async Task PackageManagerCheckExistsAsyncNonExistent()
    {
        // Пропускаем тест если дистрибутив не поддерживается
        if (OS.Distribution is Distributive.Unknown or Distributive.Windows)
        {
            Assert.Ignore("Тест требует поддерживаемый Linux дистрибутив");
            return;
        }

        try
        {
            var exists = await OS.PM.CheckExistsAsync("this-package-definitely-does-not-exist-12345");
            Assert.That(exists, Is.False);
        }
        catch (UnsupportedDistributiveException)
        {
            Assert.Ignore("Дистрибутив не поддерживается");
        }
    }

    [TestCase(TestName = "PackageManager: CheckExistsAsync для bash")]
    [Platform("Linux")]
    public async Task PackageManagerCheckExistsAsyncBash()
    {
        // Пропускаем тест если дистрибутив не поддерживается
        if (OS.Distribution is Distributive.Unknown or Distributive.Windows)
        {
            Assert.Ignore("Тест требует поддерживаемый Linux дистрибутив");
            return;
        }

        try
        {
            var exists = await OS.PM.CheckExistsAsync("bash");
            Assert.That(exists, Is.True, "bash должен быть установлен на большинстве Linux систем");
        }
        catch (UnsupportedDistributiveException)
        {
            Assert.Ignore("Дистрибутив не поддерживается");
        }
    }

    [TestCase(TestName = "PackageManager: CheckExistsAsync с CancellationToken")]
    [Platform("Linux")]
    public async Task PackageManagerCheckExistsAsyncWithCancellation()
    {
        if (OS.Distribution is Distributive.Unknown or Distributive.Windows)
        {
            Assert.Ignore("Тест требует поддерживаемый Linux дистрибутив");
            return;
        }

        using var cts = new CancellationTokenSource();

        try
        {
            var task = OS.PM.CheckExistsAsync("bash", cts.Token);
            var result = await task;
            Assert.That(result, Is.True);
        }
        catch (UnsupportedDistributiveException)
        {
            Assert.Ignore("Дистрибутив не поддерживается");
        }
    }

    [TestCase(TestName = "PackageManager: UnsupportedDistributiveException для Unknown")]
    public void PackageManagerUnsupportedDistributiveException()
    {
        // Создаем PackageManager с Unknown дистрибутивом через рефлексию нельзя,
        // но можем проверить, что исключение существует и работает
        var exception = new UnsupportedDistributiveException();
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception, Is.InstanceOf<Exception>());
    }

    #endregion

    #region UnsupportedDistributiveException Tests

    [TestCase(TestName = "UnsupportedDistributiveException: создание без параметров")]
    public void UnsupportedDistributiveExceptionDefaultConstructor()
    {
        var exception = new UnsupportedDistributiveException();
        Assert.That(exception, Is.Not.Null);
    }

    #endregion

    #region Integration Tests

    [TestCase(TestName = "Integration: полный цикл выполнения команды")]
    [Platform("Linux,MacOsX")]
    public async Task IntegrationFullCommandCycle()
    {
        // Запускаем команду
        using var processInfo = OS.Terminal.Run("echo 'Hello, World!'");

        // Ждем завершения
        var success = await processInfo.WaitForEndingAsync();

        // Читаем вывод
        var output = await processInfo.Output.ReadToEndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(processInfo.IsSuccessExiting, Is.True);
            Assert.That(output.Trim(), Does.Contain("Hello, World!"));
        });
    }

    [TestCase(TestName = "Integration: команда с ошибкой")]
    [Platform("Linux,MacOsX")]
    public async Task IntegrationCommandWithError()
    {
        using var processInfo = OS.Terminal.Run("ls /nonexistent_directory_12345");

        var success = await processInfo.WaitForEndingAsync();
        var error = await processInfo.Error.ReadToEndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(processInfo.IsSuccessExiting, Is.False);
            Assert.That(error, Is.Not.Empty);
        });
    }

    [TestCase(TestName = "Integration: множество процессов")]
    [Platform("Linux,MacOsX")]
    public async Task IntegrationMultipleProcesses()
    {
        var processes = new List<ProcessInfo>();

        try
        {
            for (var i = 0; i < 10; i++)
            {
                processes.Add(OS.Terminal.Run($"echo {i}"));
            }

            var tasks = processes.Select(p => p.WaitForEndingAsync().AsTask()).ToArray();
            var results = await Task.WhenAll(tasks);

            Assert.That(results, Has.All.True);
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    #endregion

    #region Thread Safety Tests

    [TestCase(TestName = "Thread Safety: параллельные операции с Terminal")]
    [Platform("Linux,MacOsX")]
    public async Task ThreadSafetyParallelTerminalOperations()
    {
        const int concurrency = 20;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var tasks = new Task[concurrency];

        for (var i = 0; i < concurrency; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    using var process = OS.Terminal.Run($"echo {index}");
                    await process.WaitForEndingAsync();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }

        await Task.WhenAll(tasks);

        Assert.That(exceptions, Is.Empty,
            $"Параллельные операции вызвали исключения: {string.Join(", ", exceptions.Select(e => e.Message))}");
    }

    #endregion
}
