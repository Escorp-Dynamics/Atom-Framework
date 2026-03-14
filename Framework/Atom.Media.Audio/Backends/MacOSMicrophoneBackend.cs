using System.Runtime.Versioning;

namespace Atom.Media.Audio.Backends;

/// <summary>
/// Бэкенд виртуального микрофона для macOS.
/// Использует CoreAudio HAL Audio Server Plugin.
/// </summary>
[SupportedOSPlatform("osx")]
internal sealed class MacOSMicrophoneBackend : IVirtualMicrophoneBackend
{
    private const string NotImplementedMessage =
        "macOS бэкенд виртуального микрофона ещё не реализован. " +
        "Планируется поддержка через CoreAudio HAL Plugin.";

    /// <inheritdoc/>
    public string DeviceIdentifier => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public bool IsCapturing => false;

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualMicrophoneSettings settings, CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void WriteSamples(ReadOnlySpan<byte> sampleData)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public void SetControl(MicrophoneControlType control, float value)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public float GetControl(MicrophoneControlType control)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

    /// <inheritdoc/>
    public MicrophoneControlRange? GetControlRange(MicrophoneControlType control)
        => throw new PlatformNotSupportedException(NotImplementedMessage);

#pragma warning disable CS0067
    /// <inheritdoc/>
    public event EventHandler<MicrophoneControlChangedEventArgs>? ControlChanged;
#pragma warning restore CS0067

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
