using System.Runtime.Versioning;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Бэкенд виртуальной клавиатуры для macOS.
/// </summary>
[SupportedOSPlatform("osx")]
internal sealed class MacOSKeyboardBackend : IVirtualKeyboardBackend
{
    private const string NotImplementedMessage =
        "macOS бэкенд виртуальной клавиатуры ещё не реализован. " +
        "Планируется поддержка через CGEvent API.";

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
