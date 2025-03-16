using System.Diagnostics;

namespace Atom.Distribution;

/// <summary>
/// Представляет терминал операционной системы.
/// </summary>
public partial class Terminal
{
    private readonly Distributive distribution;
    private readonly List<ProcessInfo> runningProcess = [];

    /// <summary>
    /// Пароль администратора.
    /// </summary>
    public string? RootPassword { get; set; }

    /// <summary>
    /// Коллекция активных процессов терминала.
    /// </summary>
    public IEnumerable<ProcessInfo> RunningProcess => runningProcess;

    internal Terminal(Distributive distribution) => this.distribution = distribution;

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    /// <returns>Информация о процессе.</returns>
    public ProcessInfo Run(string command)
    {
        var process = new Process();
        var info = new ProcessInfo(process);
        runningProcess.Add(info);

        process.StartInfo.FileName = distribution is Distributive.Windows ? "cmd.exe" : "/bin/bash";
        process.StartInfo.Arguments = distribution is Distributive.Windows ? $"/C {command}" : $"-c \"{command}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        process.Exited += (s, e) => runningProcess.Remove(info);

        process.Start();
        return new ProcessInfo(process);
    }

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    /// <returns>Информация о процессе.</returns>
    public ProcessInfo RunAsAdministrator(string command)
    {
        if (!string.IsNullOrEmpty(RootPassword)) command = $"echo \"{RootPassword}\" | sudo -S {command}";
        return Run(command);
    }

    /// <summary>
    /// Запускает команду на выполнение и ожидает её завершения.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask<bool> RunAndWaitAsync(string command, CancellationToken cancellationToken)
    {
        var info = Run(command);
        return info.WaitForEndingAsync(cancellationToken);
    }

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    public ValueTask<bool> RunAndWaitAsync(string command) => RunAndWaitAsync(command, CancellationToken.None);

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask<bool> RunAsAdministratorAndWaitAsync(string command, CancellationToken cancellationToken)
    {
        var info = RunAsAdministrator(command);
        return info.WaitForEndingAsync(cancellationToken);
    }

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    public ValueTask<bool> RunAsAdministratorAndWaitAsync(string command) => RunAsAdministratorAndWaitAsync(command, CancellationToken.None);
}