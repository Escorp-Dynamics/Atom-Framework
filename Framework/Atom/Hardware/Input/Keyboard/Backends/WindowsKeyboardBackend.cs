using System.Runtime.Versioning;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Бэкенд виртуальной клавиатуры для Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsKeyboardBackend : IVirtualKeyboardBackend
{
    private const string NotImplementedMessage =
        "Windows бэкенд виртуальной клавиатуры ещё не реализован. " +
        "Планируется поддержка через SendInput API.";

    /// <inheritdoc/>
    public string DeviceIdentifier => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualKeyboardSettings settings, CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void KeyDown(ConsoleKey key)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void KeyUp(ConsoleKey key)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void ModifierDown(ConsoleModifiers modifier)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void ModifierUp(ConsoleModifiers modifier)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
