using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Presentation;

/// <summary>
/// Окно в сессии разработчика, показывающее кадры браузера.
/// </summary>
/// <remarks>
/// ★ Только для отладки, включается флагом <see cref="WaylandCompositorSettings.EnablePresentation"/>.
/// Композитор подключается к сессии разработчика как обычный клиент, заводит там окно и копирует в
/// него пиксели, пришедшие от браузера, — так же поступают вложенные композиторы.
///
/// Зачем копия, а не проброс буфера: буфер браузера живёт в его памяти и должен быть возвращён
/// сразу после коммита, иначе отрисовка встанет. Копирование в свою память развязывает эти сроки.
///
/// Окно двустороннее. Вниз идут кадры, вверх — ввод разработчика и решения оболочки хоста
/// (новый размер, закрытие). Запросы самого браузера — перетаскивание за заголовок, разворот,
/// сворачивание — переадресуются настоящей оболочке: для браузера оболочка это мы, и без
/// переадресации его кнопки были бы нарисованы, но мертвы.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed partial class WaylandPresentationWindow : IDisposable
{
    private readonly WaylandHostConnection host;
    private readonly string title;
    private readonly uint registry;
    private Size surfaceSize;
    private uint compositorGlobal;
    private uint shmGlobal;
    private uint shellGlobal;
    private uint seatGlobal;
    private uint decorationManager;
    private uint decoration;
    private uint pointer;
    private uint surface;
    private uint shellSurface;
    private uint toplevel;
    private uint pool;
    private uint buffer;
    private uint lastEnterSerial;
    private uint lastButtonSerial;
    private nint memory;
    private int memoryDescriptor = -1;
    private int memoryBytes;
    private double pointerX;
    private double pointerY;
    private Size pendingSize;
    private byte[] lastFrame = [];
    private int lastFrameStride;
    private Size lastFrameSize;
    private bool isConfigured;
    private int isDisposed;

    private WaylandPresentationWindow(WaylandHostConnection host, Size initialSize, string title, string? applicationId, uint registry)
    {
        this.host = host;
        this.title = title;
        this.applicationId = applicationId;
        this.registry = registry;
        surfaceSize = initialSize;
        pendingSize = initialSize;
    }

    private readonly string? applicationId;

    /// <summary>Ввод, пришедший из сессии разработчика.</summary>
    public event Action<WaylandHostInputEvent>? HostInputReceived;

    /// <summary>Решение оболочки хоста об окне.</summary>
    public event Action<WaylandHostWindowEvent>? HostWindowEvent;

    /// <summary>Связь с сессией разработчика цела.</summary>
    public bool IsAlive => !host.IsClosed;

    /// <summary>
    /// Прозрачность четырёх углов сцены.
    /// </summary>
    /// <remarks>
    /// У окна со скруглёнными углами угловая точка обязана быть прозрачной. Значение 255
    /// означает заливку — именно так выглядела дорисовка чёрным.
    /// </remarks>
    public unsafe string DescribeCornerAlpha()
    {
        if (memory == nint.Zero || surfaceSize.Width <= 0 || surfaceSize.Height <= 0)
            return "буфера нет";

        var stride = surfaceSize.Width * BytesPerPixel;
        var pixels = (byte*)DrawMemory;
        var right = (surfaceSize.Width - 1) * BytesPerPixel;
        var bottom = (long)(surfaceSize.Height - 1) * stride;

        return $"левый-верх={pixels[3]} правый-верх={pixels[right + 3]} "
            + $"левый-низ={pixels[bottom + 3]} правый-низ={pixels[bottom + right + 3]} "
            + $"центр={pixels[(bottom / 2) + (right / 2 / BytesPerPixel * BytesPerPixel) + 3]}";
    }

    /// <summary>
    /// Сохраняет показанный кадр в файл PPM.
    /// </summary>
    /// <remarks>Формат выбран за простоту: заголовок текстом и тройки байт, без библиотек.</remarks>
    public unsafe bool TryCaptureFrame(string path)
    {
        CaptureLastFrame();

        if (lastFrame.Length == 0 || lastFrameSize.Width <= 0 || lastFrameSize.Height <= 0)
            return false;

        using var stream = File.Create(path);
        using var writer = new StreamWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        writer.Write(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"P6\n{lastFrameSize.Width} {lastFrameSize.Height}\n255\n"));
        writer.Flush();

        var row = new byte[lastFrameSize.Width * 3];

        for (var y = 0; y < lastFrameSize.Height; ++y)
        {
            for (var x = 0; x < lastFrameSize.Width; ++x)
            {
                var source = (y * lastFrameStride) + (x * BytesPerPixel);

                // На проводе порядок BGRA, в файле — RGB.
                row[(x * 3) + 0] = lastFrame[source + 2];
                row[(x * 3) + 1] = lastFrame[source + 1];
                row[(x * 3) + 2] = lastFrame[source + 0];
            }

            stream.Write(row);
        }

        return true;
    }

    /// <summary>Состояние окна для диагностики.</summary>
    public string Describe()
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"compositor={compositorGlobal} shm={shmGlobal} shell={shellGlobal} surface={surface} "
                + $"shellSurface={shellSurface} buffer={buffer} configured={isConfigured} "
                + $"memory={memory != nint.Zero} size={surfaceSize.Width}x{surfaceSize.Height}"
                + $"{(host.LastProtocolError is { } error ? " | ОШИБКА ХОСТА: " + error : string.Empty)}"
                + $"{(host.IsClosed ? " | СОЕДИНЕНИЕ С ХОСТОМ ЗАКРЫТО" : string.Empty)}");

    /// <summary>
    /// Открывает окно вывода в сессии разработчика.
    /// </summary>
    /// <returns><see langword="null"/>, если сессии нет — отладочный вывод тогда недоступен.</returns>
#pragma warning disable CA2000 // Владение соединением переходит окну, а само окно освобождает композитор.
    public static WaylandPresentationWindow? TryOpen(Size initialSize, string title, string? applicationId = null)
    {
        var connection = WaylandHostConnection.TryConnect();
        if (connection is null)
            return null;

        var registryId = connection.AllocateId("wl_registry");
        var window = new WaylandPresentationWindow(connection, initialSize, title, applicationId, registryId);

        connection.EventReceived += window.HandleHostEvent;

        // Запрашиваем каталог интерфейсов хоста: дальше работа идёт по его ответам.
        connection.Send(WaylandDisplayId, DisplayGetRegistry, writer => writer.WriteUInt(registryId));
        connection.Pump();

        return window;
    }
#pragma warning restore CA2000

    /// <summary>Обрабатывает события хоста и поддерживает окно живым.</summary>
    public void Pump() => host.Pump();

    /// <summary>
    /// Собирает сцену из слоёв и показывает её.
    /// </summary>
    /// <remarks>
    /// ★ Слои идут в порядке появления: окно первым, затем вложенные поверхности и всплывающие
    /// элементы поверх него. Показ одного слоя вместо сборки затирал бы окно — при наведении на
    /// элемент с подсказкой она коммитит свою поверхность, и окно исчезало бы с экрана.
    /// </remarks>
    /// <returns><see langword="false"/>, если окно ещё не готово принимать кадры.</returns>
    public unsafe bool PresentScene(IReadOnlyList<WaylandSceneLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        if (!isConfigured || memory == nint.Zero || surface == 0 || buffer == 0)
            return false;

        // ★ Целевой буфер ещё у хоста — писать в него нельзя, иначе он покажет недорисованный
        // кадр и окно мигнёт. Кадр пропускается: сцена осталась грязной и соберётся на следующем обороте.
        if (isBufferBusy[activeBuffer])
            return false;

        var root = layers.FirstOrDefault(layer => layer.IsRoot);
        if (root.Width <= 0 || root.Height <= 0)
            return false;

        // ★ Поверхность клиента показывается ЦЕЛИКОМ, вместе с полями вокруг окна. В них браузер
        // рисует тень и держит зону изменения размера; обрезка по геометрии отрезала угол для тяги
        // и делала скруглённые углы квадратными.
        if (!TryResizeScene(root))
            return false;

        var targetStride = surfaceSize.Width * BytesPerPixel;
        var isRootFullCover = root.IsOpaque
            && root.Width / Math.Max(1, root.Scale) >= surfaceSize.Width
            && root.Height / Math.Max(1, root.Scale) >= surfaceSize.Height;

        if (!isRootFullCover)
            ClearScene(targetStride);

        foreach (var layer in layers)
            BlitLayer(layer, layer.IsRoot ? 0 : layer.X, layer.IsRoot ? 0 : layer.Y, targetStride);

        isLastFrameStale = true;
        CommitSurface();

        return true;
    }

    private unsafe void ClearScene(int targetStride)
        => new Span<byte>((byte*)DrawMemory, surfaceSize.Height * targetStride).Clear();

    /// <summary>Подгоняет буфер окна под размер поверхности клиента.</summary>
    private bool TryResizeScene(in WaylandSceneLayer root)
    {
        var scale = Math.Max(1, root.Scale);
        var sceneWidth = root.Width / scale;
        var sceneHeight = root.Height / scale;

        if (sceneWidth <= 0 || sceneHeight <= 0)
            return false;

        if (sceneWidth == surfaceSize.Width && sceneHeight == surfaceSize.Height)
            return true;

        surfaceSize = new Size(sceneWidth, sceneHeight);
        RecreateBuffer();

        return memory != nint.Zero && buffer != 0;
    }

    /// <summary>
    /// Переносит один слой в буфер окна.
    /// </summary>
    /// <remarks>
    /// Масштаб буфера учитывается прореживанием: при HiDPI клиент рисует вдвое крупнее, и без
    /// пересчёта была бы видна лишь четверть кадра.
    /// </remarks>
    private unsafe void BlitLayer(in WaylandSceneLayer layer, int originX, int originY, int targetStride)
    {
        var scale = Math.Max(1, layer.Scale);
        var logicalWidth = layer.Width / scale;
        var logicalHeight = layer.Height / scale;
        var source = (byte*)layer.Memory;
        var target = (byte*)DrawMemory;

        for (var row = 0; row < logicalHeight; ++row)
        {
            var targetY = originY + row;
            if (targetY < 0 || targetY >= surfaceSize.Height)
                continue;

            var sourceRow = source + ((long)row * scale * layer.Stride);
            var targetRow = target + ((long)targetY * targetStride);

            if (scale == 1)
                BlitRow(sourceRow, targetRow, originX, logicalWidth, layer.IsOpaque);
            else
                BlitScaledRow(sourceRow, targetRow, originX, logicalWidth, scale, layer.IsOpaque);
        }
    }

    private unsafe void BlitRow(byte* sourceRow, byte* targetRow, int originX, int width, bool isOpaque)
    {
        var startX = Math.Max(0, originX);
        var endX = Math.Min(surfaceSize.Width, originX + width);

        if (endX <= startX)
            return;

        var source = (uint*)(sourceRow + ((startX - originX) * BytesPerPixel));
        var target = (uint*)(targetRow + (startX * BytesPerPixel));
        var count = endX - startX;

        if (isOpaque)
            CopyOpaque(source, target, count);
        else
            BlendRow(source, target, count);
    }

    /// <summary>
    /// Копирует строку, доводя альфу до непрозрачной.
    /// </summary>
    /// <remarks>
    /// ★ У формата без альфы старший байт не определён. Копирование и доводка идут ОДНИМ
    /// проходом: раздельные прогоны гоняли кадр через память дважды.
    /// </remarks>
    private static unsafe void CopyOpaque(uint* source, uint* target, int count)
    {
        var index = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            var mask = Vector256.Create(OpaqueAlpha);

            for (; index <= count - Vector256<uint>.Count; index += Vector256<uint>.Count)
                (Vector256.Load(source + index) | mask).Store(target + index);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            var mask = Vector128.Create(OpaqueAlpha);

            for (; index <= count - Vector128<uint>.Count; index += Vector128<uint>.Count)
                (Vector128.Load(source + index) | mask).Store(target + index);
        }

        for (; index < count; ++index)
            target[index] = source[index] | OpaqueAlpha;
    }

    /// <summary>
    /// Накладывает строку с учётом прозрачности.
    /// </summary>
    /// <remarks>
    /// ★ Тени всплывающих окон и скруглённые углы — частично прозрачные точки. Сплошные участки
    /// полной прозрачности и непрозрачности пропускаются целыми блоками: у тени большая часть строки
    /// — чистые нули, и смешивать их незачем.
    /// </remarks>
    private static unsafe void BlendRow(uint* source, uint* target, int count)
    {
        var index = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            var opaque = Vector256.Create(OpaqueAlpha);
            var zero = Vector256<uint>.Zero;
            var step = Vector256<uint>.Count;

            for (; index <= count - step; index += step)
            {
                var block = Vector256.Load(source + index);
                var alpha = block & opaque;

                // Блок целиком прозрачен — нижний слой остаётся как есть.
                if (alpha == zero)
                    continue;

                // Блок целиком непрозрачен — перекрывает нижний слой без арифметики.
                if (alpha == opaque)
                {
                    block.Store(target + index);
                    continue;
                }

                for (var offset = 0; offset < step; ++offset)
                    BlendPixel(target + index + offset, source[index + offset]);
            }
        }

        for (; index < count; ++index)
            BlendPixel(target + index, source[index]);
    }

    /// <summary>
    /// Накладывает точку на уже лежащую ниже.
    /// </summary>
    /// <remarks>
    /// Формат <c lang="text">ARGB8888</c> в этом протоколе — с предумноженной альфой, поэтому цвет верхней
    /// точки уже умножен на её прозрачность и его надо просто прибавить.
    /// </remarks>
    private static unsafe void BlendPixel(uint* target, uint source)
    {
        var sourceAlpha = source >> 24;

        if (sourceAlpha == 0)
            return;

        if (sourceAlpha == 0xFF)
        {
            *target = source;
            return;
        }

        var inverse = 255 - sourceAlpha;
        var destination = *target;

        // Деление на 255 заменено точным эквивалентом на сдвигах — оно здесь на каждый канал каждой точки.
        var blue = (source & 0xFFu) + Scale(destination & 0xFFu, inverse);
        var green = ((source >> 8) & 0xFFu) + Scale((destination >> 8) & 0xFFu, inverse);
        var red = ((source >> 16) & 0xFFu) + Scale((destination >> 16) & 0xFFu, inverse);
        var alpha = sourceAlpha + Scale(destination >> 24, inverse);

        *target = (Math.Min(alpha, 255u) << 24)
            | (Math.Min(red, 255u) << 16)
            | (Math.Min(green, 255u) << 8)
            | Math.Min(blue, 255u);
    }

    /// <summary>Делит произведение на 255 без деления.</summary>
    private static uint Scale(uint value, uint factor)
    {
        var product = (value * factor) + 128;
        return (product + (product >> 8)) >> 8;
    }

    private const uint OpaqueAlpha = 0xFF000000u;

    private unsafe void BlitScaledRow(byte* sourceRow, byte* targetRow, int originX, int width, int scale, bool isOpaque)
    {
        for (var column = 0; column < width; ++column)
        {
            var targetX = originX + column;
            if (targetX < 0 || targetX >= surfaceSize.Width)
                continue;

            var value = *(uint*)(sourceRow + ((long)column * scale * BytesPerPixel));

            if (isOpaque)
            {
                *(uint*)(targetRow + (targetX * BytesPerPixel)) = value | 0xFF000000u;
                continue;
            }

            BlendPixel((uint*)(targetRow + (targetX * BytesPerPixel)), value);
        }
    }

    /// <summary>
    /// Сохраняет копию показанного кадра.
    /// </summary>
    /// <remarks>
    /// ★ Вызывается ЛЕНИВО — только когда копия действительно понадобилась (смена размера, возврат
    /// из свёрнутого, снимок). Снятие на каждом кадре давало лишние 8 МБ трафика памяти и
    /// вытесняло кэш целиком.
    /// </remarks>
    private unsafe void CaptureLastFrame()
    {
        if (!isLastFrameStale || memory == nint.Zero)
            return;

        isLastFrameStale = false;

        var targetStride = surfaceSize.Width * BytesPerPixel;
        var frameBytes = surfaceSize.Height * targetStride;

        if (lastFrame.Length < frameBytes)
            lastFrame = new byte[frameBytes];

        // Читается ПОКАЗАННЫЙ буфер, а не тот, в который будет идти следующая отрисовка.
        var shown = memory + ((nint)((activeBuffer + BufferCount - 1) % BufferCount) * surfaceBufferBytes);

        new ReadOnlySpan<byte>((byte*)shown, frameBytes).CopyTo(lastFrame);
        lastFrameStride = targetStride;
        lastFrameSize = surfaceSize;
    }

    private bool isLastFrameStale;

    /// <summary>
    /// Повторяет последний кадр.
    /// </summary>
    /// <remarks>
    /// Нужно после разворачивания и смены размера: новый буфер пуст, а браузер на статичной
    /// странице может не прислать ни одного кадра ещё очень долго.
    /// </remarks>
    private unsafe void RepeatLastFrame()
    {
        CaptureLastFrame();

        if (memory == nint.Zero || buffer == 0 || lastFrame.Length == 0)
            return;

        var targetStride = surfaceSize.Width * BytesPerPixel;
        var rows = Math.Min(lastFrameSize.Height, surfaceSize.Height);
        var rowBytes = Math.Min(lastFrameStride, targetStride);
        var target = (byte*)DrawMemory;

        for (var row = 0; row < rows; ++row)
        {
            var sourceOffset = row * lastFrameStride;
            if (sourceOffset + rowBytes > lastFrame.Length)
                break;

            lastFrame.AsSpan(sourceOffset, rowBytes).CopyTo(new Span<byte>(target + (row * targetStride), rowBytes));
        }

        CommitSurface();
    }

    private void CommitSurface()
    {
        var shown = buffers[activeBuffer];
        isBufferBusy[activeBuffer] = true;

        host.Send(surface, SurfaceAttach, writer => writer.WriteUInt(shown).WriteInt(0).WriteInt(0));
        host.Send(surface, SurfaceDamageBuffer, writer => writer
            .WriteInt(0)
            .WriteInt(0)
            .WriteInt(surfaceSize.Width)
            .WriteInt(surfaceSize.Height));
        host.Send(surface, SurfaceCommit);

        // Следующий кадр готовится в соседнем буфере, пока хост показывает этот.
        activeBuffer = (activeBuffer + 1) % BufferCount;
        buffer = buffers[activeBuffer];
    }

    /// <summary>
    /// Показывает курсор браузера в сессии разработчика.
    /// </summary>
    /// <remarks>
    /// ★ Курсор задаётся отдельной поверхностью, привязанной к нашему указателю. Держим её своей:
    /// поверхность браузера принадлежит его соединению, и сослаться на неё в чужой сессии нельзя.
    /// </remarks>
    public unsafe void PresentCursor(in WaylandFrame frame, int hotspotX, int hotspotY)
    {
        if (compositorGlobal == 0 || shmGlobal == 0 || pointer == 0 || lastEnterSerial == 0)
            return;

        if (frame.Width <= 0 || frame.Height <= 0)
            return;

        if (!EnsureCursorBuffer(frame.Width, frame.Height))
            return;

        var targetStride = cursorSize.Width * BytesPerPixel;
        var rowBytes = Math.Min(frame.Width * BytesPerPixel, targetStride);
        var target = (byte*)cursorMemory;

        for (var row = 0; row < frame.Height; ++row)
        {
            var sourceOffset = row * frame.Stride;
            if (sourceOffset + rowBytes > frame.Pixels.Length)
                break;

            frame.Pixels.Slice(sourceOffset, rowBytes).CopyTo(new Span<byte>(target + (row * targetStride), rowBytes));
        }

        host.Send(cursorSurface, SurfaceAttach, writer => writer.WriteUInt(cursorBuffer).WriteInt(0).WriteInt(0));
        host.Send(cursorSurface, SurfaceDamageBuffer, writer => writer
            .WriteInt(0)
            .WriteInt(0)
            .WriteInt(cursorSize.Width)
            .WriteInt(cursorSize.Height));
        host.Send(cursorSurface, SurfaceCommit);

        cursorHotspot = (hotspotX, hotspotY);

        host.Send(pointer, PointerSetCursor, writer => writer
            .WriteUInt(lastEnterSerial)
            .WriteUInt(cursorSurface)
            .WriteInt(hotspotX)
            .WriteInt(hotspotY));
    }

    private (int X, int Y) cursorHotspot;

    /// <summary>Прячет курсор над окном вывода.</summary>
    public void HideCursor()
    {
        if (pointer == 0 || lastEnterSerial == 0)
            return;

        host.Send(pointer, PointerSetCursor, writer => writer
            .WriteUInt(lastEnterSerial)
            .WriteUInt(0)
            .WriteInt(0)
            .WriteInt(0));
    }

    private bool EnsureCursorBuffer(int width, int height)
    {
        if (cursorSurface == 0)
        {
            cursorSurface = host.AllocateId("wl_surface");
            host.Send(compositorGlobal, CompositorCreateSurface, writer => writer.WriteUInt(cursorSurface));
        }

        if (cursorMemory != nint.Zero && cursorSize.Width == width && cursorSize.Height == height)
            return true;

        ReleaseCursorBuffer();

        var stride = width * BytesPerPixel;
        cursorBytes = stride * height;

        cursorDescriptor = MemfdCreate("atom-cursor", MfdCloexec);
        if (cursorDescriptor < 0)
            return false;

        if (Ftruncate(cursorDescriptor, cursorBytes) < 0)
            return false;

        var address = Mmap(nint.Zero, (nuint)cursorBytes, ProtRead | ProtWrite, MapShared, cursorDescriptor, 0);
        if (address == MapFailedAddress)
            return false;

        cursorMemory = address;
        cursorSize = new Size(width, height);

        cursorPool = host.AllocateId("wl_shm_pool");
        host.SendWithDescriptor(shmGlobal, ShmCreatePool, cursorDescriptor, writer => writer
            .WriteUInt(cursorPool)
            .WriteInt(cursorBytes));

        cursorBuffer = host.AllocateId("wl_buffer");
        host.Send(cursorPool, ShmPoolCreateBuffer, writer => writer
            .WriteUInt(cursorBuffer)
            .WriteInt(0)
            .WriteInt(width)
            .WriteInt(height)
            .WriteInt(stride)
            .WriteUInt(FormatArgb8888));

        return true;
    }

    private void ReleaseCursorBuffer()
    {
        if (cursorBuffer != 0)
        {
            host.Send(cursorBuffer, BufferDestroy);
            cursorBuffer = 0;
        }

        if (cursorPool != 0)
        {
            host.Send(cursorPool, ShmPoolDestroy);
            cursorPool = 0;
        }

        if (cursorMemory != nint.Zero)
        {
            _ = Munmap(cursorMemory, (nuint)cursorBytes);
            cursorMemory = nint.Zero;
        }

        if (cursorDescriptor >= 0)
        {
            _ = Close(cursorDescriptor);
            cursorDescriptor = -1;
        }
    }

    private uint cursorSurface;
    private uint cursorPool;
    private uint cursorBuffer;
    private nint cursorMemory;
    private int cursorDescriptor = -1;
    private int cursorBytes;
    private Size cursorSize;

    /// <summary>
    /// Выполняет запрос браузера к оболочке.
    /// </summary>
    /// <remarks>
    /// Перетаскивание и изменение размера требуют порядкового номера НАСТОЯЩЕГО нажатия в сессии
    /// хоста: оболочка проверяет, что жест начат живым вводом, и на выдуманный номер не ответит.
    /// </remarks>
    public void ApplyWindowCommand(WaylandWindowCommand command)
    {
        if (toplevel == 0)
            return;

        switch (command.Kind)
        {
            case WaylandWindowCommandKind.SetTitle when command.Text is { Length: > 0 } windowTitle:
                host.Send(toplevel, XdgToplevelSetTitle, writer => writer.WriteString(windowTitle));
                break;

            case WaylandWindowCommandKind.SetAppId when command.Text is { Length: > 0 } appId:
                host.Send(toplevel, XdgToplevelSetAppId, writer => writer.WriteString(appId));
                break;

            case WaylandWindowCommandKind.Move when lastButtonSerial != 0:
                // Номер подставляется наш: у хоста своя нумерация, и номер браузера ему ничего не говорит.
                host.Send(toplevel, XdgToplevelMove, writer => writer
                    .WriteUInt(seatGlobal)
                    .WriteUInt(lastButtonSerial));
                break;

            case WaylandWindowCommandKind.Resize when lastButtonSerial != 0:
                host.Send(toplevel, XdgToplevelResize, writer => writer
                    .WriteUInt(seatGlobal)
                    .WriteUInt(lastButtonSerial)
                    .WriteUInt(command.Edge));
                break;

            case WaylandWindowCommandKind.Maximize:
                host.Send(toplevel, command.IsEnabled ? XdgToplevelSetMaximized : XdgToplevelUnsetMaximized);
                break;

            case WaylandWindowCommandKind.Fullscreen when command.IsEnabled:
                host.Send(toplevel, XdgToplevelSetFullscreen, writer => writer.WriteUInt(0));
                break;

            case WaylandWindowCommandKind.Fullscreen:
                host.Send(toplevel, XdgToplevelUnsetFullscreen);
                break;

            case WaylandWindowCommandKind.Minimize:
                host.Send(toplevel, XdgToplevelSetMinimized);
                break;

            case WaylandWindowCommandKind.ShowMenu when lastButtonSerial != 0:
                host.Send(toplevel, XdgToplevelShowWindowMenu, writer => writer
                    .WriteUInt(seatGlobal)
                    .WriteUInt(lastButtonSerial)
                    .WriteInt(command.X)
                    .WriteInt(command.Y));
                break;

            default:
                // Остальные сочетания означают жест без живого нажатия — оболочка такой запрос отклонит.
                break;
        }
    }

    private void HandleHostEvent(uint objectId, ushort opcode, ReadOnlyMemory<byte> payload)
    {
        var message = new WaylandMessage { ObjectId = objectId, Opcode = opcode, Payload = payload };

        if (objectId == registry && opcode == RegistryGlobal)
            HandleGlobalAnnounced(message);
        else if (objectId == shellSurface && opcode == XdgSurfaceConfigure)
            HandleSurfaceConfigured(message);
        else if (objectId == toplevel && toplevel != 0)
            HandleToplevelEvent(opcode, message);
        else if (objectId == shellGlobal && opcode == XdgWmBasePing)
            HandlePing(message);
        else if (objectId == seatGlobal && opcode == SeatCapabilities)
            HandleSeatCapabilities(message);
        else if (objectId == pointer && pointer != 0)
            HandlePointerEvent(opcode, message);
        else if (objectId == keyboard && keyboard != 0)
            HandleKeyboardEvent(opcode, message);
        else if (opcode == BufferRelease)
            ReleaseBuffer(objectId);
    }

    /// <summary>
    /// Отмечает буфер свободным.
    /// </summary>
    /// <remarks>
    /// ★ Без этого события очерёдность буферов была слепой: при частых кадрах оба оказывались у хоста,
    /// и отрисовка шла поверх показываемого — окно мигало при движении курсора.
    /// </remarks>
    private void ReleaseBuffer(uint bufferId)
    {
        for (var index = 0; index < BufferCount; ++index)
        {
            if (buffers[index] == bufferId)
                isBufferBusy[index] = false;
        }
    }

    private void HandleGlobalAnnounced(in WaylandMessage message)
    {
        var offset = 0;
        var name = message.ReadUInt(ref offset);
        var interfaceName = message.ReadString(ref offset);
        var version = message.ReadUInt(ref offset);

        switch (interfaceName)
        {
            case "wl_compositor":
                compositorGlobal = Bind(name, interfaceName, Math.Min(version, 4u));
                TryCreateWindow();
                break;

            case "wl_shm":
                shmGlobal = Bind(name, interfaceName, 1);
                break;

            case "xdg_wm_base":
                shellGlobal = Bind(name, interfaceName, Math.Min(version, 2u));
                TryCreateWindow();
                break;

            case "wl_seat":
                seatGlobal = Bind(name, interfaceName, Math.Min(version, 5u));
                break;

            case "zxdg_decoration_manager_v1":
                decorationManager = Bind(name, interfaceName, 1);
                TryRequestClientSideDecoration();
                break;
        }
    }

    private void HandleSurfaceConfigured(in WaylandMessage message)
    {
        var offset = 0;
        var serial = message.ReadUInt(ref offset);

        host.Send(shellSurface, XdgSurfaceAckConfigure, writer => writer.WriteUInt(serial));

        if (pendingSize.Width > 0 && pendingSize.Height > 0 && pendingSize != surfaceSize)
        {
            surfaceSize = pendingSize;
            RecreateBuffer();
            RepeatLastFrame();

            HostWindowEvent?.Invoke(new WaylandHostWindowEvent
            {
                Kind = WaylandHostWindowEventKind.Resized,
                Size = surfaceSize,
            });

            isConfigured = true;
            return;
        }

        if (isConfigured)
        {
            // Окно вернулось из свёрнутого или сменило состояние — картинку надо показать заново.
            RepeatLastFrame();
            SceneRefreshRequested?.Invoke();
            return;
        }

        isConfigured = true;
        RecreateBuffer();
        SceneRefreshRequested?.Invoke();
    }

    /// <summary>Сцену нужно собрать заново из текущих буферов клиента.</summary>
    public event Action? SceneRefreshRequested;

    private void HandleToplevelEvent(ushort opcode, in WaylandMessage message)
    {
        switch (opcode)
        {
            case XdgToplevelConfigure:
                var offset = 0;
                var width = message.ReadInt(ref offset);
                var height = message.ReadInt(ref offset);

                // Нули означают «решай сам» — оставляем прежний размер.
                if (width > 0 && height > 0)
                    pendingSize = new Size(width, height);
                break;

            case XdgToplevelClose:
                HostWindowEvent?.Invoke(new WaylandHostWindowEvent { Kind = WaylandHostWindowEventKind.Close });
                break;
        }
    }

    private void HandlePing(in WaylandMessage message)
    {
        var offset = 0;
        var serial = message.ReadUInt(ref offset);

        // Без ответа оболочка считает окно зависшим и предлагает его закрыть.
        host.Send(shellGlobal, XdgWmBasePong, writer => writer.WriteUInt(serial));
    }

    private void HandleSeatCapabilities(in WaylandMessage message)
    {
        var offset = 0;
        var capabilities = message.ReadUInt(ref offset);

        if ((capabilities & SeatCapabilityPointer) != 0 && pointer == 0)
        {
            pointer = host.AllocateId("wl_pointer");
            host.Send(seatGlobal, SeatGetPointer, writer => writer.WriteUInt(pointer));
        }

        if ((capabilities & SeatCapabilityKeyboard) != 0 && keyboard == 0)
        {
            keyboard = host.AllocateId("wl_keyboard");
            host.Send(seatGlobal, SeatGetKeyboard, writer => writer.WriteUInt(keyboard));
        }
    }

    private void HandleKeyboardEvent(ushort opcode, in WaylandMessage message)
    {
        var offset = 0;

        switch (opcode)
        {
            case KeyboardEnter:
                HostInputReceived?.Invoke(new WaylandHostInputEvent { Kind = WaylandHostInputKind.KeyboardFocus, IsPressed = true });
                break;

            case KeyboardLeave:
                HostInputReceived?.Invoke(new WaylandHostInputEvent { Kind = WaylandHostInputKind.KeyboardFocus, IsPressed = false });
                break;

            case KeyboardKey:
                _ = message.ReadUInt(ref offset);
                _ = message.ReadUInt(ref offset);
                var key = message.ReadUInt(ref offset);
                var state = message.ReadUInt(ref offset);

                HostInputReceived?.Invoke(new WaylandHostInputEvent
                {
                    Kind = WaylandHostInputKind.Key,
                    Code = key,
                    IsPressed = state == KeyboardKeyPressed,
                });
                break;

            case KeyboardModifiers:
                _ = message.ReadUInt(ref offset);
                var depressed = message.ReadUInt(ref offset);
                var latched = message.ReadUInt(ref offset);
                var locked = message.ReadUInt(ref offset);
                var group = message.ReadUInt(ref offset);

                HostInputReceived?.Invoke(new WaylandHostInputEvent
                {
                    Kind = WaylandHostInputKind.Modifiers,
                    Code = depressed,
                    Latched = latched,
                    Locked = locked,
                    Group = group,
                });
                break;
        }
    }

    private uint keyboard;

    private void HandlePointerEvent(ushort opcode, in WaylandMessage message)
    {
        var offset = 0;

        switch (opcode)
        {
            case PointerEnter:
                HandlePointerEntered(ref offset, message);
                break;

            case PointerMotion:
                _ = message.ReadUInt(ref offset);
                pointerX = message.ReadFixed(ref offset);
                pointerY = message.ReadFixed(ref offset);
                RaiseMotion();
                break;

            case PointerLeave:
                HostInputReceived?.Invoke(new WaylandHostInputEvent { Kind = WaylandHostInputKind.PointerLeave });
                break;

            case PointerButton:
                lastButtonSerial = message.ReadUInt(ref offset);
                _ = message.ReadUInt(ref offset);
                var code = message.ReadUInt(ref offset);
                var state = message.ReadUInt(ref offset);

                HostInputReceived?.Invoke(new WaylandHostInputEvent
                {
                    Kind = WaylandHostInputKind.PointerButton,
                    X = pointerX,
                    Y = pointerY,
                    Code = code,
                    IsPressed = state == PointerButtonPressed,
                });
                break;

            case PointerAxis:
                _ = message.ReadUInt(ref offset);
                var axis = message.ReadUInt(ref offset);
                var value = message.ReadFixed(ref offset);

                HostInputReceived?.Invoke(new WaylandHostInputEvent
                {
                    Kind = WaylandHostInputKind.PointerAxis,
                    Code = axis,
                    Value = value,
                });
                break;
        }
    }

    private void HandlePointerEntered(ref int offset, in WaylandMessage message)
    {
        // Номер входа обновляется каждый раз: на set_cursor с устаревшим оболочка не ответит.
        lastEnterSerial = message.ReadUInt(ref offset);
        _ = message.ReadUInt(ref offset);
        pointerX = message.ReadFixed(ref offset);
        pointerY = message.ReadFixed(ref offset);

        // При каждом входе курсор назначается заново — иначе он останется стрелкой оболочки.
        if (cursorSurface != 0 && cursorBuffer != 0)
        {
            host.Send(pointer, PointerSetCursor, writer => writer
                .WriteUInt(lastEnterSerial)
                .WriteUInt(cursorSurface)
                .WriteInt(cursorHotspot.X)
                .WriteInt(cursorHotspot.Y));
        }

        RaiseMotion();
    }

    private void RaiseMotion() => HostInputReceived?.Invoke(new WaylandHostInputEvent
    {
        Kind = WaylandHostInputKind.PointerMotion,
        X = pointerX,
        Y = pointerY,
    });

    private uint Bind(uint name, string interfaceName, uint version)
    {
        var id = host.AllocateId(interfaceName);

        host.Send(registry, RegistryBind, writer => writer
            .WriteUInt(name)
            .WriteString(interfaceName)
            .WriteUInt(version)
            .WriteUInt(id));

        return id;
    }

    private void TryCreateWindow()
    {
        if (compositorGlobal == 0 || shellGlobal == 0 || surface != 0)
            return;

        surface = host.AllocateId("wl_surface");
        host.Send(compositorGlobal, CompositorCreateSurface, writer => writer.WriteUInt(surface));

        shellSurface = host.AllocateId("xdg_surface");
        host.Send(shellGlobal, XdgWmBaseGetXdgSurface, writer => writer
            .WriteUInt(shellSurface)
            .WriteUInt(surface));

        toplevel = host.AllocateId("xdg_toplevel");
        host.Send(shellSurface, XdgSurfaceGetToplevel, writer => writer.WriteUInt(toplevel));
        host.Send(toplevel, XdgToplevelSetTitle, writer => writer.WriteString(title));

        // ★ Идентификатор задаётся СРАЗУ, а не ждёт запроса браузера: окно появляется на панели
        // задач раньше, чем браузер успевает представиться, и без этого первые секунды видна прослойка.
        if (applicationId is { Length: > 0 })
            host.Send(toplevel, XdgToplevelSetAppId, writer => writer.WriteString(applicationId));

        TryRequestClientSideDecoration();

        // Первый коммит идёт без буфера: оболочка отвечает на него размерами окна.
        host.Send(surface, SurfaceCommit);
    }

    /// <summary>
    /// Просит оболочку хоста не рисовать свою рамку.
    /// </summary>
    /// <remarks>
    /// ★ Заголовок и кнопки уже нарисованы браузером внутри кадра. Рамка оболочки поверх него дала
    /// бы два заголовка сразу, поэтому объявляем декорации своими.
    /// </remarks>
    private void TryRequestClientSideDecoration()
    {
        if (decorationManager == 0 || toplevel == 0 || decoration != 0)
            return;

        decoration = host.AllocateId("zxdg_toplevel_decoration_v1");
        host.Send(decorationManager, DecorationManagerGetToplevelDecoration, writer => writer
            .WriteUInt(decoration)
            .WriteUInt(toplevel));
        host.Send(decoration, DecorationSetMode, writer => writer.WriteUInt(DecorationModeClientSide));
    }

    private void RecreateBuffer()
    {
        if (shmGlobal == 0 || surfaceSize.Width <= 0 || surfaceSize.Height <= 0)
            return;

        // ★ Отцеплять буфер коммитом НЕЛЬЗЯ: пустой коммит до следующего configure оболочка
        // считает нарушением («attached a buffer before configure event») и рвёт соединение.
        // Буфер разделяемой памяти уничтожается безопасно и так: оболочка держит свою ссылку.
        ReleaseBufferResources();

        var stride = surfaceSize.Width * BytesPerPixel;
        surfaceBufferBytes = stride * surfaceSize.Height;

        // ★ Буферов ДВА. Рисовать в тот, что сейчас показывает хост, нельзя: он ловит момент
        // между очисткой фона и переносом слоёв, и окно на долю секунды становится прозрачным.
        memoryBytes = surfaceBufferBytes * BufferCount;

        memoryDescriptor = MemfdCreate("atom-presentation", MfdCloexec);
        if (memoryDescriptor < 0)
            return;

        if (Ftruncate(memoryDescriptor, memoryBytes) < 0)
            return;

        var address = Mmap(nint.Zero, (nuint)memoryBytes, ProtRead | ProtWrite, MapShared, memoryDescriptor, 0);
        if (address == MapFailedAddress)
            return;

        memory = address;

        pool = host.AllocateId("wl_shm_pool");
        host.SendWithDescriptor(shmGlobal, ShmCreatePool, memoryDescriptor, writer => writer
            .WriteUInt(pool)
            .WriteInt(memoryBytes));

        for (var index = 0; index < BufferCount; ++index)
        {
            var bufferId = host.AllocateId("wl_buffer");
            var bufferOffset = index * surfaceBufferBytes;
            buffers[index] = bufferId;

            // Формат с альфой обязателен: без него прозрачные углы и тень оказываются чёрными.
            host.Send(pool, ShmPoolCreateBuffer, writer => writer
                .WriteUInt(bufferId)
                .WriteInt(bufferOffset)
                .WriteInt(surfaceSize.Width)
                .WriteInt(surfaceSize.Height)
                .WriteInt(stride)
                .WriteUInt(FormatArgb8888));
        }

        activeBuffer = 0;
        buffer = buffers[0];
        Array.Clear(isBufferBusy);
    }

    /// <summary>Память буфера, в который идёт отрисовка.</summary>
    private nint DrawMemory => memory + ((nint)activeBuffer * surfaceBufferBytes);

    private readonly uint[] buffers = new uint[BufferCount];
    private readonly bool[] isBufferBusy = new bool[BufferCount];
    private int surfaceBufferBytes;
    private int activeBuffer;

    private const int BufferCount = 2;

    private void ReleaseBufferResources()
    {
        for (var index = 0; index < BufferCount; ++index)
        {
            if (buffers[index] == 0)
                continue;

            host.Send(buffers[index], BufferDestroy);
            buffers[index] = 0;
        }

        buffer = 0;

        if (pool != 0)
        {
            host.Send(pool, ShmPoolDestroy);
            pool = 0;
        }

        if (memory != nint.Zero)
        {
            _ = Munmap(memory, (nuint)memoryBytes);
            memory = nint.Zero;
        }

        if (memoryDescriptor >= 0)
        {
            _ = Close(memoryDescriptor);
            memoryDescriptor = -1;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
            return;

        ReleaseBufferResources();
        ReleaseCursorBuffer();
        host.Dispose();
    }

    private const int BytesPerPixel = 4;
    private const uint WaylandDisplayId = 1;

    private const ushort DisplayGetRegistry = 1;

    private const ushort RegistryBind = 0;
    private const ushort RegistryGlobal = 0;

    private const ushort CompositorCreateSurface = 0;

    private const ushort SurfaceAttach = 1;
    private const ushort SurfaceCommit = 6;
    private const ushort SurfaceDamageBuffer = 9;

    private const ushort ShmCreatePool = 0;
    private const ushort ShmPoolCreateBuffer = 0;
    private const ushort ShmPoolDestroy = 1;
    private const ushort BufferDestroy = 0;
    private const ushort BufferRelease = 0;

    private const ushort XdgWmBasePing = 0;
    private const ushort XdgWmBaseGetXdgSurface = 2;
    private const ushort XdgWmBasePong = 3;

    private const ushort XdgSurfaceConfigure = 0;
    private const ushort XdgSurfaceGetToplevel = 1;
    private const ushort XdgSurfaceAckConfigure = 4;

    private const ushort XdgToplevelConfigure = 0;
    private const ushort XdgToplevelClose = 1;
    private const ushort XdgToplevelSetTitle = 2;
    private const ushort XdgToplevelSetAppId = 3;
    private const ushort XdgToplevelShowWindowMenu = 4;
    private const ushort XdgToplevelMove = 5;
    private const ushort XdgToplevelResize = 6;
    private const ushort XdgToplevelSetMaximized = 9;
    private const ushort XdgToplevelUnsetMaximized = 10;
    private const ushort XdgToplevelSetFullscreen = 11;
    private const ushort XdgToplevelUnsetFullscreen = 12;
    private const ushort XdgToplevelSetMinimized = 13;

    private const ushort DecorationManagerGetToplevelDecoration = 1;
    private const ushort DecorationSetMode = 1;
    private const uint DecorationModeClientSide = 1;

    private const ushort SeatGetPointer = 0;
    private const ushort SeatGetKeyboard = 1;
    private const ushort SeatCapabilities = 0;
    private const uint SeatCapabilityPointer = 1;
    private const uint SeatCapabilityKeyboard = 2;

    private const ushort KeyboardEnter = 1;
    private const ushort KeyboardLeave = 2;
    private const ushort KeyboardKey = 3;
    private const ushort KeyboardModifiers = 4;
    private const uint KeyboardKeyPressed = 1;

    private const ushort PointerEnter = 0;
    private const ushort PointerLeave = 1;
    private const ushort PointerMotion = 2;
    private const ushort PointerButton = 3;
    private const ushort PointerAxis = 4;
    private const uint PointerButtonPressed = 1;

    private const ushort PointerSetCursor = 0;

    private const uint FormatArgb8888 = 0;

    private const uint MfdCloexec = 0x0001;
    private const int ProtRead = 0x1;
    private const int ProtWrite = 0x2;
    private const int MapShared = 0x01;
    private static readonly nint MapFailedAddress = -1;

    [LibraryImport("libc", EntryPoint = "memfd_create", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int MemfdCreate(string name, uint flags);

    [LibraryImport("libc", EntryPoint = "ftruncate", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Ftruncate(int descriptor, long length);

    [LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint Mmap(nint address, nuint length, int protection, int flags, int descriptor, long offset);

    [LibraryImport("libc", EntryPoint = "munmap", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Munmap(nint address, nuint length);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Close(int descriptor);
}

/// <summary>
/// Слой сцены: одна поверхность со своим положением.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WaylandSceneLayer
{
    /// <summary>Адрес пикселей в памяти клиента.</summary>
    public required nint Memory { get; init; }

    /// <summary>Длина строки в байтах.</summary>
    public required int Stride { get; init; }

    /// <summary>Ширина буфера.</summary>
    public required int Width { get; init; }

    /// <summary>Высота буфера.</summary>
    public required int Height { get; init; }

    /// <summary>Масштаб буфера.</summary>
    public int Scale { get; init; }

    /// <summary>Положение в сцене по горизонтали.</summary>
    public int X { get; init; }

    /// <summary>Положение в сцене по вертикали.</summary>
    public int Y { get; init; }

    /// <summary>Это само окно, задающее размер сцены.</summary>
    public bool IsRoot { get; init; }

    /// <summary>Буфер без альфа-канала.</summary>
    public bool IsOpaque { get; init; }

    /// <summary>Порядок наложения: меньше — ниже.</summary>
    public int Depth { get; init; }

    /// <summary>Границы окна внутри кадра.</summary>
    public WaylandWindowGeometry? Geometry { get; init; }
}

/// <summary>
/// Кадр браузера для показа.
/// </summary>
internal readonly ref struct WaylandFrame
{
    /// <summary>Пиксели кадра.</summary>
    public required ReadOnlySpan<byte> Pixels { get; init; }

    /// <summary>Длина строки в байтах.</summary>
    public required int Stride { get; init; }

    /// <summary>Ширина кадра.</summary>
    public required int Width { get; init; }

    /// <summary>Высота кадра.</summary>
    public required int Height { get; init; }

    /// <summary>Границы окна внутри кадра, если клиент их объявил.</summary>
    public WaylandWindowGeometry? Geometry { get; init; }
}

/// <summary>
/// Событие ввода из сессии разработчика.
/// </summary>
internal readonly record struct WaylandHostInputEvent
{
    /// <summary>Вид события.</summary>
    public required WaylandHostInputKind Kind { get; init; }

    /// <summary>Координата по горизонтали.</summary>
    public double X { get; init; }

    /// <summary>Координата по вертикали.</summary>
    public double Y { get; init; }

    /// <summary>Код кнопки или номер оси.</summary>
    public uint Code { get; init; }

    /// <summary>Величина прокрутки.</summary>
    public double Value { get; init; }

    /// <summary>Защёлкнутые модификаторы.</summary>
    public uint Latched { get; init; }

    /// <summary>Зафиксированные модификаторы.</summary>
    public uint Locked { get; init; }

    /// <summary>Группа раскладки.</summary>
    public uint Group { get; init; }

    /// <summary>Нажатие, а не отпускание.</summary>
    public bool IsPressed { get; init; }
}

/// <summary>Вид события ввода из сессии разработчика.</summary>
internal enum WaylandHostInputKind
{
    /// <summary>Перемещение указателя.</summary>
    PointerMotion,

    /// <summary>Кнопка указателя.</summary>
    PointerButton,

    /// <summary>Прокрутка колесом.</summary>
    PointerAxis,

    /// <summary>Указатель покинул окно.</summary>
    PointerLeave,

    /// <summary>Клавиша.</summary>
    Key,

    /// <summary>Состояние модификаторов.</summary>
    Modifiers,

    /// <summary>Клавиатурный фокус окна.</summary>
    KeyboardFocus,
}

/// <summary>
/// Решение оболочки хоста об окне вывода.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WaylandHostWindowEvent
{
    /// <summary>Что произошло с окном.</summary>
    public required WaylandHostWindowEventKind Kind { get; init; }

    /// <summary>Новый размер окна.</summary>
    public Size Size { get; init; }
}

/// <summary>Вид решения оболочки хоста.</summary>
internal enum WaylandHostWindowEventKind
{
    /// <summary>Окно сменило размер.</summary>
    Resized,

    /// <summary>Окно просят закрыть.</summary>
    Close,
}
