using System.Runtime.InteropServices;

namespace Atom;

/// <summary>
/// Представляет нативное исключение.
/// </summary>
public partial class NativeException : Exception
{
    /// <summary>
    /// Нативное сообщение.
    /// </summary>
    public string? NativeMessage { get; private set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="NativeException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    /// <param name="innerException">Внутреннее исключение.</param>
    public NativeException(string? message, Exception? innerException) : base(message, innerException) => NativeMessage = GetError();

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="NativeException"/>.
    /// </summary>
    /// <param name="message">Сообщение об ошибке.</param>
    public NativeException(string? message) : base(message) => NativeMessage = GetError();

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="NativeException"/>.
    /// </summary>
    public NativeException() : base() => NativeMessage = GetError();

    [LibraryImport("libc", EntryPoint = "strerror", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint ErrorUnix(int errorNo);

    private static unsafe string? GetError() => OperatingSystem.IsWindows()
        ? Marshal.GetLastPInvokeErrorMessage()
        : Marshal.PtrToStringAnsi(ErrorUnix(Marshal.GetLastWin32Error()));
}