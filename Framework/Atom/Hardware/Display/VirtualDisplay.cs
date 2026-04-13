using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Atom.Hardware.Display;

/// <summary>
/// Представляет виртуальный дисплей на базе xpra.
/// Создаёт изолированную X11-сессию и при необходимости публикует её окна на хостовом рабочем столе через rootless attach.
/// </summary>
/// <remarks>
/// <para>
/// При <see cref="VirtualDisplaySettings.IsVisible"/> = <see langword="false"/> (по умолчанию)
/// запускается изолированная xpra-сессия без локального attach.
/// При <see cref="VirtualDisplaySettings.IsVisible"/> = <see langword="true"/>
/// дополнительно запускается локальный <c>xpra attach</c>, публикующий окна rootless на текущем экране.
/// </para>
/// <para>
/// Браузеры и другие X11-приложения, запущенные с <see cref="Display"/>,
/// рисуют в изолированную X11-сессию и не влияют на системный display напрямую.
/// </para>
/// <para>
/// Виртуальная мышь, подключённая к этому дисплею через XTEST,
/// инжектит события только в этот X-сервер и не затрагивает физическую мышь.
/// </para>
/// <example>
/// <code>
/// // Невидимый rootless-сеанс без локального attach:
/// await using var display = await VirtualDisplay.CreateAsync();
///
/// // Видимый rootless-сеанс: окна публикуются на текущем рабочем столе через xpra attach.
/// await using var display = await VirtualDisplay.CreateAsync(new VirtualDisplaySettings
/// {
///     Resolution = new(1920, 1080),
///     IsVisible = true,
/// });
///
/// // Запуск браузера на виртуальном дисплее.
/// var psi = new ProcessStartInfo("chromium");
/// psi.Environment["DISPLAY"] = display.Display;
/// Process.Start(psi);
/// </code>
/// </example>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class VirtualDisplay : IAsyncDisposable
{
    private static readonly Lock displayReservationGate = new();
    private static readonly HashSet<int> reservedDisplayNumbers = [];
    private Process? xpraServerProcess;
    private Process? xpraAttachProcess;
    private Process? wmProcess;
    private bool isDisposed;

    /// <summary>
    /// Настройки виртуального дисплея.
    /// </summary>
    public VirtualDisplaySettings Settings { get; }

    /// <summary>
    /// Строка дисплея X11 (например, <c>:99</c>).
    /// Используйте для переменной окружения <c>DISPLAY</c>.
    /// </summary>
    public string Display { get; }

    /// <summary>
    /// Номер дисплея.
    /// </summary>
    public int DisplayNumber { get; }

    /// <summary>
    /// Разрешение виртуального экрана.
    /// </summary>
    public Size Resolution => Settings.Resolution;

    internal bool IsDisposed => Volatile.Read(ref isDisposed);

    private VirtualDisplay(Process xpraServerProcess, Process? xpraAttachProcess, Process? wmProcess, VirtualDisplaySettings settings, int displayNumber)
    {
        this.xpraServerProcess = xpraServerProcess;
        this.xpraAttachProcess = xpraAttachProcess;
        this.wmProcess = wmProcess;
        Settings = settings;
        DisplayNumber = displayNumber;
        Display = ":" + displayNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Создаёт новый виртуальный дисплей.
    /// </summary>
    /// <param name="settings">Настройки дисплея.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный виртуальный дисплей.</returns>
    public static async ValueTask<VirtualDisplay> CreateAsync(
        VirtualDisplaySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("VirtualDisplay поддерживается только на Linux.");

        ValidateSettings(settings);

        var displayNumber = ReserveDisplayNumber(settings.DisplayNumber);
        var existingDisplayProcessIds = CaptureDisplayProcessIds(displayNumber);
        var displayName = ":" + displayNumber.ToString(CultureInfo.InvariantCulture);
        var logger = settings.Logger;
        var serverName = settings.IsVisible ? "xpra" : "Xvfb";
        logger?.LogVirtualDisplayStarting(displayName, settings.Resolution.Width, settings.Resolution.Height, settings.ColorDepth, settings.IsVisible);
        var (process, serverLogs) = settings.IsVisible
            ? StartXpraServer(settings, displayNumber, logger)
            : StartXvfbServer(settings, displayNumber, logger);

        Process? windowManager = null;
        Process? attachProcess = null;

        try
        {
            await WaitForDisplayReadyAsync(displayNumber, serverName, cancellationToken).ConfigureAwait(false);
            logger?.LogVirtualDisplayX11SocketReady(displayName);

            if (settings.IsVisible)
            {
                await WaitForXpraControlSocketReadyAsync(displayNumber, process, serverLogs, cancellationToken).ConfigureAwait(false);
                logger?.LogVirtualDisplayControlSocketReady(displayName, GetXpraSocketDiagnostics(displayNumber));
                attachProcess = await StartVisibleAttachAsync(displayNumber, displayName, logger, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            KillProcess(attachProcess);
            KillProcess(windowManager);
            KillProcess(process);
            if (TryTerminateDisplayProcesses(displayNumber, existingDisplayProcessIds))
            {
                CleanupStaleXpraRuntimeArtifacts(displayNumber);
                CleanupDisplayFiles(displayNumber.ToString(CultureInfo.InvariantCulture));
            }

            ReleaseDisplayReservation(displayNumber);
            throw;
        }

        return new VirtualDisplay(process, attachProcess, windowManager, settings, displayNumber);
    }

    /// <summary>
    /// Создаёт виртуальный дисплей с настройками по умолчанию.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный виртуальный дисплей.</returns>
    public static ValueTask<VirtualDisplay> CreateAsync(CancellationToken cancellationToken = default) =>
        CreateAsync(new VirtualDisplaySettings(), cancellationToken);

    /// <summary>
    /// Высвобождает ресурсы и завершает xpra-сессию.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, value: true))
            return ValueTask.CompletedTask;

        var wm = Interlocked.Exchange(ref wmProcess, value: null);
        KillProcess(wm);

        var attach = Interlocked.Exchange(ref xpraAttachProcess, value: null);
        KillProcess(attach);

        var proc = Interlocked.Exchange(ref xpraServerProcess, value: null);
        KillProcess(proc);

        var displayStr = DisplayNumber.ToString(CultureInfo.InvariantCulture);

        if (TryTerminateDisplayProcesses(DisplayNumber))
        {
            CleanupStaleXpraRuntimeArtifacts(DisplayNumber);

            // Удаляем lock-файл и сокет X-сервера только после подтверждённого завершения процессов.
            CleanupDisplayFiles(displayStr);
        }
        else
        {
            var remainingProcessIds = CaptureDisplayProcessIds(DisplayNumber);
            Settings.Logger?.LogVirtualDisplayTerminationTimedOut(Display, string.Join(',', remainingProcessIds.Order()));
        }

        ReleaseDisplayReservation(DisplayNumber);

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    internal void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        var process = xpraServerProcess;
        if (process is not { HasExited: true })
        {
            if (process is null)
                throw new VirtualDisplayException("Виртуальный дисплей " + Display + " больше недоступен.");

            return;
        }

        if (IsDisplayEndpointAvailable(DisplayNumber))
            return;

        throw new VirtualDisplayException("Виртуальный дисплей " + Display + " больше недоступен.");
    }

    private static bool IsDisplayEndpointAvailable(int displayNumber)
    {
        var displayStr = displayNumber.ToString(CultureInfo.InvariantCulture);
        var lockFile = "/tmp/.X" + displayStr + "-lock";
        var socketFile = "/tmp/.X11-unix/X" + displayStr;

        if (!File.Exists(socketFile))
            return false;

        return !File.Exists(lockFile) || !IsStaleDisplay(lockFile);
    }

    private static void CleanupDisplayFiles(string displayStr)
    {
        var lockFile = "/tmp/.X" + displayStr + "-lock";
        var socketFile = "/tmp/.X11-unix/X" + displayStr;

        try { File.Delete(lockFile); }
#pragma warning disable ERP022, S2486, S108
        catch { }
#pragma warning restore ERP022, S2486, S108

        try { File.Delete(socketFile); }
#pragma warning disable ERP022, S2486, S108
        catch { }
#pragma warning restore ERP022, S2486, S108
    }

    private static void ValidateSettings(VirtualDisplaySettings settings)
    {
        if (settings.Resolution.Width < 1)
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Resolution.Width, "Resolution.Width должен быть больше 0.");

        if (settings.Resolution.Height < 1)
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Resolution.Height, "Resolution.Height должен быть больше 0.");

        if (settings.ColorDepth is not (8 or 16 or 24 or 32))
            throw new ArgumentOutOfRangeException(nameof(settings), settings.ColorDepth, "ColorDepth должен быть 8, 16, 24 или 32.");

        if (settings.DisplayNumber is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(settings), settings.DisplayNumber, "DisplayNumber должен быть от 0 до 255.");
    }

    private static int ReserveDisplayNumber(int? requestedDisplayNumber)
    {
        lock (displayReservationGate)
        {
            if (requestedDisplayNumber is int explicitDisplayNumber)
            {
                if (!reservedDisplayNumbers.Add(explicitDisplayNumber))
                    throw new VirtualDisplayException("Дисплей :" + explicitDisplayNumber.ToString(CultureInfo.InvariantCulture) + " уже зарезервирован текущим процессом.");

                return explicitDisplayNumber;
            }

            return ReserveFreeDisplayNumberUnsafe();
        }
    }

    private static void ReleaseDisplayReservation(int displayNumber)
    {
        lock (displayReservationGate)
            _ = reservedDisplayNumbers.Remove(displayNumber);
    }

    private static int ReserveFreeDisplayNumberUnsafe()
    {
        var activeDisplayNumbers = GetRunningManagedDisplayNumbers();

        for (var n = 99; n <= 255; n++)
        {
            if (TryReserveDisplayNumberCandidate(n, activeDisplayNumbers))
                return n;
        }

        throw new VirtualDisplayException("Не удалось найти свободный номер дисплея (99–255).");
    }

    private static bool TryReserveDisplayNumberCandidate(int displayNumber, HashSet<int> activeDisplayNumbers)
    {
        if (reservedDisplayNumbers.Contains(displayNumber) || activeDisplayNumbers.Contains(displayNumber))
            return false;

        CleanupStaleDisplayFilesIfNeeded(displayNumber);

        if (HasDisplayFiles(displayNumber))
            return false;

        CleanupStaleXpraRuntimeArtifacts(displayNumber);

        if (HasXpraRuntimeArtifacts(displayNumber))
            return false;

        _ = reservedDisplayNumbers.Add(displayNumber);
        return true;
    }

    private static void CleanupStaleDisplayFilesIfNeeded(int displayNumber)
    {
        var displayStr = displayNumber.ToString(CultureInfo.InvariantCulture);
        var lockFile = "/tmp/.X" + displayStr + "-lock";
        var socketFile = "/tmp/.X11-unix/X" + displayStr;

        if (File.Exists(lockFile) && IsStaleDisplay(lockFile))
            CleanupDisplayFiles(displayStr);

        if (File.Exists(socketFile) && !File.Exists(lockFile))
            CleanupDisplayFiles(displayStr);
    }

    private static bool HasDisplayFiles(int displayNumber)
    {
        var displayStr = displayNumber.ToString(CultureInfo.InvariantCulture);
        return File.Exists("/tmp/.X" + displayStr + "-lock")
            || File.Exists("/tmp/.X11-unix/X" + displayStr);
    }

    private static bool IsStaleDisplay(string lockFile)
    {
        if (!File.Exists(lockFile))
            return false;

        try
        {
            var pidText = File.ReadAllText(lockFile).Trim();
            if (int.TryParse(pidText, CultureInfo.InvariantCulture, out var pid))
            {
                return !Directory.Exists("/proc/" + pid.ToString(CultureInfo.InvariantCulture));
            }
        }
#pragma warning disable ERP022, S2486, S108
        catch { }
#pragma warning restore ERP022, S2486, S108

        return false;
    }

    private static HashSet<int> GetRunningManagedDisplayNumbers()
    {
        var displayNumbers = new HashSet<int>();

        foreach (var processId in EnumerateProcessIds())
        {
            if (TryGetManagedDisplayNumber(TryReadProcessCommandLine(processId), out var displayNumber))
                _ = displayNumbers.Add(displayNumber);
        }

        return displayNumbers;
    }

    private static HashSet<int> CaptureDisplayProcessIds(int displayNumber)
    {
        var processIds = new HashSet<int>();

        foreach (var processId in EnumerateProcessIds())
        {
            if (OwnsManagedDisplay(TryReadProcessCommandLine(processId), displayNumber))
                _ = processIds.Add(processId);
        }

        return processIds;
    }

    private static IEnumerable<int> EnumerateProcessIds()
    {
        IEnumerable<string> directories;

        try
        {
            directories = Directory.EnumerateDirectories("/proc");
        }
#pragma warning disable ERP022, S2486, S108
        catch
        {
            yield break;
        }
#pragma warning restore ERP022, S2486, S108

        foreach (var directory in directories)
        {
            var fileName = Path.GetFileName(directory);
            if (int.TryParse(fileName, CultureInfo.InvariantCulture, out var processId))
            {
                yield return processId;
            }
        }
    }

    private static string? TryReadProcessCommandLine(int processId)
    {
        try
        {
            return File.ReadAllText(Path.Combine("/proc", processId.ToString(CultureInfo.InvariantCulture), "cmdline"));
        }
#pragma warning disable ERP022, S2486, S108
        catch
        {
            return null;
        }
#pragma warning restore ERP022, S2486, S108
    }

    private static bool OwnsManagedDisplay(string? commandLine, int displayNumber) =>
        TryGetManagedDisplayNumber(commandLine, out var ownedDisplayNumber)
        && ownedDisplayNumber == displayNumber;

    private static bool TryGetManagedDisplayNumber(string? commandLine, out int displayNumber)
    {
        displayNumber = -1;

        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        if (TryExtractDisplayNumber(commandLine, "Xvfb-for-Xpra-", out displayNumber))
        {
            return true;
        }

        var normalizedCommandLine = commandLine.Replace('\0', ' ');

        if (LooksLikeManagedHiddenXvfbCommand(commandLine, normalizedCommandLine)
            && (TryExtractDisplayNumber(commandLine, "Xvfb\0:", out displayNumber)
                || TryExtractDisplayNumber(normalizedCommandLine, "Xvfb :", out displayNumber)))
        {
            return true;
        }

        if (!commandLine.Contains("xpra", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryExtractDisplayNumber(commandLine, "start\0:", out displayNumber)
            || TryExtractDisplayNumber(commandLine, "attach\0:", out displayNumber))
        {
            return true;
        }

        return TryExtractDisplayNumber(normalizedCommandLine, "start :", out displayNumber)
            || TryExtractDisplayNumber(normalizedCommandLine, "attach :", out displayNumber);
    }

    private static bool LooksLikeManagedHiddenXvfbCommand(string commandLine, string normalizedCommandLine)
    {
        if (!normalizedCommandLine.Contains("Xvfb", StringComparison.OrdinalIgnoreCase))
            return false;

        return ContainsCommandFragment(commandLine, normalizedCommandLine, "+extension\0XTEST", "+extension XTEST")
            && ContainsCommandFragment(commandLine, normalizedCommandLine, "-nolisten\0tcp", "-nolisten tcp")
            && ContainsCommandFragment(commandLine, normalizedCommandLine, "-screen\00", "-screen 0");
    }

    private static bool ContainsCommandFragment(string commandLine, string normalizedCommandLine, string nulSeparatedFragment, string spacedFragment)
        => commandLine.Contains(nulSeparatedFragment, StringComparison.OrdinalIgnoreCase)
            || normalizedCommandLine.Contains(spacedFragment, StringComparison.OrdinalIgnoreCase);

    private static bool TryExtractDisplayNumber(string text, string marker, out int displayNumber)
    {
        displayNumber = -1;

        var markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return false;

        var startIndex = markerIndex + marker.Length;
        var endIndex = startIndex;

        while (endIndex < text.Length && char.IsDigit(text[endIndex]))
            endIndex++;

        return endIndex > startIndex
            && int.TryParse(text.AsSpan(startIndex, endIndex - startIndex), CultureInfo.InvariantCulture, out displayNumber);
    }

    private static bool HasXpraRuntimeArtifacts(int displayNumber)
    {
        var (displayDirectory, directSocketPath, hostSocketPath) = GetXpraRuntimeArtifactPaths(displayNumber);

        return Directory.Exists(displayDirectory)
            || File.Exists(directSocketPath)
            || File.Exists(hostSocketPath);
    }

    private static void CleanupStaleXpraRuntimeArtifacts(int displayNumber)
    {
        var (displayDirectory, directSocketPath, hostSocketPath) = GetXpraRuntimeArtifactPaths(displayNumber);

        TryDeleteFile(directSocketPath);
        TryDeleteFile(hostSocketPath);
        TryDeleteDirectory(displayDirectory);
    }

    private static (string DisplayDirectory, string DirectSocketPath, string HostSocketPath) GetXpraRuntimeArtifactPaths(int displayNumber)
    {
        var (_, directSocketPath, hostSocketPath) = GetXpraSocketPaths(displayNumber);
        var baseDirectory = GetXpraSocketDirectory();
        var displayDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? string.Empty
            : Path.Combine(baseDirectory, displayNumber.ToString(CultureInfo.InvariantCulture));

        return (displayDirectory, directSocketPath, hostSocketPath);
    }

    private static void KillLeakedDisplayProcesses(int displayNumber, HashSet<int>? preserveProcessIds = null)
    {
        foreach (var processId in CaptureDisplayProcessIds(displayNumber))
        {
            if (preserveProcessIds?.Contains(processId) == true)
                continue;

            KillProcessById(processId);
        }
    }

    private static bool TryTerminateDisplayProcesses(int displayNumber, HashSet<int>? preserveProcessIds = null)
    {
        var startTime = Stopwatch.GetTimestamp();
        var timeout = TimeSpan.FromSeconds(5);

        while (true)
        {
            KillLeakedDisplayProcesses(displayNumber, preserveProcessIds);

            var remainingProcessIds = CaptureDisplayProcessIds(displayNumber)
                .Where(processId => preserveProcessIds?.Contains(processId) != true)
                .ToArray();

            if (remainingProcessIds.Length == 0)
                return true;

            foreach (var processId in remainingProcessIds)
                KillProcessById(processId);

            if (Stopwatch.GetElapsedTime(startTime) >= timeout)
                return false;

            Thread.Sleep(100);
        }
    }

    private static void KillProcessById(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
        }
#pragma warning disable ERP022, S2486, S108
        catch
        {
        }
#pragma warning restore ERP022, S2486, S108
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            File.Delete(path);
        }
#pragma warning disable ERP022, S2486, S108
        catch
        {
        }
#pragma warning restore ERP022, S2486, S108
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
#pragma warning disable ERP022, S2486, S108
        catch
        {
        }
#pragma warning restore ERP022, S2486, S108
    }

    private static (Process Process, ProcessLogBuffer Logs) StartXpraServer(VirtualDisplaySettings settings, int displayNumber, ILogger? logger)
    {
        var psi = CreateXpraServerStartInfo(settings, displayNumber);

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new VirtualDisplayException("Не удалось запустить xpra.");
        }
        catch (Exception ex) when (ex is not VirtualDisplayException)
        {
            throw new VirtualDisplayException(
                "Не удалось запустить xpra. Убедитесь что установлен xpra и доступен backend для вложенного X11-сервера.", ex);
        }

        var logBuffer = AttachProcessLogging(process, logger, displayNumber, "xpra-server");

        // Если процесс завершился мгновенно — ошибка запуска.
        if (process.HasExited)
        {
            process.WaitForExit(250);
            var stderr = logBuffer.GetBufferedStderr();
            process.Dispose();
            throw new VirtualDisplayException(
                "xpra завершился при запуске. Stderr: " + stderr);
        }

        return (process, logBuffer);
    }

    private static (Process Process, ProcessLogBuffer Logs) StartXvfbServer(VirtualDisplaySettings settings, int displayNumber, ILogger? logger)
    {
        var psi = CreateXvfbServerStartInfo(settings, displayNumber);

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new VirtualDisplayException("Не удалось запустить Xvfb.");
        }
        catch (Exception ex) when (ex is not VirtualDisplayException)
        {
            throw new VirtualDisplayException(
                "Не удалось запустить Xvfb. Убедитесь что Xvfb установлен и доступен в PATH.", ex);
        }

        var logBuffer = AttachProcessLogging(process, logger, displayNumber, "xvfb-server");

        if (process.HasExited)
        {
            process.WaitForExit(250);
            var stderr = logBuffer.GetBufferedStderr();
            process.Dispose();
            throw new VirtualDisplayException(
                "Xvfb завершился при запуске. Stderr: " + stderr);
        }

        return (process, logBuffer);
    }

    private static ProcessStartInfo CreateXvfbServerStartInfo(VirtualDisplaySettings settings, int displayNumber)
    {
        var screenArg = settings.Resolution.Width.ToString(CultureInfo.InvariantCulture)
            + "x" + settings.Resolution.Height.ToString(CultureInfo.InvariantCulture)
            + "x" + settings.ColorDepth.ToString(CultureInfo.InvariantCulture);

        return new ProcessStartInfo
        {
            FileName = "Xvfb",
            ArgumentList =
            {
                ":" + displayNumber.ToString(CultureInfo.InvariantCulture),
                "-screen",
                "0",
                screenArg,
                "-ac",
                "-nolisten",
                "tcp",
                "+extension",
                "XTEST",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    private static ProcessStartInfo CreateXpraServerStartInfo(VirtualDisplaySettings settings, int displayNumber)
    {
        var screenArg = settings.Resolution.Width.ToString(CultureInfo.InvariantCulture)
            + "x" + settings.Resolution.Height.ToString(CultureInfo.InvariantCulture)
            + "x" + settings.ColorDepth.ToString(CultureInfo.InvariantCulture);

        var display = ":" + displayNumber.ToString(CultureInfo.InvariantCulture);
        var socketDirectory = GetXpraSocketDirectory();

        var startInfo = new ProcessStartInfo
        {
            FileName = "xpra",
            ArgumentList =
            {
                "start",
                display,
                    "--daemon=no",
                    "--splash=no",
                "--attach=no",
                "--mdns=no",
                "--notifications=no",
                "--clipboard=no",
                "--pulseaudio=no",
                "--speaker=off",
                "--microphone=off",
                "--webcam=no",
                "--printing=no",
                "--file-transfer=off",
                "--open-files=no",
                "--html=off",
                "--input-method=none",
                "--start-new-commands=no",
                "--exit-with-children=no",
                "--exit-with-windows=no",
                "--start-via-proxy=no",
                "--systemd-run=no",
                "--dbus-launch=no",
                "--windows=yes",
                "--resize-display=" + settings.Resolution.Width.ToString(CultureInfo.InvariantCulture) + "x" + settings.Resolution.Height.ToString(CultureInfo.InvariantCulture),
                "--pixel-depth=" + settings.ColorDepth.ToString(CultureInfo.InvariantCulture),
                "--use-display=no",
                "--xvfb=Xvfb -screen 0 " + screenArg + " -ac -nolisten tcp +extension XTEST",
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(socketDirectory))
        {
            startInfo.ArgumentList.Add("--bind=auto");
            startInfo.ArgumentList.Add("--socket-dir=" + socketDirectory);
        }

        return startInfo;
    }

    private static (Process Process, ProcessLogBuffer Logs) StartXpraAttach(int displayNumber, ILogger? logger)
    {
        var socketDirectory = GetXpraSocketDirectory();
        var psi = new ProcessStartInfo
        {
            FileName = "xpra",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in GetXpraAttachArguments(displayNumber))
            psi.ArgumentList.Add(argument);

        if (!string.IsNullOrWhiteSpace(socketDirectory))
            psi.ArgumentList.Add("--socket-dir=" + socketDirectory);

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new VirtualDisplayException("Не удалось подключить локальный xpra attach.");
        }
        catch (Exception ex) when (ex is not VirtualDisplayException)
        {
            throw new VirtualDisplayException(
                "Не удалось подключить локальный xpra attach. Убедитесь что xpra установлен и доступен текущий DISPLAY.", ex);
        }

        var logBuffer = AttachProcessLogging(process, logger, displayNumber, "xpra-attach");

        if (process.HasExited)
        {
            process.WaitForExit(250);
            var stderr = logBuffer.GetBufferedStderr();
            process.Dispose();
            throw new VirtualDisplayException(
                "xpra attach завершился при запуске. Stderr: " + stderr);
        }

        return (process, logBuffer);
    }

    private static string[] GetXpraAttachArguments(int displayNumber)
        =>
        [
            "attach",
            ":" + displayNumber.ToString(CultureInfo.InvariantCulture),
            "--opengl=auto",
            "--splash=no",
            "--tray=no",
            "--reconnect=no",
            "--mmap=yes",
            "--encoding=webp",
            "--video=no",
            "--min-quality=50",
            "--quality=70",
            "--min-speed=80",
            "--speed=100",
            "--auto-refresh-delay=0",
            "--compression_level=0",
            "--headerbar=no",
            "--border=off",
            "--title=@title@",
            "--system-tray=no",
            "--bell=no",
            "--xsettings=no",
            "--keyboard-sync=no",
            "--clipboard=no",
            "--notifications=no",
            "--sharing=no",
            "--windows=yes",
            "--refresh-rate=30",
            "--desktop-scaling=off",
        ];

    private static async ValueTask WaitForDisplayReadyAsync(int displayNumber, string serverName, CancellationToken cancellationToken)
    {
        var socketPath = "/tmp/.X11-unix/X" + displayNumber.ToString(CultureInfo.InvariantCulture);

        // Ожидаем появления Unix-сокета X-сервера (до 3 секунд).
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(socketPath))
                return;

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new VirtualDisplayException(
            serverName + " не создал сокет " + socketPath + " за 3 секунды.");
    }

    private static async ValueTask WaitForXpraControlSocketReadyAsync(int displayNumber, Process xpraServerProcess, ProcessLogBuffer logBuffer, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsXpraControlSocketAvailable(displayNumber))
                return;

            if (xpraServerProcess.HasExited)
            {
                var stderr = logBuffer.GetBufferedStderr();
                throw new VirtualDisplayException(
                    "xpra-сервер завершился до публикации control socket для дисплея :"
                    + displayNumber.ToString(CultureInfo.InvariantCulture)
                    + ". Stderr: "
                    + stderr
                    + ". Диагностика сокетов: "
                    + GetXpraSocketDiagnostics(displayNumber));
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new VirtualDisplayException(
            "xpra не опубликовал control socket для дисплея :"
            + displayNumber.ToString(CultureInfo.InvariantCulture)
            + " за 10 секунд. Диагностика сокетов: "
            + GetXpraSocketDiagnostics(displayNumber));
    }

    private static async ValueTask WaitForXpraAttachReadyAsync(int displayNumber, Process xpraAttachProcess, ProcessLogBuffer logBuffer, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (xpraAttachProcess.HasExited)
            {
                var stderr = logBuffer.GetBufferedStderr();
                throw new VirtualDisplayException(
                    "xpra attach завершился до публикации visible-сеанса для дисплея :"
                    + displayNumber.ToString(CultureInfo.InvariantCulture)
                    + ". Stderr: "
                    + stderr);
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsXpraControlSocketAvailable(int displayNumber)
    {
        var (_, directSocketPath, hostSocketPath) = GetXpraSocketPaths(displayNumber);

        return File.Exists(directSocketPath) || File.Exists(hostSocketPath);
    }

    private static (string RuntimeDirectory, string DirectSocketPath, string HostSocketPath) GetXpraSocketPaths(int displayNumber)
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
            return (string.Empty, string.Empty, string.Empty);

        var displayString = displayNumber.ToString(CultureInfo.InvariantCulture);
        var baseDirectory = GetXpraSocketDirectory();
        var directSocketPath = Path.Combine(baseDirectory, displayString, "socket");
        var hostSocketPath = Path.Combine(baseDirectory, Environment.MachineName + "-" + displayString);
        return (runtimeDirectory, directSocketPath, hostSocketPath);
    }

    private static string GetXpraSocketDirectory()
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return string.IsNullOrWhiteSpace(runtimeDirectory)
            ? string.Empty
            : Path.Combine(runtimeDirectory, "xpra");
    }

    private static string GetXpraSocketDiagnostics(int displayNumber)
    {
        var (runtimeDirectory, directSocketPath, hostSocketPath) = GetXpraSocketPaths(displayNumber);
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
            return "XDG_RUNTIME_DIR не задан.";

        var displayDirectory = Path.GetDirectoryName(directSocketPath) ?? string.Empty;
        var displayEntries = Directory.Exists(displayDirectory)
            ? string.Join(',', Directory.EnumerateFileSystemEntries(displayDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal))
            : string.Empty;

        return "runtime=" + runtimeDirectory
            + "; directSocketPath=" + directSocketPath
            + "; directSocketExists=" + File.Exists(directSocketPath).ToString(CultureInfo.InvariantCulture)
            + "; hostSocketPath=" + hostSocketPath
            + "; hostSocketExists=" + File.Exists(hostSocketPath).ToString(CultureInfo.InvariantCulture)
            + "; displayEntries=" + displayEntries;
    }

    private static async ValueTask<Process> StartVisibleAttachAsync(int displayNumber, string displayName, ILogger? logger, CancellationToken cancellationToken)
    {
        var (attachProcess, attachLogs) = StartXpraAttach(displayNumber, logger);
        await WaitForXpraAttachReadyAsync(displayNumber, attachProcess, attachLogs, cancellationToken).ConfigureAwait(false);
        logger?.LogVirtualDisplayAttachConnected(displayName);
        return attachProcess;
    }

    private static ProcessLogBuffer AttachProcessLogging(Process process, ILogger? logger, int displayNumber, string processName)
    {
        var logBuffer = new ProcessLogBuffer(logger, ":" + displayNumber.ToString(CultureInfo.InvariantCulture), processName);

        process.OutputDataReceived += (_, eventArgs) => logBuffer.RecordStdout(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => logBuffer.RecordStderr(eventArgs.Data);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return logBuffer;
    }

    private sealed class ProcessLogBuffer(ILogger? logger, string displayName, string processName)
    {
        private const int MaxBufferedLines = 64;
        private readonly Lock gate = new();
        private readonly Queue<string> stderrLines = [];
        private readonly Queue<string> stdoutLines = [];

        public void RecordStdout(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            Append(stdoutLines, line);
            logger?.LogVirtualDisplayProcessStdout(processName, displayName, line);
        }

        public void RecordStderr(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            Append(stderrLines, line);
            logger?.LogVirtualDisplayProcessStderr(processName, displayName, line);
        }

        public string GetBufferedStderr() => GetBufferedLines(stderrLines);

        private void Append(Queue<string> target, string line)
        {
            lock (gate)
            {
                if (target.Count >= MaxBufferedLines)
                    _ = target.Dequeue();

                target.Enqueue(line);
            }
        }

        private string GetBufferedLines(Queue<string> source)
        {
            lock (gate)
            {
                return source.Count == 0
                    ? string.Empty
                    : string.Join(Environment.NewLine, source);
            }
        }
    }

    private static void KillProcess(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(1000);
            }
        }
#pragma warning disable ERP022, S2486, S108 // Процесс мог завершиться между проверкой и kill.
        catch { }
#pragma warning restore ERP022, S2486, S108
        finally
        {
            process.Dispose();
        }
    }
}
