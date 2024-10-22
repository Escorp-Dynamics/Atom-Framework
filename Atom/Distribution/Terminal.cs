using System.Diagnostics;

namespace Atom.Distribution;

/// <summary>
/// Представляет терминал операционной системы.
/// </summary>
public class Terminal
{
    private readonly Distributive distribution;

    internal Terminal(Distributive distribution) => this.distribution = distribution;

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask<bool> RunAsync(string command, CancellationToken cancellationToken)
    {
        using var process = new Process();

        process.StartInfo.FileName = distribution is Distributive.Windows ? "cmd.exe" : "/bin/bash";
        process.StartInfo.Arguments = distribution is Distributive.Windows ? $"/C {command}" : $"-c \"{command}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode is 0;
    }

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    public ValueTask<bool> RunAsync(string command) => RunAsync(command, CancellationToken.None);
}