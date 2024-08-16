using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Atom.Net.Http;
using Atom.Web.Browsers.NativeMessaging.Signals;

namespace Atom.Web.Browsers.NativeMessaging;

/// <summary>
/// Предоставляет доступ к веб-браузерам через нативное сообщение.
/// </summary>
public abstract class WebBrowserServer : IWebBrowserServer
{
    private readonly Process stdio;

    /// <inheritdoc/>
    public Manifest Manifest { get; protected set; }

    /// <inheritdoc/>
    public bool IsRunning { get; protected set; }

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowserServer>? Started;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowserServer>? Stopped;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowserServer, SignalReceivedAsyncEventArgs>? SignalReceived;

    /// <inheritdoc/>
    public event AsyncEventHandler<IWebBrowserServer, FailedEventArgs>? SignalReceiveFailed;

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="WebBrowserServer"/>.
    /// </summary>
    /// <param name="manifest">Манифест сервера.</param>
    protected WebBrowserServer(Manifest manifest)
    {
        stdio = new Process();
        Manifest = manifest;
    }

    private async ValueTask OnDataReceived(string? source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(source) || !source.StartsWith("@atom:")) return;

        var parts = source[6..].Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is not 2 || string.IsNullOrEmpty(parts[0])) return;

        var signalName = parts[0];

        if (string.IsNullOrEmpty(parts[1]))
        {
            if (!Signal.TryGetByName(signalName, out var signal) || signal is null)
            {
                await OnSignalReceiveFailed(new FailedEventArgs
                {
                    Exception = new InvalidDataException($"Неизвестный тип сигнала: {signalName}"),
                    CancellationToken = cancellationToken
                }).ConfigureAwait(false);

                return;
            }

            await OnSignalReceived(new SignalReceivedAsyncEventArgs(signal) { CancellationToken = cancellationToken }).ConfigureAwait(false);
            return;
        }

        var buffer = Convert.FromBase64String(parts[1]);
        var json = Encoding.UTF8.GetString(buffer);

        try
        {
            var properties = JsonSerializer.Deserialize(json, JsonHttpContext.Default.Form);

            if (!Signal.TryGetByName(signalName, properties!, out var signal) || signal is null)
            {
                await OnSignalReceiveFailed(new FailedEventArgs
                {
                    Exception = new InvalidDataException($"Неизвестный тип сигнала: {signalName}"),
                    CancellationToken = cancellationToken
                }).ConfigureAwait(false);

                return;
            }

            await OnSignalReceived(new SignalReceivedAsyncEventArgs(signal) { CancellationToken = cancellationToken }).ConfigureAwait(false);
            return;
        }
        catch (JsonException ex)
        {
            await OnSignalReceiveFailed(new FailedEventArgs
            {
                Exception = ex,
                CancellationToken = cancellationToken
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Происходит в момент получения сигнала с клиента.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    protected virtual async ValueTask OnSignalReceived(SignalReceivedAsyncEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.IsCancelled) return;

        await SignalReceived.On(this, e).ConfigureAwait(false);
    }

    /// <summary>
    /// Происходит в момент неудачного получения сигнала.
    /// </summary>
    /// <param name="e">Аргументы события.</param>
    protected virtual ValueTask OnSignalReceiveFailed(FailedEventArgs e) => SignalReceiveFailed.On(this, e);

    private async ValueTask<string> CreateStdioAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(Environment.CurrentDirectory, "stdio.");
        //var symlink = Path.Combine(Environment.CurrentDirectory, "atom");
        var sb = new StringBuilder();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            sb.AppendLine("while ($true) {");
            sb.AppendLine("    $rawLength = [System.BitConverter]::ToUInt32([System.Console]::Read(), 0)");
            sb.AppendLine("    if ($rawLength -eq 0) {");
            sb.AppendLine("        exit 0");
            sb.AppendLine("    }");
            sb.AppendLine("    $messageBytes = [System.Console]::Read($rawLength)");
            sb.AppendLine("    $message = [System.Text.Encoding]::UTF8.GetString($messageBytes)");
            sb.AppendLine("    Write-Host $message");
            sb.AppendLine("}");
        }
        else
        {
            sb.AppendLine($"#!/bin/bash");
            sb.AppendLine("while true; do");
            sb.AppendLine("    cat > /tmp/atom.in");
            sb.AppendLine("done");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            path += "ps1";
        else
            path += "sh";

        //if (File.Exists(symlink)) File.Delete(symlink);
        //File.CreateSymbolicLink(symlink, path);

        await File.WriteAllTextAsync(path, sb.ToString(), cancellationToken).ConfigureAwait(false);

        /*if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead
                                    | UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite
                                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);*/

        stdio.StartInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        
        //stdio.OutputDataReceived += async (sender, args) => await OnDataReceived(args.Data, cancellationToken).ConfigureAwait(false);
        stdio.Start();

        _ = Task.Run(async () =>
        {
            while (IsRunning)
            {
                var lengthBuffer = new Memory<byte>(new byte[4]);
                await stdio.StandardOutput.BaseStream.ReadAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
                var length = BitConverter.ToInt32(lengthBuffer.ToArray(), 0);

                var dataBuffer = new Memory<byte>(new byte[length]);
                await stdio.StandardOutput.BaseStream.ReadAsync(dataBuffer, cancellationToken).ConfigureAwait(false);
                await OnDataReceived(Encoding.UTF8.GetString(dataBuffer.Span), cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

        return path;
    }

    /// <summary>
    /// Происходит в момент запуска браузерного сервера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    protected virtual async ValueTask OnStarted(CancellationToken cancellationToken)
    {
        Manifest.Path = await CreateStdioAsync(cancellationToken).ConfigureAwait(false); ;

        var json = JsonSerializer.Serialize(Manifest, JsonManifestContext.Default.Manifest);
        var path = GetManifestPath();

        var dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

        await File.WriteAllTextAsync(Path.GetFullPath(path), json, cancellationToken).ConfigureAwait(false);

        await Started.On(this).ConfigureAwait(false);
    }

    /// <summary>
    /// Происходит в момент остановки браузерного сервера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    protected virtual ValueTask OnStopped(CancellationToken cancellationToken) => Stopped.On(this);

    /// <summary>
    /// Возвращает путь к файлу манифеста.
    /// </summary>
    protected abstract string GetManifestPath();

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning) return;
        IsRunning = true;

        await OnStarted(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask StartAsync() => StartAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (!IsRunning) return;
        IsRunning = false;

        var path = GetManifestPath();
        if (File.Exists(path)) File.Delete(path);

        stdio.Kill(true);

        await OnStopped(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask StopAsync() => StopAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        stdio.Dispose();
        GC.SuppressFinalize(this);
    }
}