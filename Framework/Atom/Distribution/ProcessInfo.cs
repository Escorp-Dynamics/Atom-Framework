using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Atom.Distribution;

/// <summary>
/// Представляет информацию о процессе терминала.
/// </summary>
public readonly struct ProcessInfo : IEquatable<ProcessInfo>, IDisposable
{
    private readonly Process process;
    private readonly Terminal? terminal;

    /// <summary>
    /// Поток вывода.
    /// </summary>
    public readonly StreamReader Output => process.StandardOutput;

    /// <summary>
    /// Поток ошибок.
    /// </summary>
    public readonly StreamReader Error => process.StandardError;

    /// <summary>
    /// Определяет, был ли процесс завершён.
    /// </summary>
    public readonly bool IsRunning => !process.HasExited;

    /// <summary>
    /// Код завершения процесса.
    /// </summary>
    public readonly int ExitCode => process.ExitCode;

    /// <summary>
    /// Определяет, был ли процесс успешно завершён.
    /// </summary>
    public readonly bool IsSuccessExiting => ExitCode is 0;

    internal ProcessInfo(Process process, Terminal terminal)
    {
        this.process = process;
        this.terminal = terminal;
    }

    /// <summary>
    /// Ожидает завершения процесса.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если процесс был закрыт успешно, иначе <see langword="false"/>.</returns>
    public readonly async ValueTask<bool> WaitForEndingAsync(CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return IsSuccessExiting;
    }

    /// <summary>
    /// Ожидает завершения процесса.
    /// </summary>
    /// <returns><c>True</c>, если процесс был закрыт успешно, иначе <see langword="false"/>.</returns>
    public readonly ValueTask<bool> WaitForEndingAsync() => WaitForEndingAsync(CancellationToken.None);

    /// <summary>
    /// Убивает процесс.
    /// </summary>
    /// <param name="entireProcessTree">Указывает, нужно ли так же убить связанные процессы.</param>
    public void Kill(bool entireProcessTree)
    {
        if (IsRunning) process.Kill(entireProcessTree);
    }

    /// <summary>
    /// Убивает процесс.
    /// </summary>
    public void Kill()
    {
        if (IsRunning) process.Kill();
    }

    /// <summary>
    /// Получает хэш-код текущего экземпляра.
    /// </summary>
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>
    /// Сравнивает объект с текущим экземпляром.
    /// </summary>
    /// <param name="obj">Сравниваемый объект.</param>
    /// <returns><c>True</c>, если экземпляры равны, иначе <see langword="false"/>.</returns>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ProcessInfo info && Equals(info);

    /// <summary>
    /// Сравнивает объект с текущим экземпляром.
    /// </summary>
    /// <param name="other">Сравниваемый объект.</param>
    /// <returns><c>True</c>, если экземпляры равны, иначе <see langword="false"/>.</returns>
    public bool Equals(ProcessInfo other) => process.Id == other.process.Id;

    /// <summary>
    /// Сравнивает два экземпляра <see cref="ProcessInfo"/>.
    /// </summary>
    /// <param name="left">Экземпляр слева.</param>
    /// <param name="right">Экземпляр справа.</param>
    /// <returns><c>True</c>, если экземпляры равны, иначе <see langword="false"/>.</returns>
    public static bool operator ==(ProcessInfo left, ProcessInfo right) => left.Equals(right);

    /// <summary>
    /// Сравнивает два экземпляра <see cref="ProcessInfo"/>.
    /// </summary>
    /// <param name="left">Экземпляр слева.</param>
    /// <param name="right">Экземпляр справа.</param>
    /// <returns><c>True</c>, если экземпляры не равны, иначе <see langword="false"/>.</returns>
    public static bool operator !=(ProcessInfo left, ProcessInfo right) => !left.Equals(right);

    /// <summary>
    /// Освобождает ресурсы процесса.
    /// </summary>
    [SuppressMessage("IDisposableAnalyzers.Correctness", "IDISP007:Don't dispose injected", Justification = "Объект владеет процессом, созданным терминалом")]
    public readonly void Dispose()
    {
        if (terminal is null) return;
        if (terminal.ReleaseProcess(process)) process.Dispose();
    }
}