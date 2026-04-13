#pragma warning disable MA0051

using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Бэкенд виртуальной мыши для X11 через расширение XTEST.
/// Инжектит события мыши напрямую в указанный X-сервер,
/// не затрагивая другие дисплеи или физическую мышь.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class XTestMouseBackend : IVirtualMouseBackend
{
    private nint display;
    private int screenNumber;
    private bool isDisposed;
    private Point? lastAbsoluteMovePosition;
    private Point? lastPointerPositionBeforeButtonDown;
    private Point? lastPointerPositionAfterButtonUp;

    /// <inheritdoc/>
    public string DeviceIdentifier { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasSeparateCursor => false;

    internal Point? LastAbsoluteMovePosition => lastAbsoluteMovePosition;

    internal Point? LastPointerPositionBeforeButtonDown => lastPointerPositionBeforeButtonDown;

    internal Point? LastPointerPositionInChildWindowBeforeButtonDown { get; private set; }

    internal Point? LastPointerPositionAfterButtonUp => lastPointerPositionAfterButtonUp;

    internal nint LastPointerChildWindowBeforeButtonDown { get; private set; }

    /// <summary>
    /// Строка дисплея X11 (например, <c>:99</c>).
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public ValueTask InitializeAsync(VirtualMouseSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new VirtualMouseException("DisplayName не задан для XTEST-мыши.");

        display = XOpenDisplay(DisplayName);
        if (display == nint.Zero)
        {
            throw new VirtualMouseException(
                "Не удалось подключиться к X-серверу " + DisplayName + ". " +
                "Убедитесь что целевой X11 display доступен и DISPLAY задан корректно.");
        }

        screenNumber = XDefaultScreen(display);

        // Проверяем наличие XTEST расширения.
        var hasXTest = XTestQueryExtension(
            display, out _, out _, out _, out _);

        if (!hasXTest)
        {
            _ = XCloseDisplay(display);
            display = nint.Zero;
            throw new VirtualMouseException(
                "X-сервер " + DisplayName + " не поддерживает расширение XTEST.");
        }

        DeviceIdentifier = "xtest:" + DisplayName;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Задаёт строку дисплея для подключения.
    /// Должен быть вызван перед <see cref="InitializeAsync"/>.
    /// </summary>
    internal void SetDisplayName(string displayName) =>
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("DisplayName не может быть пустым.", nameof(displayName))
            : displayName;

    /// <inheritdoc/>
    public void MoveAbsolute(Point position)
    {
        ThrowIfUnavailable();
        lastAbsoluteMovePosition = position;
        _ = XTestFakeMotionEvent(display, screenNumber, position.X, position.Y, delay: 0);
        _ = XFlush(display);
    }

    /// <inheritdoc/>
    public void MoveRelative(Size delta)
    {
        ThrowIfUnavailable();
        _ = XTestFakeRelativeMotionEvent(display, delta.Width, delta.Height, delay: 0);
        _ = XFlush(display);
    }

    /// <inheritdoc/>
    public void ButtonDown(VirtualMouseButton button)
    {
        ThrowIfUnavailable();
        _ = TryQueryPointerState(out lastPointerPositionBeforeButtonDown, out var childWindow, out var childPosition);
        LastPointerChildWindowBeforeButtonDown = childWindow;
        LastPointerPositionInChildWindowBeforeButtonDown = childPosition;
        _ = XTestFakeButtonEvent(display, MapButton(button), isPress: 1, delay: 0);
        _ = XFlush(display);
    }

    /// <inheritdoc/>
    public void ButtonUp(VirtualMouseButton button)
    {
        ThrowIfUnavailable();
        _ = XTestFakeButtonEvent(display, MapButton(button), isPress: 0, delay: 0);
        _ = XFlush(display);
        lastPointerPositionAfterButtonUp = QueryPointerPosition();
    }

    /// <inheritdoc/>
    public void Scroll(int delta)
    {
        ThrowIfUnavailable();
        // В X11 scroll — это кнопки 4 (вверх) и 5 (вниз).
        var button = (uint)(delta > 0 ? 4 : 5);
        var count = Math.Abs(delta);
        for (var i = 0; i < count; i++)
        {
            _ = XTestFakeButtonEvent(display, button, isPress: 1, delay: 0);
            _ = XTestFakeButtonEvent(display, button, isPress: 0, delay: 0);
        }

        _ = XFlush(display);
    }

    /// <inheritdoc/>
    public void ScrollHorizontal(int delta)
    {
        ThrowIfUnavailable();
        // Кнопки 6 (влево) и 7 (вправо).
        var button = (uint)(delta > 0 ? 7 : 6);
        var count = Math.Abs(delta);
        for (var i = 0; i < count; i++)
        {
            _ = XTestFakeButtonEvent(display, button, isPress: 1, delay: 0);
            _ = XTestFakeButtonEvent(display, button, isPress: 0, delay: 0);
        }

        _ = XFlush(display);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, value: true))
            return ValueTask.CompletedTask;

        if (display != nint.Zero)
        {
            _ = XCloseDisplay(display);
            display = nint.Zero;
        }

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

    private void ThrowIfUnavailable()
    {
        ThrowIfDisposed();

        if (display == nint.Zero)
            throw new VirtualMouseException("XTEST-мышь не инициализирована или уже отключена от X-сервера.");
    }

    private Point? QueryPointerPosition()
    {
        _ = TryQueryPointerState(out var rootPosition, out _, out _);
        return rootPosition;
    }

    private bool TryQueryPointerState(out Point? rootPosition, out nint childWindow, out Point? childPosition)
    {
        var rootWindow = XRootWindow(display, screenNumber);
        if (!XQueryPointer(
            display,
            rootWindow,
            out _,
            out var resolvedChildWindow,
            out var rootX,
            out var rootY,
            out var windowX,
            out var windowY,
            out _))
        {
            rootPosition = null;
            childWindow = nint.Zero;
            childPosition = null;
            return false;
        }

        rootPosition = new Point(rootX, rootY);
        childWindow = resolvedChildWindow;
        childPosition = null;

        if (resolvedChildWindow != nint.Zero)
            childPosition = new Point(windowX, windowY);

        return true;
    }

    private static uint MapButton(VirtualMouseButton button) => button switch
    {
        VirtualMouseButton.Left => 1,
        VirtualMouseButton.Middle => 2,
        VirtualMouseButton.Right => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Неизвестная кнопка мыши."),
    };

    #region Native interop

    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XOpenDisplay(string? displayName);

    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XDefaultScreen(nint display);

    [LibraryImport("libX11.so.6", EntryPoint = "XRootWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint XRootWindow(nint display, int screenNumber);

    [LibraryImport("libX11.so.6", EntryPoint = "XQueryPointer")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool XQueryPointer(
        nint display,
        nint w,
        out nint rootReturn,
        out nint childReturn,
        out int rootXReturn,
        out int rootYReturn,
        out int winXReturn,
        out int winYReturn,
        out uint maskReturn);

    [LibraryImport("libX11.so.6", EntryPoint = "XFlush")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XFlush(nint display);

    [LibraryImport("libXtst.so.6", EntryPoint = "XTestQueryExtension")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool XTestQueryExtension(
        nint display,
        out int eventBasep,
        out int errorBasep,
        out int majorVersionp,
        out int minorVersionp);

    [LibraryImport("libXtst.so.6", EntryPoint = "XTestFakeMotionEvent")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XTestFakeMotionEvent(
        nint display, int screenNumber, int x, int y, nuint delay);

    [LibraryImport("libXtst.so.6", EntryPoint = "XTestFakeRelativeMotionEvent")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XTestFakeRelativeMotionEvent(
        nint display, int dx, int dy, nuint delay);

    [LibraryImport("libXtst.so.6", EntryPoint = "XTestFakeButtonEvent")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int XTestFakeButtonEvent(
        nint display, uint button, int isPress, nuint delay);

    #endregion
}
