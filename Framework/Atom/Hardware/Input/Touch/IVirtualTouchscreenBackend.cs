using System.Drawing;

namespace Atom.Hardware.Input.Touch;

/// <summary>
/// Платформенный бэкенд виртуального тачскрина.
/// </summary>
internal interface IVirtualTouchscreenBackend : IAsyncDisposable
{
    /// <summary>Идентификатор созданного устройства.</summary>
    string DeviceIdentifier { get; }

    /// <summary>Создаёт устройство в системе.</summary>
    ValueTask InitializeAsync(VirtualTouchscreenSettings settings, CancellationToken cancellationToken);

    /// <summary>Одиночное касание в точке экрана.</summary>
    ValueTask TapAsync(Point point, TimeSpan holdDuration, CancellationToken cancellationToken);

    /// <summary>Проведение пальцем по траектории.</summary>
    ValueTask SwipeAsync(IReadOnlyList<Point> path, TimeSpan duration, CancellationToken cancellationToken);
}
