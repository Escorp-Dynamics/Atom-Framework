using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Реестр запущенных драйвером браузерных процессов, привязанных к процессу-владельцу.
/// </summary>
/// <remarks>
/// Graceful-освобождение убивает браузер само. Но при жёстком завершении владельца (SIGKILL,
/// «стоп» в отладчике, падение) <c>DisposeAsync</c> не вызывается, и браузер + его временный
/// профиль остаются сиротами. Реестр это чинит: каждый запуск регистрирует PID браузера и путь
/// профиля с PID владельца, а следующий запуск при старте выметает записи, чей владелец уже мёртв —
/// убивает осиротевший браузер и удаляет его временный профиль.
/// <para>
/// Перед убийством PID сверяется с командной строкой процесса (в ней есть уникальный путь профиля):
/// это защищает от переиспользования PID системой.
/// </para>
/// </remarks>
internal static class BridgeBrowserProcessRegistry
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);

    private static string StateDirectory
        => IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".atom-webdriver");

    private static string RegistryPath => IOPath.Combine(StateDirectory, "browser-processes.json");

    private static string LockPath => IOPath.Combine(StateDirectory, "browser-processes.lock");

    /// <summary>Регистрирует запущенный браузерный процесс за текущим владельцем.</summary>
    internal static void Register(int browserProcessId, string? profilePath)
    {
        if (browserProcessId <= 0)
            return;

        Mutate(entries =>
        {
            entries.RemoveAll(entry => entry.BrowserProcessId == browserProcessId);
            entries.Add(new Entry(Environment.ProcessId, browserProcessId, profilePath ?? string.Empty));
        });
    }

    /// <summary>
    /// Поднимает внешний watchdog, который привязывает жизнь браузера к процессу-владельцу:
    /// при смерти владельца (в т.ч. SIGKILL из отладчика, когда <c>DisposeAsync</c> не вызывается)
    /// он немедленно убивает дерево браузера и его временный профиль, не дожидаясь startup-sweep
    /// следующего запуска. При graceful-освобождении браузер умирает сам — watchdog это замечает и
    /// молча выходит.
    /// </summary>
    internal static void SpawnParentDeathWatchdog(int browserProcessId, string? profilePath)
    {
        if (browserProcessId <= 0 || !OperatingSystem.IsLinux())
            return;

        // setsid отвязывает watchdog в собственную сессию — иначе групповое убийство владельца
        // (отладчик может бить по группе процессов) заодно снесло бы и сторожа. exec >/dev/null
        // закрывает унаследованный stdout владельца. Профиль — уникальный временный каталог, поэтому
        // pkill -f по нему безопасен (без сопутствующих жертв); он же добивает дочерние процессы,
        // которые не держат PID главного в командной строке.
        var setsidPath = ResolveExecutablePath("setsid");
        if (setsidPath is null)
            return; // без setsid остаётся startup-sweep следующего запуска

        const string script =
            "exec >/dev/null 2>&1; " +
            "while kill -0 \"$1\" 2>/dev/null; do " +
            "kill -0 \"$2\" 2>/dev/null || exit 0; " +
            "sleep 1; done; " +
            "kill -9 \"$2\" 2>/dev/null; " +
            // Профиль — уникальный временный каталог; case-guard страхует от rm вне .atom-webdriver.
            "if [ -n \"$3\" ]; then pkill -9 -f \"$3\" 2>/dev/null; " +
            "case \"$3\" in */.atom-webdriver/*) rm -rf \"$3\" 2>/dev/null;; esac; fi; " +
            "exit 0";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = setsidPath,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("sh");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add("atom-webdriver-watchdog"); // $0
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)); // $1 — владелец
            startInfo.ArgumentList.Add(browserProcessId.ToString(CultureInfo.InvariantCulture)); // $2 — браузер
            startInfo.ArgumentList.Add(profilePath ?? string.Empty); // $3 — профиль

            using var watchdog = Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // Нет setsid/sh — не критично: остаётся startup-sweep следующего запуска.
        }
    }

    /// <summary>Снимает запись о браузере (graceful-освобождение уже убило процесс).</summary>
    internal static void Unregister(int browserProcessId)
    {
        if (browserProcessId <= 0)
            return;

        Mutate(entries => entries.RemoveAll(entry => entry.BrowserProcessId == browserProcessId));
    }

    /// <summary>
    /// Выметает браузеры, чьи владельцы уже не живут: убивает процесс и удаляет временный профиль.
    /// </summary>
    internal static void SweepAbandoned(ILogger? logger)
    {
        Mutate(entries =>
        {
            for (var index = entries.Count - 1; index >= 0; index--)
            {
                var entry = entries[index];

                if (IsProcessAlive(entry.OwnerProcessId))
                    continue; // владелец жив — запись актуальна

                // Владелец мёртв: убиваем осиротевший браузер (с проверкой по cmdline) и профиль.
                if (IsProcessAlive(entry.BrowserProcessId) && CommandLineMatchesProfile(entry.BrowserProcessId, entry.ProfilePath))
                {
                    KillProcessTree(entry.BrowserProcessId);
                    logger?.LogWebBrowserOrphanSwept(entry.BrowserProcessId, entry.OwnerProcessId);
                }

                TryDeleteTemporaryProfile(entry.ProfilePath);
                entries.RemoveAt(index);
            }
        });
    }

    private static bool CommandLineMatchesProfile(int processId, string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            return false;

        try
        {
            var cmdline = File.ReadAllText($"/proc/{processId.ToString(CultureInfo.InvariantCulture)}/cmdline");
            return cmdline.Contains(profilePath, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Нет /proc или процесс исчез — не наш кандидат на убийство.
            return false;
        }
    }

    private static void KillProcessTree(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Процесс уже завершился либо дерево недоступно — достаточно того, что его нет.
        }
    }

    private static void TryDeleteTemporaryProfile(string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            return;

        // Удаляем только временные профили драйвера, а не пользовательские каталоги.
        var temporaryRoot = StateDirectory + IOPath.DirectorySeparatorChar;
        if (!profilePath.StartsWith(temporaryRoot, StringComparison.Ordinal))
            return;

        try
        {
            if (Directory.Exists(profilePath))
                Directory.Delete(profilePath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Профиль подберёт следующий проход, если удалить сейчас не вышло.
        }
    }

    private static string? ResolveExecutablePath(string command)
    {
        // Абсолютный путь нужен и ради безопасности (нет PATH-подмены), и чтобы удовлетворить анализатор.
        string[] candidates =
        [
            $"/usr/bin/{command}",
            $"/bin/{command}",
            $"/usr/local/bin/{command}",
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsProcessAlive(int processId)
    {
        if (processId <= 0)
            return false;

        if (processId == Environment.ProcessId)
            return true;

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void Mutate(Action<List<Entry>> mutation)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        using var registryLock = AcquireLock();
        var entries = Read();
        mutation(entries);
        Write(entries);
    }

    private static List<Entry> Read()
    {
        try
        {
            if (!File.Exists(RegistryPath))
                return [];

            if (JsonNode.Parse(File.ReadAllText(RegistryPath)) is not JsonArray array)
                return [];

            var entries = new List<Entry>(array.Count);
            foreach (var node in array)
            {
                if (node is not JsonObject item)
                    continue;

                var owner = item["owner"]?.GetValue<int>() ?? 0;
                var browser = item["browser"]?.GetValue<int>() ?? 0;
                var profile = item["profile"]?.GetValue<string>() ?? string.Empty;
                if (browser > 0)
                    entries.Add(new Entry(owner, browser, profile));
            }

            return entries;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
        {
            return [];
        }
    }

    private static void Write(List<Entry> entries)
    {
        try
        {
            var array = new JsonArray();
            foreach (var entry in entries)
            {
                array.Add(new JsonObject
                {
                    ["owner"] = JsonValue.Create(entry.OwnerProcessId),
                    ["browser"] = JsonValue.Create(entry.BrowserProcessId),
                    ["profile"] = JsonValue.Create(entry.ProfilePath),
                });
            }

            File.WriteAllText(RegistryPath, array.ToJsonString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Реестр вспомогательный: неудачная запись лишь отложит уборку до следующего прохода.
        }
    }

    private static IDisposable AcquireLock()
    {
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(LockRetryDelay);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new NoOpLock();
            }
        }
    }

    private sealed record Entry(int OwnerProcessId, int BrowserProcessId, string ProfilePath);

    private sealed class NoOpLock : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
