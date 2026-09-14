using System.Drawing;
using System.Runtime.Versioning;

using Atom.Hardware.Input.Touch.Backends;

namespace Atom.Hardware.Input.Touch;

/// <summary>
/// Виртуальный сенсорный экран уровня ядра.
/// </summary>
/// <remarks>
/// Нужен там, где заявленное устройство мобильное. Мышиный ввод по такому документу невозможен
/// физически, и это видно по типу указателя в каждом событии: у настоящего касания
/// <c lang="text">pointerType</c> равен «touch», а у мыши — «mouse». Синтетические события из страницы
/// не годятся: они приходят с <c lang="text">isTrusted = false</c>.
/// </remarks>
public sealed class VirtualTouchscreen : IAsyncDisposable
{
    private readonly IVirtualTouchscreenBackend backend;
    private int isDisposed;

    private VirtualTouchscreen(IVirtualTouchscreenBackend backend) => this.backend = backend;

    /// <summary>Идентификатор устройства в системе.</summary>
    public string DeviceIdentifier => backend.DeviceIdentifier;

    /// <summary>
    /// Создаёт тачскрин для сеанса собственного композитора.
    /// </summary>
    /// <remarks>
    /// ★ Предпочтительный путь на Linux. Прежний требовал <c lang="text">/dev/uinput</c>, прав,
    /// udev-правил и совпадения матрицы устройства с размером выхода; здесь касание подаётся прямо
    /// в протокол в координатах поверхности.
    /// </remarks>
    /// <param name="session">Сеанс дисплея.</param>
    /// <param name="settings">Настройки тачскрина.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    [SupportedOSPlatform("linux")]
#pragma warning disable CA2000 // Владение бэкендом переходит устройству.
    public static async ValueTask<VirtualTouchscreen> CreateForSessionAsync(
        Atom.Display.WaylandDisplaySession session,
        VirtualTouchscreenSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var effectiveSettings = (settings ?? new VirtualTouchscreenSettings()) with
        {
            ScreenSize = session.Resolution,
        };

        var created = new WaylandTouchscreenBackend(session.Input);
        await created.InitializeAsync(effectiveSettings, cancellationToken).ConfigureAwait(false);

        return new VirtualTouchscreen(created);
    }
#pragma warning restore CA2000

    /// <summary>
    /// Создаёт устройство и ждёт его регистрации в системе.
    /// </summary>
    public static async ValueTask<VirtualTouchscreen> CreateAsync(
        VirtualTouchscreenSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Виртуальный тачскрин пока реализован только для Linux.");

        // Владение переходит созданному VirtualTouchscreen либо освобождается в catch; анализатор
        // передачи через конструктор не видит.
#pragma warning disable CA2000
        var created = new UinputTouchscreenBackend();
#pragma warning restore CA2000

        try
        {
            await created.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await created.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new VirtualTouchscreen(created);
    }

    /// <summary>
    /// Одиночное касание по абсолютной экранной точке.
    /// </summary>
    /// <param name="point">Точка экрана.</param>
    /// <param name="holdDuration">Сколько держится палец; по умолчанию — как при обычном тапе.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public ValueTask TapAsync(Point point, TimeSpan holdDuration = default, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) is not 0, this);
        return backend.TapAsync(point, holdDuration, cancellationToken);
    }

    /// <summary>
    /// Проведение пальцем по траектории без отрыва.
    /// </summary>
    /// <param name="path">Точки экрана, минимум две.</param>
    /// <param name="duration">Длительность всего жеста; по умолчанию — как у обычного свайпа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public ValueTask SwipeAsync(IReadOnlyList<Point> path, TimeSpan duration = default, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) is not 0, this);
        return backend.SwipeAsync(path, duration, cancellationToken);
    }

    /// <summary>
    /// Проведение по прямой между двумя точками.
    /// </summary>
    /// <remarks>
    /// Траектория строится с промежуточными точками: настоящий палец не телепортируется, и жест
    /// из двух событий отличим от живого по одному лишь числу отсчётов.
    /// </remarks>
    public ValueTask SwipeAsync(Point from, Point to, TimeSpan duration = default, CancellationToken cancellationToken = default)
    {
        const int steps = 12;
        var path = new List<Point>(steps + 1);

        for (var index = 0; index <= steps; ++index)
        {
            var progress = (double)index / steps;

            // Разгон и торможение: палец не движется равномерно от начала до конца.
            var eased = progress < 0.5
                ? 2 * progress * progress
                : 1 - (Math.Pow((-2 * progress) + 2, 2) / 2);

            path.Add(new Point(
                (int)Math.Round(from.X + ((to.X - from.X) * eased)),
                (int)Math.Round(from.Y + ((to.Y - from.Y) * eased))));
        }

        return SwipeAsync(path, duration, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
            return;

        await backend.DisposeAsync().ConfigureAwait(false);
    }
}
