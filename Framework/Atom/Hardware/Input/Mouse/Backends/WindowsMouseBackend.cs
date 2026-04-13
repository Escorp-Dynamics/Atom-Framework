using System.Drawing;
using System.Runtime.Versioning;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Бэкенд виртуальной мыши для Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMouseBackend : IVirtualMouseBackend
{
    private const string NotImplementedMessage =
        "Windows бэкенд виртуальной мыши ещё не реализован. " +
        "Планируется поддержка через SendInput API.";

    /// <inheritdoc/>
    public string DeviceIdentifier => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public bool HasSeparateCursor => false;

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualMouseSettings settings, CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void MoveAbsolute(Point position)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void MoveRelative(Size delta)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void ButtonDown(VirtualMouseButton button)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void ButtonUp(VirtualMouseButton button)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void Scroll(int delta)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void ScrollHorizontal(int delta)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
