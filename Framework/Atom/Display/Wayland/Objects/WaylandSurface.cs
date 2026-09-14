using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">wl_compositor</c> — фабрика поверхностей.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WaylandCompositorGlobal : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_compositor";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestCreateSurface:
                Client.Register(new WaylandSurface { Id = message.ReadUInt(ref offset), Version = Version, Client = Client });
                break;

            case RequestCreateRegion:
                Client.Register(new WaylandRegion { Id = message.ReadUInt(ref offset), Version = Version, Client = Client });
                break;
        }
    }

    private const ushort RequestCreateSurface = 0;
    private const ushort RequestCreateRegion = 1;
}

/// <summary>
/// Объект <c lang="text">wl_surface</c> — прямоугольник, который рисует клиент.
/// </summary>
/// <remarks>
/// ★ Кадры отпускаются событием <c lang="text">frame</c>. Без него браузер останавливает отрисовку после
/// первого кадра: он ждёт разрешения на следующий. Именно этим композитор задаёт частоту, и
/// именно поэтому страница в фоне «замирает» — там разрешение приходит редко.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandSurface : WaylandObject
{
    private readonly List<uint> pendingFrameCallbacks = [];
    private uint attachedBufferId;

    /// <inheritdoc/>
    public override string InterfaceName => "wl_surface";

    /// <summary>
    /// Границы самого окна внутри поверхности.
    /// </summary>
    /// <remarks>За ними клиент рисует тень своих декораций — её показывать не надо.</remarks>
    public Presentation.WaylandWindowGeometry? WindowGeometry { get; set; }

    /// <summary>
    /// Роль поверхности.
    /// </summary>
    /// <remarks>
    /// ★ Клиент коммитит не только окно: так же приходят курсор, всплывающие меню и
    /// подповерхности. Кадр надо разводить по роли, иначе курсор подменит собой окно.
    /// </remarks>
    public WaylandSurfaceRole Role { get; set; }

    /// <summary>Горячая точка курсора относительно его картинки.</summary>
    public (int X, int Y) CursorHotspot { get; set; }

    /// <summary>Поверхность хотя бы раз прислала кадр.</summary>
    public bool HasPresentedFrame { get; private set; }

    /// <summary>Смещение относительно родительской поверхности.</summary>
    public (int X, int Y) Position { get; set; }

    /// <summary>Родительская поверхность, если эта вложена.</summary>
    public uint ParentSurfaceId { get; set; }

    /// <summary>Масштаб буфера клиента.</summary>
    public int BufferScale { get; private set; } = 1;

    /// <summary>Последний приложенный кадр.</summary>
    public WaylandBuffer? CurrentBuffer { get; private set; }

    /// <summary>Поверхность показывается на экране.</summary>
    public bool IsMapped { get; private set; }

    /// <summary>Область, принимающая ввод.</summary>
    public uint InputRegionId { get; private set; }

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestDestroy:
                Client.Unregister(Id);
                break;

            case RequestAttach:
                attachedBufferId = message.ReadUInt(ref offset);
                hasPendingAttach = true;
                break;

            case RequestFrame:
                pendingFrameCallbacks.Add(message.ReadUInt(ref offset));
                break;

            case RequestSetInputRegion:
                // Зона изменения размера лежит ВНЕ окна, в полях тени — именно её клиент тут объявляет.
                InputRegionId = message.ReadUInt(ref offset);
                break;

            case RequestSetBufferScale:
                // При HiDPI буфер приходит вдвое больше окна; без учёта видна только четверть кадра.
                BufferScale = Math.Max(1, message.ReadInt(ref offset));
                break;

            case RequestCommit:
                Commit();
                break;
        }
    }

    private bool hasPendingAttach;

    /// <summary>
    /// Применяет накопленное состояние поверхности.
    /// </summary>
    /// <remarks>
    /// Буфер сразу возвращается клиенту: выводить изображение некуда, а удержание буфера
    /// остановило бы отрисовку — клиент ждёт его освобождения, чтобы нарисовать следующий кадр.
    /// </remarks>
    private void Commit()
    {
        // ★ Отметку ставим только когда роль УЖЕ назначена. Клиент вправе коммитить поверхность
        // до get_xdg_surface (Chrome так и делает перед открытием меню), и взвод отметки на том
        // пустом коммите съедал бы настоящий запрос конфигурации — всплывающее окно не появлялось.
        if (!hasConfigured)
        {
            foreach (var xdgSurface in Client.OfType<XdgSurface>())
            {
                if (xdgSurface.SurfaceId != Id)
                    continue;

                hasConfigured = true;
                xdgSurface.SendInitialConfigure();
                break;
            }
        }

        if (hasPendingAttach)
            ApplyAttachedBuffer();

        // ★ Сцена собирается ЦЕЛИКОМ и НЕ здесь. Один логический кадр браузера — это 3-10 коммитов
        // (окно плюс подповерхности), и сборка на каждый из них умножала работу впустую. Помечаем
        // сцену грязной, а собирает её цикл событий один раз за оборот.
        if (Role != WaylandSurfaceRole.Cursor)
            Client.Compositor.InvalidateScene();

        // Буфер возвращается ПОСЛЕ сборки сцены: до этого клиент не вправе в него писать.
        if (CurrentBuffer is { } presented)
            presented.SendRelease();

        ReleaseFrameCallbacks();
    }

    private void ApplyAttachedBuffer()
    {
        hasPendingAttach = false;

        // Пустой буфер по протоколу означает снятие поверхности с экрана.
        if (attachedBufferId == 0)
        {
            IsMapped = false;
            CurrentBuffer = null;
            return;
        }

        if (Client.Find<WaylandBuffer>(attachedBufferId) is not { } buffer)
        {
            attachedBufferId = 0;
            return;
        }

        IsMapped = true;
        CurrentBuffer = buffer;
        attachedBufferId = 0;

        if (Role == WaylandSurfaceRole.Cursor)
        {
            Client.Compositor.PresentCursor(buffer, CursorHotspot.X, CursorHotspot.Y);
            return;
        }

        HasPresentedFrame = true;
        Client.Compositor.NoteCommit();
    }

    private bool hasConfigured;

    /// <summary>
    /// Сообщает, что поверхность показана на выходе.
    /// </summary>
    /// <remarks>
    /// ★ Клиент считает окно невидимым, пока не узнал, на каком выходе оно оказалось: по этому
    /// событию он выбирает масштаб и частоту кадров. Firefox без него рисует один кадр и встаёт.
    /// </remarks>
    public void SendEnter(uint outputId) => Emit(EventEnter, writer => writer.WriteUInt(outputId));

    /// <summary>
    /// Разрешает клиенту рисовать следующий кадр.
    /// </summary>
    /// <remarks>
    /// Время передаётся в миллисекундах монотонных часов: браузер по нему считает интервалы между
    /// кадрами, и скачки назад сбивают его планировщик анимации.
    /// </remarks>
    public void ReleaseFrameCallbacks()
    {
        if (pendingFrameCallbacks.Count == 0)
            return;

        var timestamp = (uint)(Environment.TickCount64 & 0xFFFFFFFF);

        foreach (var callbackId in pendingFrameCallbacks)
        {
            // Объект одноразовый: регистрация с удалением дала бы лишний delete_id.
            var callback = new WaylandCallback { Id = callbackId, Version = 1, Client = Client };
            callback.SendDone(timestamp);
            Client.SendDeleteId(callbackId);
        }

        pendingFrameCallbacks.Clear();
    }

    private const ushort RequestDestroy = 0;
    private const ushort RequestAttach = 1;
    private const ushort RequestFrame = 3;
    private const ushort RequestCommit = 6;
    private const ushort RequestSetInputRegion = 5;
    private const ushort RequestSetBufferScale = 8;

    private const ushort EventEnter = 0;
}

/// <summary>
/// Назначение поверхности.
/// </summary>
internal enum WaylandSurfaceRole
{
    /// <summary>Роль ещё не назначена.</summary>
    None,

    /// <summary>Окно приложения.</summary>
    Window,

    /// <summary>Вложенная поверхность окна.</summary>
    Subsurface,

    /// <summary>Всплывающее окно: меню, подсказка, выпадающий список.</summary>
    Popup,

    /// <summary>Картинка курсора.</summary>
    Cursor,
}

/// <summary>
/// Объект <c lang="text">wl_region</c> — область поверхности.
/// </summary>
/// <remarks>
/// Клиент задаёт ими непрозрачную область и зону ввода. Без вывода изображения обе роли не влияют
/// ни на что, но интерфейс обязан существовать: браузер создаёт области при каждом кадре.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandRegion : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_region";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        if (message.Opcode == RequestDestroy)
            Client.Unregister(Id);
    }

    private const ushort RequestDestroy = 0;
}

/// <summary>
/// Объект <c lang="text">wl_subcompositor</c> — вложенные поверхности.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WaylandSubcompositor : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_subcompositor";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestDestroy:
                Client.Unregister(Id);
                break;

            case RequestGetSubsurface:
                HandleGetSubsurface(ref offset, message);
                break;
        }
    }

    /// <summary>
    /// Создаёт вложенную поверхность.
    /// </summary>
    /// <remarks>
    /// ★ Содержимое окна часто рисуется ИМЕННО в подповерхность, а само окно остаётся почти
    /// пустым: у Firefox на неё приходится 1417 коммитов против двух у окна. Без наследования
    /// роли вывод показывал бы один-единственный кадр.
    /// </remarks>
    private void HandleGetSubsurface(ref int offset, in WaylandMessage message)
    {
        var subsurfaceId = message.ReadUInt(ref offset);
        var surfaceId = message.ReadUInt(ref offset);
        var parentId = message.ReadUInt(ref offset);

        Client.Register(new WaylandSubsurface
        {
            Id = subsurfaceId,
            Version = Version,
            Client = Client,
            SurfaceId = surfaceId,
            ParentId = parentId,
        });

        if (Client.Find<WaylandSurface>(surfaceId) is { } surface)
        {
            surface.ParentSurfaceId = parentId;
            surface.Role = WaylandSurfaceRole.Subsurface;
        }
    }

    private const ushort RequestDestroy = 0;
    private const ushort RequestGetSubsurface = 1;
}

/// <summary>
/// Объект <c lang="text">wl_subsurface</c>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WaylandSubsurface : WaylandObject
{
    /// <summary>Поверхность, ставшая вложенной.</summary>
    public required uint SurfaceId { get; init; }

    /// <summary>Родительская поверхность.</summary>
    public required uint ParentId { get; init; }

    /// <inheritdoc/>
    public override string InterfaceName => "wl_subsurface";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestDestroy:
                DetachFromParent();
                Client.Unregister(Id);
                break;

            case RequestSetPosition:
                HandleSetPosition(ref offset, message);
                break;
        }
    }

    /// <summary>
    /// Смещает вложенную поверхность относительно родителя.
    /// </summary>
    /// <remarks>
    /// Без этого содержимое легло бы в левый верхний угол поверх декораций окна.
    /// </remarks>
    private void HandleSetPosition(ref int offset, in WaylandMessage message)
    {
        var x = message.ReadInt(ref offset);
        var y = message.ReadInt(ref offset);

        if (Client.Find<WaylandSurface>(SurfaceId) is { } surface)
            surface.Position = (x, y);
    }

    /// <summary>Снимает с поверхности роль вложенной.</summary>
    private void DetachFromParent()
    {
        if (Client.Find<WaylandSurface>(SurfaceId) is not { } surface)
            return;

        surface.ParentSurfaceId = 0;
        surface.Position = (0, 0);
        surface.Role = WaylandSurfaceRole.None;
    }

    private const ushort RequestDestroy = 0;
    private const ushort RequestSetPosition = 1;
}
