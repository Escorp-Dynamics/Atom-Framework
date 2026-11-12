using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atom.Net.Browsing.WebDriver.Protocol.Cdp;
using Microsoft.Extensions.Logging;
using IOPath = System.IO.Path;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Процесс <c lang="text">obscura serve</c>, поднятый драйвером, и адрес его CDP-сервера.
/// </summary>
/// <remarks>
/// Порт выбирает ОС (<c lang="text">--port 0</c>), а фактический адрес публикуется сервером в
/// <c lang="text">--ready-file</c> после инициализации V8. Доступ к CDP закрыт случайным bearer-токеном,
/// чтобы к локальному порту не подключились другие пользователи машины. Учётные данные прокси
/// передаются через окружение, а не командную строку, чтобы не светиться в списке процессов.
/// </remarks>
internal sealed class ObscuraServer : IAsyncDisposable
{
    private const int StderrTailLimit = 4096;

    // Адрес-зонд для IWebProxy.GetProxy: прокси без явного Address раскрывает себя только так.
    private static readonly Uri ProxyProbeUri = new UriBuilder(Uri.UriSchemeHttp, "example.com").Uri;

    private readonly StringBuilder stderrTail = new();
    private int disposeState;

    private ObscuraServer(Process process, string runDirectory, string token)
    {
        Process = process;
        RunDirectory = runDirectory;
        Token = token;
    }

    /// <summary>Процесс сервера.</summary>
    public Process Process { get; }

    /// <summary>Уникальный рабочий каталог запуска (ready-файл, хранилище).</summary>
    public string RunDirectory { get; }

    /// <summary>Bearer-токен доступа к CDP.</summary>
    public string Token { get; }

    /// <summary>WebSocket-адрес браузерной цели.</summary>
    public Uri BrowserEndpoint { get; private set; } = null!;

    /// <summary>
    /// Запускает сервер и ждёт публикации его адреса.
    /// </summary>
    public static async ValueTask<ObscuraServer> StartAsync(
        string executablePath,
        ObscuraServerOptions options,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(options);

        var runDirectory = IOPath.Combine(GetRunsRoot(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        var readyFile = IOPath.Combine(runDirectory, "ready.json");
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        var startInfo = BuildStartInfo(executablePath, options, readyFile, runDirectory, token);
        Process process;

        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Не удалось запустить '{executablePath}'");
        }
        catch
        {
            TryDeleteDirectory(runDirectory);
            throw;
        }

        var server = new ObscuraServer(process, runDirectory, token);

        try
        {
            server.BeginDrainOutput();

            BridgeBrowserProcessRegistry.Register(process.Id, runDirectory);
            BridgeBrowserProcessRegistry.SpawnParentDeathWatchdog(process.Id, runDirectory);

            server.BrowserEndpoint = await server.WaitForReadyAsync(readyFile, options.StartupTimeout, cancellationToken).ConfigureAwait(false);
            logger?.LogObscuraServerStarted(process.Id, server.BrowserEndpoint.ToString());
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Открывает CDP-соединение с браузерной целью сервера.
    /// </summary>
    public ValueTask<CdpConnection> ConnectAsync(CancellationToken cancellationToken)
        => CdpConnection.ConnectAsync(BrowserEndpoint, Token, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
            return;

        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            // Процесс уже завершился или не успел — запись реестра снимется, сирота уйдёт со sweep.
        }

        BridgeBrowserProcessRegistry.Unregister(SafeProcessId());
        Process.Dispose();
        TryDeleteDirectory(RunDirectory);
    }

    internal static ProcessStartInfo BuildStartInfo(
        string executablePath,
        ObscuraServerOptions options,
        string readyFile,
        string runDirectory,
        string token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = runDirectory,
        };

        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("--ready-file");
        startInfo.ArgumentList.Add(readyFile);
        startInfo.ArgumentList.Add("--quiet");

        if (options.UseStealth)
            startInfo.ArgumentList.Add("--stealth");

        if (options.AllowPrivateNetwork)
            startInfo.ArgumentList.Add("--allow-private-network");

        if (!string.IsNullOrWhiteSpace(options.UserAgent))
        {
            startInfo.ArgumentList.Add("--user-agent");
            startInfo.ArgumentList.Add(options.UserAgent);
        }

        foreach (var argument in options.ExtraArguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment["OBSCURA_CDP_TOKEN"] = token;
        startInfo.Environment.Remove("OBSCURA_PROXY");

        if (options.ProxyUrl is { } proxyUrl)
            startInfo.Environment["OBSCURA_PROXY"] = proxyUrl;

        if (!string.IsNullOrWhiteSpace(options.TimeZone))
            startInfo.Environment["OBSCURA_TIMEZONE"] = options.TimeZone;

        if (options.Geolocation is { } geolocation)
        {
            startInfo.Environment["OBSCURA_GEOLOCATION"] = string.Create(
                CultureInfo.InvariantCulture,
                $"{geolocation.Latitude},{geolocation.Longitude}");
        }

        return startInfo;
    }

    /// <summary>
    /// Переводит <see cref="IWebProxy"/> в URL прокси Obscura (с учётными данными, если они заданы).
    /// </summary>
    internal static string? ResolveProxyUrl(IWebProxy? proxy)
    {
        if (proxy is null)
            return null;

        var probe = ProxyProbeUri;
        var address = proxy is WebProxy webProxy ? webProxy.Address : proxy.GetProxy(probe);
        if (address is null || address == probe)
            return null;

        var builder = new UriBuilder(address);
        if (proxy.Credentials?.GetCredential(address, "Basic") is { UserName.Length: > 0 } credential)
        {
            builder.UserName = Uri.EscapeDataString(credential.UserName);
            builder.Password = Uri.EscapeDataString(credential.Password);
        }

        return builder.Uri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.Path, UriFormat.UriEscaped);
    }

    internal static Uri ParseReadyFile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var host = root.TryGetProperty("host", out var hostElement) ? hostElement.GetString() : null;
        var port = root.TryGetProperty("port", out var portElement) && portElement.TryGetInt32(out var parsedPort) ? parsedPort : 0;
        var path = root.TryGetProperty("websocket_path", out var pathElement) ? pathElement.GetString() : null;

        if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535 || string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Ready-файл Obscura не содержит адрес CDP-сервера");

        var authority = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
        return new Uri(string.Create(CultureInfo.InvariantCulture, $"ws://{authority}:{port}{path}"));
    }

    private static string GetRunsRoot()
        => IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".atom-webdriver", "obscura-runs");

    private async ValueTask<Uri> WaitForReadyAsync(string readyFile, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            while (true)
            {
                if (File.Exists(readyFile))
                    return ParseReadyFile(await File.ReadAllTextAsync(readyFile, deadline.Token).ConfigureAwait(false));

                if (Process.HasExited)
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Obscura завершилась с кодом {Process.ExitCode} до готовности CDP-сервера. stderr: {GetStderrTail()}"));
                }

                await Task.Delay(50, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                string.Create(CultureInfo.InvariantCulture, $"Obscura не опубликовала адрес CDP-сервера за {timeout.TotalSeconds:0.#} с. stderr: {GetStderrTail()}"),
                exception);
        }
    }

    private void BeginDrainOutput()
    {
        // Вывод нужно вычитывать, иначе заполненный канал заблокирует процесс; хвост stderr — для диагностики.
        Process.OutputDataReceived += static (_, _) => { };
        Process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
                return;

            lock (stderrTail)
            {
                stderrTail.AppendLine(args.Data);
                if (stderrTail.Length > StderrTailLimit)
                    stderrTail.Remove(0, stderrTail.Length - StderrTailLimit);
            }
        };
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    private string GetStderrTail()
    {
        lock (stderrTail)
        {
            return stderrTail.Length == 0 ? "<пусто>" : stderrTail.ToString().Trim();
        }
    }

    private int SafeProcessId()
    {
        try
        {
            return Process.Id;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Каталог подберёт startup-sweep следующего запуска.
        }
    }
}

/// <summary>
/// Параметры запуска <c lang="text">obscura serve</c>.
/// </summary>
internal sealed record ObscuraServerOptions
{
    public bool UseStealth { get; init; }

    public bool AllowPrivateNetwork { get; init; }

    public string? UserAgent { get; init; }

    /// <summary>URL прокси, включая учётные данные; передаётся через окружение.</summary>
    public string? ProxyUrl { get; init; }

    public string? TimeZone { get; init; }

    public (double Latitude, double Longitude)? Geolocation { get; init; }

    public IReadOnlyList<string> ExtraArguments { get; init; } = [];

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
