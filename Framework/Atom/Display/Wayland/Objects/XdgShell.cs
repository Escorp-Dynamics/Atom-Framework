using System.Runtime.Versioning;

using Atom.Display.Wayland.Presentation;
using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">xdg_wm_base</c> — оболочка окон.
/// </summary>
/// <remarks>
/// Без неё браузер не может создать окно вовсе: <c lang="text">wl_surface</c> сам по себе не имеет роли,
/// и клиент обязан назначить её через оболочку.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class XdgWmBase : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "xdg_wm_base";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestDestroy:
                Client.Unregister(Id);
                break;

            case RequestCreatePositioner:
                Client.Register(new XdgPositioner { Id = message.ReadUInt(ref offset), Version = Version, Client = Client });
                break;

            case RequestGetXdgSurface:
                HandleGetXdgSurface(ref offset, message);
                break;

            case RequestPong:
                // Клиент подтвердил, что жив: проверку отзывчивости мы не ведём.
                break;
        }
    }

    private void HandleGetXdgSurface(ref int offset, in WaylandMessage message)
    {
        var surfaceObjectId = message.ReadUInt(ref offset);
        var wlSurfaceId = message.ReadUInt(ref offset);

        Client.Register(new XdgSurface
        {
            Id = surfaceObjectId,
            Version = Version,
            Client = Client,
            SurfaceId = wlSurfaceId,
        });
    }

    private const ushort RequestDestroy = 0;
    private const ushort RequestCreatePositioner = 1;
    private const ushort RequestGetXdgSurface = 2;
    private const ushort RequestPong = 3;
}

/// <summary>
/// Объект <c lang="text">xdg_surface</c> — поверхность с ролью окна.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class XdgSurface : WaylandObject
{
    /// <summary>Поверхность, которой назначается роль.</summary>
    public required uint SurfaceId { get; init; }

    /// <inheritdoc/>
    public override string InterfaceName => "xdg_surface";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestDestroy:
                Client.Unregister(Id);
                break;

            case RequestGetToplevel:
                HandleGetToplevel(message.ReadUInt(ref offset));
                break;

            case RequestGetPopup:
                HandleGetPopup(ref offset, message);
                break;

            case RequestSetWindowGeometry:
                HandleSetWindowGeometry(ref offset, message);
                break;

            case RequestAckConfigure:
                // Клиент принял размер окна; подтверждать нечего.
                break;
        }
    }

    /// <summary>
    /// Запоминает границы самого окна внутри поверхности.
    /// </summary>
    /// <remarks>
    /// ★ При собственных декорациях клиент рисует поверхность БОЛЬШЕ окна — по краям идёт тень.
    /// Без этой области вывод показывал бы окно с полями, а координаты ввода расходились бы с картинкой.
    /// </remarks>
    private void HandleSetWindowGeometry(ref int offset, in WaylandMessage message)
    {
        var x = message.ReadInt(ref offset);
        var y = message.ReadInt(ref offset);
        var width = message.ReadInt(ref offset);
        var height = message.ReadInt(ref offset);

        if (width <= 0 || height <= 0)
            return;

        if (Client.Find<WaylandSurface>(SurfaceId) is { } surface)
            surface.WindowGeometry = new WaylandWindowGeometry(x, y, width, height);
    }

    /// <summary>
    /// Создаёт всплывающее окно.
    /// </summary>
    /// <remarks>
    /// ★ Клиент не имеет права приложить буфер, пока не получит <c lang="text">popup.configure</c>.
    /// Без него мёртв весь всплывающий интерфейс: выпадающие списки, контекстные меню,
    /// подсказки, автозаполнение адресной строки.
    /// </remarks>
    private void HandleGetPopup(ref int offset, in WaylandMessage message)
    {
        var popupId = message.ReadUInt(ref offset);
        var parentId = message.ReadUInt(ref offset);
        var positionerId = message.ReadUInt(ref offset);

        var positioner = Client.Find<XdgPositioner>(positionerId);

        var popup = new XdgPopup
        {
            Id = popupId,
            Version = Version,
            Client = Client,
            OwnerSurfaceId = SurfaceId,
            ParentXdgSurfaceId = parentId,
            Bounds = positioner?.ResolveBounds(Client.Compositor.Settings.Resolution) ?? default,
        };

        Client.Register(popup);
        Popup = popup;

        if (Client.Find<WaylandSurface>(SurfaceId) is { } surface)
        {
            surface.Role = WaylandSurfaceRole.Popup;

            // Всплывающее окно позиционируется относительно родительского окна.
            if (Client.Find<XdgSurface>(parentId) is { } parentSurface)
                surface.ParentSurfaceId = parentSurface.SurfaceId;

            surface.Position = (popup.Bounds.X, popup.Bounds.Y);
        }
    }

    /// <summary>Всплывающее окно, если роль назначена так.</summary>
    public XdgPopup? Popup { get; private set; }

    private void HandleGetToplevel(uint toplevelId)
    {
        var toplevel = new XdgToplevel
        {
            Id = toplevelId,
            Version = Version,
            Client = Client,
            OwnerSurfaceId = SurfaceId,
        };

        Client.Register(toplevel);
        Toplevel = toplevel;

        // Роль назначена именно здесь: до этого момента поверхность ещё не окно.
        if (Client.Find<WaylandSurface>(SurfaceId) is { } surface)
        {
            surface.Role = WaylandSurfaceRole.Window;

            foreach (var output in Client.OfType<WaylandOutput>())
                surface.SendEnter(output.Id);
        }

        // ★ Конфигурация шлётся НЕ здесь, а после первого commit поверхности: по правилам
        // оболочки клиент сначала заявляет роль и пустой commit, и только потом ждёт размер. Преждевременный
        // configure клиент подтверждает, но буфер не прикладывает — окно остаётся неотрисованным.
    }

    /// <summary>Роль окна, если она уже назначена.</summary>
    public XdgToplevel? Toplevel { get; private set; }

    /// <summary>
    /// Отвечает на первый commit поверхности размером окна.
    /// </summary>
    /// <remarks>
    /// ★ Размер берётся из геометрии, объявленной самим клиентом: браузер запускается с размером
    /// окна под заявленный профиль, и навязывание разрешения экрана растягивало мобильное окно
    /// на весь экран — геометрия страницы расходилась с личностью.
    /// </remarks>
    public void SendInitialConfigure()
    {
        // У всплывающего окна размер задан позиционером; разрешение экрана к нему неприменимо.
        if (Popup is not null)
        {
            SendCurrentConfigure();
            return;
        }

        // ★ Ноль означает «решай сам»: клиент возьмёт размер, запрошенный при запуске. Навязывание
        // разрешения экрана раздувало окно до полной высоты дисплея — заявленные профилем 1040
        // превращались в 1080 и стирали признак панели задач.
        RequestedSize = System.Drawing.Size.Empty;
        SendCurrentConfigure();
    }

    /// <summary>Желаемый размер окна.</summary>
    public System.Drawing.Size RequestedSize { get; set; }

    /// <summary>Повторяет configure с текущими размером и состоянием.</summary>
    public void SendCurrentConfigure()
    {
        // У всплывающего окна своя конфигурация — положение и размер относительно родителя.
        if (Popup is { } popup)
        {
            popup.SendConfigure();
            SendConfigure(Client.Compositor.NextSerial());
            return;
        }

        if (Toplevel is not { } toplevel)
            return;

        // Развёрнутое окно занимает экран целиком; иначе размер выбирает сам клиент.
        if (RequestedSize.IsEmpty && toplevel.IsMaximized)
            RequestedSize = Client.Compositor.Settings.Resolution;

        toplevel.SendConfigure(RequestedSize.Width, RequestedSize.Height);
        SendConfigure(Client.Compositor.NextSerial());
    }

    /// <summary>
    /// Требует от клиента принять текущее состояние окна.
    /// </summary>
    /// <remarks>
    /// До этого события клиент не имеет права показывать содержимое — таково правило оболочки,
    /// и без него окно не появляется вовсе.
    /// </remarks>
    public void SendConfigure(uint serial) => Emit(EventConfigure, writer => writer.WriteUInt(serial));

    private const ushort RequestDestroy = 0;
    private const ushort RequestGetToplevel = 1;
    private const ushort RequestGetPopup = 2;
    private const ushort RequestSetWindowGeometry = 3;
    private const ushort RequestAckConfigure = 4;

    private const ushort EventConfigure = 0;
}

/// <summary>
/// Объект <c lang="text">xdg_toplevel</c> — обычное окно приложения.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class XdgToplevel : WaylandObject
{
    /// <summary>Поверхность окна.</summary>
    public required uint OwnerSurfaceId { get; init; }

    /// <inheritdoc/>
    public override string InterfaceName => "xdg_toplevel";

    /// <summary>Окно развёрнуто на весь экран.</summary>
    public bool IsMaximized { get; private set; }

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestDestroy:
                Client.Unregister(Id);
                break;

            case RequestSetTitle:
                Client.Compositor.ForwardWindowCommand(WaylandWindowCommand.SetTitle(message.ReadString(ref offset)));
                break;

            case RequestSetAppId:
                // ★ По этому идентификатору панель задач находит .desktop и берёт иконку приложения.
                Client.Compositor.ForwardWindowCommand(WaylandWindowCommand.SetAppId(message.ReadString(ref offset)));
                break;

            case RequestShowWindowMenu:
                HandleShowWindowMenu(ref offset, message);
                break;

            case RequestMove:
                HandleMove(ref offset, message);
                break;

            case RequestResize:
                HandleResize(ref offset, message);
                break;

            case RequestSetMaximized:
                ApplyMaximized(isMaximized: true);
                break;

            case RequestUnsetMaximized:
                ApplyMaximized(isMaximized: false);
                break;

            case RequestSetFullscreen:
                ApplyFullscreen(isFullscreen: true);
                break;

            case RequestUnsetFullscreen:
                ApplyFullscreen(isFullscreen: false);
                break;

            case RequestSetMinimized:
                Client.Compositor.ForwardWindowCommand(WaylandWindowCommand.Minimize());
                break;
        }
    }

    private void ApplyFullscreen(bool isFullscreen)
    {
        IsFullscreen = isFullscreen;
        Client.Compositor.ForwardWindowCommand(WaylandWindowCommand.Fullscreen(isFullscreen));
        NotifyStateChanged();
    }

    /// <summary>Окно развёрнуто во весь экран.</summary>
    public bool IsFullscreen { get; private set; }

    private void NotifyStateChanged()
    {
        // Клиент ждёт ответный configure — иначе кнопка нажата, а вид окна не меняется.
        foreach (var surface in Client.OfType<XdgSurface>())
        {
            if (surface.Toplevel == this)
                surface.SendCurrentConfigure();
        }
    }

    private void HandleMove(ref int offset, in WaylandMessage message)
    {
        _ = message.ReadUInt(ref offset);
        var serial = message.ReadUInt(ref offset);

        Client.Compositor.ForwardWindowCommand(WaylandWindowCommand.Move(serial));
    }

    private void HandleShowWindowMenu(ref int offset, in WaylandMessage message)
    {
        _ = message.ReadUInt(ref offset);
        var serial = message.ReadUInt(ref offset);
        var x = message.ReadInt(ref offset);
        var y = message.ReadInt(ref offset);

        Client.Compositor.ForwardWindowCommand(WaylandWindowCommand.ShowMenu(serial, x, y));
    }

    private void HandleResize(ref int offset, in WaylandMessage message)
    {
        _ = message.ReadUInt(ref offset);
        var serial = message.ReadUInt(ref offset);
        var edge = message.ReadUInt(ref offset);

        Client.Compositor.ForwardWindowCommand(WaylandWindowCommand.Resize(serial, edge));
    }

    private void ApplyMaximized(bool isMaximized)
    {
        IsMaximized = isMaximized;
        Client.Compositor.ForwardWindowCommand(WaylandWindowCommand.Maximize(isMaximized));
        NotifyStateChanged();
    }

    /// <summary>
    /// Сообщает окну его размер и состояние.
    /// </summary>
    /// <remarks>
    /// Состояние «активно» обязательно: без него браузер считает окно фоновым и режет частоту
    /// кадров — ровно та проблема, из-за которой в прежней схеме приходилось бороться за
    /// передний план между вкладками.
    ///
    /// Состояния «во весь экран» здесь НЕТ намеренно: с ним браузер прячет заголовок, вкладки и
    /// кнопки окна целиком.
    /// </remarks>
    public void SendConfigure(int width, int height)
        => Emit(EventConfigure, writer =>
        {
            writer.WriteInt(width);
            writer.WriteInt(height);

            Span<byte> states = stackalloc byte[12];
            var length = 0;

            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(states, StateActivated);
            length += 4;

            if (IsMaximized)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(states[length..], StateMaximized);
                length += 4;
            }

            if (IsFullscreen)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(states[length..], StateFullscreen);
                length += 4;
            }

            writer.WriteArray(states[..length]);
        });

    /// <summary>
    /// Просит клиента закрыть окно.
    /// </summary>
    /// <remarks>Полноценный выход из браузера: он сохраняет сеанс, а не падает.</remarks>
    public void SendClose() => Emit(EventClose);

    private const ushort RequestDestroy = 0;
    private const ushort RequestSetTitle = 2;
    private const ushort RequestSetAppId = 3;
    private const ushort RequestShowWindowMenu = 4;
    private const ushort RequestMove = 5;
    private const ushort RequestResize = 6;
    private const ushort RequestSetMaximized = 9;
    private const ushort RequestUnsetMaximized = 10;
    private const ushort RequestSetFullscreen = 11;
    private const ushort RequestUnsetFullscreen = 12;
    private const ushort RequestSetMinimized = 13;

    private const ushort EventConfigure = 0;
    private const ushort EventClose = 1;

    private const uint StateMaximized = 1;
    private const uint StateFullscreen = 2;
    private const uint StateActivated = 4;
}

/// <summary>
/// Объект <c lang="text">xdg_positioner</c>.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class XdgPositioner : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "xdg_positioner";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestDestroy:
                Client.Unregister(Id);
                break;

            case RequestSetSize:
                width = message.ReadInt(ref offset);
                height = message.ReadInt(ref offset);
                break;

            case RequestSetAnchorRect:
                anchorX = message.ReadInt(ref offset);
                anchorY = message.ReadInt(ref offset);
                anchorWidth = message.ReadInt(ref offset);
                anchorHeight = message.ReadInt(ref offset);
                break;

            case RequestSetAnchor:
                anchor = message.ReadUInt(ref offset);
                break;

            case RequestSetGravity:
                gravity = message.ReadUInt(ref offset);
                break;

            case RequestSetOffset:
                offsetX = message.ReadInt(ref offset);
                offsetY = message.ReadInt(ref offset);
                break;
        }
    }

    /// <summary>
    /// Считает положение и размер всплывающего окна.
    /// </summary>
    /// <remarks>
    /// ★ Якорь задаёт точку на области привязки, гравитация — в какую сторону от неё растёт окно.
    /// Без их учёта меню Chrome (якорь в нижнем-правом углу кнопки, рост влево-вниз) уезжало за
    /// край экрана, и от него оставалась видна узкая полоса.
    /// </remarks>
    public WaylandWindowGeometry ResolveBounds(System.Drawing.Size screen)
    {
        var x = anchorX + AnchorOffset(anchor, anchorWidth, isHorizontal: true) + offsetX;
        var y = anchorY + AnchorOffset(anchor, anchorHeight, isHorizontal: false) + offsetY;

        x += GravityOffset(gravity, width, isHorizontal: true);
        y += GravityOffset(gravity, height, isHorizontal: false);

        // Окно не должно выходить за экран: клиент рассчитывает, что оно видно целиком.
        if (screen.Width > 0)
            x = Math.Max(0, Math.Min(x, screen.Width - width));

        if (screen.Height > 0)
            y = Math.Max(0, Math.Min(y, screen.Height - height));

        return new WaylandWindowGeometry(x, y, width, height);
    }

    /// <summary>Смещение точки привязки внутри области.</summary>
    private static int AnchorOffset(uint value, int size, bool isHorizontal) => value switch
    {
        AnchorTop or AnchorBottom when isHorizontal => size / 2,
        AnchorLeft or AnchorRight when !isHorizontal => size / 2,
        AnchorRight or AnchorTopRight or AnchorBottomRight when isHorizontal => size,
        AnchorBottom or AnchorBottomLeft or AnchorBottomRight when !isHorizontal => size,
        AnchorNone => size / 2,
        _ => 0,
    };

    /// <summary>Сдвиг окна относительно точки привязки по направлению роста.</summary>
    private static int GravityOffset(uint value, int size, bool isHorizontal) => value switch
    {
        GravityLeft or GravityTopLeft or GravityBottomLeft when isHorizontal => -size,
        GravityTop or GravityTopLeft or GravityTopRight when !isHorizontal => -size,
        GravityTop or GravityBottom when isHorizontal => -size / 2,
        GravityLeft or GravityRight when !isHorizontal => -size / 2,
        GravityNone => -size / 2,
        _ => 0,
    };

    private int width;
    private int height;
    private int anchorX;
    private int anchorY;
    private int anchorWidth;
    private int anchorHeight;
    private int offsetX;
    private int offsetY;
    private uint anchor;
    private uint gravity;

    private const ushort RequestDestroy = 0;
    private const ushort RequestSetSize = 1;
    private const ushort RequestSetAnchorRect = 2;
    private const ushort RequestSetAnchor = 3;
    private const ushort RequestSetGravity = 4;
    private const ushort RequestSetOffset = 6;

    private const uint AnchorNone = 0;
    private const uint AnchorTop = 1;
    private const uint AnchorBottom = 2;
    private const uint AnchorLeft = 3;
    private const uint AnchorRight = 4;
    private const uint AnchorTopRight = 7;
    private const uint AnchorBottomLeft = 6;
    private const uint AnchorBottomRight = 8;

    private const uint GravityNone = 0;
    private const uint GravityTop = 1;
    private const uint GravityBottom = 2;
    private const uint GravityLeft = 3;
    private const uint GravityRight = 4;
    private const uint GravityTopLeft = 5;
    private const uint GravityBottomLeft = 6;
    private const uint GravityTopRight = 7;
}

/// <summary>
/// Объект <c lang="text">xdg_popup</c> — всплывающее окно.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class XdgPopup : WaylandObject
{
    /// <summary>Поверхность всплывающего окна.</summary>
    public required uint OwnerSurfaceId { get; init; }

    /// <summary>Родительская оболочечная поверхность.</summary>
    public required uint ParentXdgSurfaceId { get; init; }

    /// <summary>Положение и размер относительно родителя.</summary>
    public WaylandWindowGeometry Bounds { get; init; }

    /// <inheritdoc/>
    public override string InterfaceName => "xdg_popup";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        switch (message.Opcode)
        {
            case RequestDestroy:
                DetachSurface();
                Client.Unregister(Id);
                break;

            case RequestGrab:
                // Захват ввода мы не ведём: окно одно, и перехватывать события не у кого.
                break;
        }
    }

    /// <summary>Сообщает клиенту положение и размер.</summary>
    public void SendConfigure() => Emit(EventConfigure, writer => writer
        .WriteInt(Bounds.X)
        .WriteInt(Bounds.Y)
        .WriteInt(Bounds.Width > 0 ? Bounds.Width : DefaultWidth)
        .WriteInt(Bounds.Height > 0 ? Bounds.Height : DefaultHeight));

    /// <summary>Сообщает, что всплывающее окно снято оболочкой.</summary>
    public void SendPopupDone() => Emit(EventPopupDone);

    private void DetachSurface()
    {
        if (Client.Find<WaylandSurface>(OwnerSurfaceId) is not { } surface)
            return;

        surface.Role = WaylandSurfaceRole.None;
        surface.ParentSurfaceId = 0;
        surface.Position = (0, 0);
    }

    private const ushort RequestDestroy = 0;
    private const ushort RequestGrab = 1;

    private const ushort EventConfigure = 0;
    private const ushort EventPopupDone = 1;

    private const int DefaultWidth = 200;
    private const int DefaultHeight = 100;
}
