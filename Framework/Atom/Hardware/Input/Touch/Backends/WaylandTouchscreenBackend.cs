using System.Drawing;
using System.Runtime.Versioning;

using Atom.Display.Wayland;

namespace Atom.Hardware.Input.Touch.Backends;

/// <summary>
/// Тачскрин на собственном композиторе Wayland.
/// </summary>
/// <remarks>
/// ★ Заменяет устройство ядра через <c lang="text">/dev/uinput</c>. Прежний путь требовал прав,
/// udev-правил, службы seat и совпадения матрицы устройства с размером выхода — иначе касания
/// уходили мимо. Здесь событие подаётся прямо в протокол в координатах поверхности.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandTouchscreenBackend(WaylandInput input) : IVirtualTouchscreenBackend
{

    /// <inheritdoc/>
    public string DeviceIdentifier => "wayland-touch";

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualTouchscreenSettings settings, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask TapAsync(Point point, TimeSpan holdDuration, CancellationToken cancellationToken)
        => input.TapAsync(point.X, point.Y, holdDuration, cancellationToken);

    /// <inheritdoc/>
    public ValueTask SwipeAsync(IReadOnlyList<Point> path, TimeSpan duration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        var points = new (double X, double Y)[path.Count];

        for (var index = 0; index < path.Count; ++index)
            points[index] = (path[index].X, path[index].Y);

        return input.SwipeAsync(points, duration, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
