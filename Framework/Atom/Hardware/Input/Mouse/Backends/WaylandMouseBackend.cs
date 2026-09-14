using System.Drawing;
using System.Runtime.Versioning;

using Atom.Display.Wayland;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Мышь на собственном композиторе Wayland.
/// </summary>
/// <remarks>
/// ★ Заменяет XTEST. Прежний путь требовал работающего X-сервера, а координаты приходилось
/// пересчитывать в экранные — композитор же принимает их прямо в системе поверхности окна.
///
/// Событие подаётся в протокол и для страницы неотличимо от настоящего: приходит от оконной
/// системы, а значит с <c lang="text">isTrusted = true</c>.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandMouseBackend(WaylandInput input) : IVirtualMouseBackend
{
    private Point current;

    /// <inheritdoc/>
    public string DeviceIdentifier => "wayland-pointer";

    /// <summary>
    /// Отдельный курсор у устройства.
    /// </summary>
    /// <remarks>
    /// У композитора один указатель на сеанс, но сеансов столько, сколько браузеров: каждый на
    /// своём сокете. Борьбы за общий курсор, ради которой заводили MPX, здесь нет вовсе.
    /// </remarks>
    public bool HasSeparateCursor => true;

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualMouseSettings settings, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public void MoveAbsolute(Point position)
    {
        current = position;
        input.MoveTo(position.X, position.Y);
    }

    /// <inheritdoc/>
    public void MoveRelative(Size delta)
        => MoveAbsolute(new Point(current.X + delta.Width, current.Y + delta.Height));

    /// <inheritdoc/>
    public void ButtonDown(VirtualMouseButton button) => input.PointerDown(Translate(button));

    /// <inheritdoc/>
    public void ButtonUp(VirtualMouseButton button) => input.PointerUp(Translate(button));

    /// <inheritdoc/>
    public void Scroll(int delta) => input.SendPointerAxis(VerticalAxis, -delta);

    /// <inheritdoc/>
    public void ScrollHorizontal(int delta) => input.SendPointerAxis(HorizontalAxis, delta);

    private static WaylandPointerButton Translate(VirtualMouseButton button) => button switch
    {
        VirtualMouseButton.Right => WaylandPointerButton.Right,
        VirtualMouseButton.Middle => WaylandPointerButton.Middle,
        _ => WaylandPointerButton.Left,
    };

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private const uint VerticalAxis = 0;
    private const uint HorizontalAxis = 1;
}
