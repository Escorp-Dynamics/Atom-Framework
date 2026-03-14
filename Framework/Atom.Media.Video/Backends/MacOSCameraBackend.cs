using System.Runtime.Versioning;

namespace Atom.Media.Video.Backends;

/// <summary>
/// Бэкенд виртуальной камеры для macOS.
/// Использует CoreMediaIO Camera Extension (macOS 12.3+).
/// </summary>
[SupportedOSPlatform("osx")]
internal sealed class MacOSCameraBackend : IVirtualCameraBackend
{
    private const string NotImplementedMessage =
        "macOS бэкенд виртуальной камеры ещё не реализован. " +
        "Планируется поддержка через CoreMediaIO Camera Extension.";

    /// <inheritdoc/>
    public string DeviceIdentifier => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public bool IsCapturing => false;

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualCameraSettings settings, CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void WriteFrame(ReadOnlySpan<byte> frameData)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void SetControl(CameraControlType control, float value)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public float GetControl(CameraControlType control)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public CameraControlRange? GetControlRange(CameraControlType control)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

#pragma warning disable CS0067
    /// <inheritdoc/>
    public event EventHandler<CameraControlChangedEventArgs>? ControlChanged;
#pragma warning restore CS0067

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
