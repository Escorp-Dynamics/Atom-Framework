using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.Distribution;

/// <summary>
/// Представляет терминал операционной системы.
/// </summary>
public sealed class Terminal : IDisposable
{
    private readonly Distributive distribution;
    private Process[] runningProcesses = [];

    /// <summary>
    /// Пароль администратора.
    /// </summary>
    public string? RootPassword { get; set; }

    /// <summary>
    /// Возвращает коллекцию активных процессов терминала.
    /// </summary>
    public IEnumerable<ProcessInfo> RunningProcesses
    {
        get
        {
            var snapshot = Volatile.Read(ref runningProcesses);

            for (var i = 0; i < snapshot.Length; ++i)
            {
                yield return new ProcessInfo(snapshot[i], this);
            }
        }
    }

    internal Terminal(Distributive distribution) => this.distribution = distribution;

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    /// <returns>Информация о процессе.</returns>
    public ProcessInfo Run(string command)
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = distribution is Distributive.Windows ? "cmd.exe" : "/bin/bash",
                Arguments = distribution is Distributive.Windows ? $"/C {command}" : $"-c \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        RegisterProcess(process);

        try
        {
            process.Start();
        }
        catch
        {
            UnregisterProcess(process, dispose: true);
            throw;
        }

        return new ProcessInfo(process, this);
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
    public async ValueTask<bool> RunAndWaitAsync(string command, CancellationToken cancellationToken)
    {
        using var info = Run(command);
        return await info.WaitForEndingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> RunAndWaitAsync(string command) => RunAndWaitAsync(command, CancellationToken.None);

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask<bool> RunAsAdministratorAndWaitAsync(string command, CancellationToken cancellationToken)
    {
        using var info = RunAsAdministrator(command);
        return await info.WaitForEndingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Запускает команду на выполнение.
    /// </summary>
    /// <param name="command">Команда на выполнение в терминале.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> RunAsAdministratorAndWaitAsync(string command) => RunAsAdministratorAndWaitAsync(command, CancellationToken.None);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RegisterProcess(Process process)
    {
        process.Exited += OnProcessExited;
        AddProcess(process);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is Process process)
        {
            ReleaseProcess(process);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ReleaseProcess(Process process)
    {
        process.Exited -= OnProcessExited;
        return RemoveProcess(process);
    }

    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected", Justification = "Терминал владеет и освобождает процессы, которые создаёт")]
    private void UnregisterProcess(Process process, bool dispose)
    {
        if (dispose || ReleaseProcess(process)) process.Dispose();
    }

    /// <summary>
    /// Освобождает все активные процессы терминала.
    /// </summary>
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected", Justification = "Терминал создает и владеет экземплярами процессов")]
    public void Dispose()
    {
        foreach (var process in Interlocked.Exchange(ref runningProcesses, []))
        {
            process.Exited -= OnProcessExited;
            process.Dispose();
        }
    }

    private void AddProcess(Process process)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref runningProcesses);
            var length = snapshot.Length;
            var newArray = new Process[length + 1];
            if (length > 0) Array.Copy(snapshot, newArray, length);
            newArray[length] = process;

            if (ReferenceEquals(Interlocked.CompareExchange(ref runningProcesses, newArray, snapshot), snapshot)) return;
        }
    }

    private bool RemoveProcess(Process process)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref runningProcesses);
            var index = Array.IndexOf(snapshot, process);
            if (index < 0) return false;

            Process[] newArray;

            if (snapshot.Length == 1)
            {
                newArray = [];
            }
            else
            {
                newArray = new Process[snapshot.Length - 1];
                if (index > 0) Array.Copy(snapshot, 0, newArray, 0, index);
                if (index < snapshot.Length - 1) Array.Copy(snapshot, index + 1, newArray, index, snapshot.Length - index - 1);
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref runningProcesses, newArray, snapshot), snapshot)) return true;
        }
    }
}
