using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">wl_pointer</c> — указатель.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class WaylandPointer : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_pointer";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestSetCursor:
                HandleSetCursor(ref offset, message);
                break;

            case RequestRelease:
                Client.Unregister(Id);
                break;
        }
    }

    /// <summary>
    /// Клиент задаёт свой курсор.
    /// </summary>
    /// <remarks>
    /// ★ Курсор — полноценная поверхность со своими кадрами, а не имя из списка: браузер рисует
    /// его сам и меняет при наведении на ссылку или поле ввода. Его надо передать сессии хоста,
    /// иначе указатель над окном всегда остаётся стрелкой.
    /// </remarks>
    private void HandleSetCursor(ref int offset, in WaylandMessage message)
    {
        _ = message.ReadUInt(ref offset);
        var cursorSurfaceId = message.ReadUInt(ref offset);
        var hotspotX = message.ReadInt(ref offset);
        var hotspotY = message.ReadInt(ref offset);

        if (cursorSurfaceId == 0)
        {
            // Пустая поверхность означает скрытый курсор — так браузер прячет его над видео.
            Client.Compositor.HideCursor();
            return;
        }

        if (Client.Find<WaylandSurface>(cursorSurfaceId) is not { } cursor)
            return;

        cursor.Role = WaylandSurfaceRole.Cursor;
        cursor.CursorHotspot = (hotspotX, hotspotY);
    }

    /// <summary>Указатель вошёл в поверхность.</summary>
    public void SendEnter(uint serial, uint surfaceId, double x, double y)
        => Emit(EventEnter, writer => writer
            .WriteUInt(serial)
            .WriteUInt(surfaceId)
            .WriteFixed(x)
            .WriteFixed(y));

    /// <summary>Указатель покинул поверхность.</summary>
    public void SendLeave(uint serial, uint surfaceId)
        => Emit(EventLeave, writer => writer
            .WriteUInt(serial)
            .WriteUInt(surfaceId));

    /// <summary>Перемещение указателя.</summary>
    public void SendMotion(uint timestamp, double x, double y)
        => Emit(EventMotion, writer => writer
            .WriteUInt(timestamp)
            .WriteFixed(x)
            .WriteFixed(y));

    /// <summary>Прокрутка колесом.</summary>
    public void SendAxis(uint timestamp, uint axis, double value)
        => Emit(EventAxis, writer => writer
            .WriteUInt(timestamp)
            .WriteUInt(axis)
            .WriteFixed(value));

    /// <summary>Нажатие или отпускание кнопки.</summary>
    public void SendButton(uint serial, uint timestamp, uint button, bool pressed)
        => Emit(EventButton, writer => writer
            .WriteUInt(serial)
            .WriteUInt(timestamp)
            .WriteUInt(button)
            .WriteUInt(pressed ? StatePressed : StateReleased));

    /// <summary>Завершает пакет событий указателя.</summary>
    public void SendFrame()
    {
        if (Version >= 5)
            Emit(EventFrame);
    }

    /// <summary>Источник прокрутки: колесо или тачпад.</summary>
    public void SendAxisSource(uint source)
    {
        if (Version >= 5)
            Emit(EventAxisSource, writer => writer.WriteUInt(source));
    }

    /// <summary>Число щелчков колеса.</summary>
    public void SendAxisDiscrete(uint axis, int steps)
    {
        if (Version >= 5)
            Emit(EventAxisDiscrete, writer => writer.WriteUInt(axis).WriteInt(steps));
    }

    private const ushort RequestSetCursor = 0;
    private const ushort RequestRelease = 1;

    private const ushort EventEnter = 0;
    private const ushort EventLeave = 1;
    private const ushort EventMotion = 2;
    private const ushort EventButton = 3;
    private const ushort EventAxis = 4;
    private const ushort EventFrame = 5;
    private const ushort EventAxisSource = 6;
    private const ushort EventAxisDiscrete = 8;

    private const uint StateReleased = 0;
    private const uint StatePressed = 1;
}

/// <summary>
/// Объект <c lang="text">wl_keyboard</c> — клавиатура.
/// </summary>
/// <remarks>
/// ★ Раскладка передаётся клиенту файловым дескриптором в формате XKB. Без неё браузер не
/// обрабатывает нажатия вовсе: он не знает, какой символ соответствует коду клавиши.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed partial class WaylandKeyboard : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_keyboard";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        if (message.Opcode == RequestRelease)
            Client.Unregister(Id);
    }

    /// <summary>
    /// Отдаёт раскладку клиенту.
    /// </summary>
    /// <remarks>
    /// Минимальная раскладка задаётся текстом: тянуть xkbcommon ради неё незачем, а браузеру
    /// достаточно соответствия кодов символам латиницы.
    /// </remarks>
    public void SendKeymap()
    {
        var keymap = Encoding.UTF8.GetBytes(MinimalKeymap);
        var descriptor = CreateAnonymousFile(keymap);

        if (descriptor < 0)
            return;

        try
        {
            EmitWithDescriptor(EventKeymap, descriptor, writer => writer
                .WriteUInt(KeymapFormatXkbV1)
                .WriteUInt((uint)keymap.Length));
        }
        finally
        {
            _ = Close(descriptor);
        }

        if (Version >= 4)
            Emit(EventRepeatInfo, writer => writer.WriteInt(RepeatRate).WriteInt(RepeatDelay));
    }

    /// <summary>Клавиатурный фокус перешёл на поверхность.</summary>
    public void SendEnter(uint serial, uint surfaceId)
        => Emit(EventEnter, writer => writer
            .WriteUInt(serial)
            .WriteUInt(surfaceId)
            .WriteArray([]));

    /// <summary>Клавиатурный фокус ушёл с поверхности.</summary>
    public void SendLeave(uint serial, uint surfaceId)
        => Emit(EventLeave, writer => writer
            .WriteUInt(serial)
            .WriteUInt(surfaceId));

    /// <summary>Состояние модификаторов.</summary>
    public void SendModifiers(uint serial, uint depressed, uint latched, uint locked, uint group)
        => Emit(EventModifiers, writer => writer
            .WriteUInt(serial)
            .WriteUInt(depressed)
            .WriteUInt(latched)
            .WriteUInt(locked)
            .WriteUInt(group));

    /// <summary>Нажатие или отпускание клавиши.</summary>
    public void SendKey(uint serial, uint timestamp, uint key, bool pressed)
        => Emit(EventKey, writer => writer
            .WriteUInt(serial)
            .WriteUInt(timestamp)
            .WriteUInt(key)
            .WriteUInt(pressed ? StatePressed : StateReleased));

    /// <summary>
    /// Создаёт безымянный файл в памяти под раскладку.
    /// </summary>
    /// <remarks>
    /// Файл существует только пока открыт дескриптор — на диске ничего не остаётся.
    /// </remarks>
    private static int CreateAnonymousFile(byte[] content)
    {
        var descriptor = MemfdCreate("atom-keymap", MfdCloexec);
        if (descriptor < 0)
            return -1;

        if (Write(descriptor, content, (nuint)content.Length) < 0)
        {
            _ = Close(descriptor);
            return -1;
        }

        return descriptor;
    }

    private const ushort RequestRelease = 0;

    private const ushort EventKeymap = 0;
    private const ushort EventEnter = 1;
    private const ushort EventLeave = 2;
    private const ushort EventKey = 3;
    private const ushort EventModifiers = 4;
    private const ushort EventRepeatInfo = 5;

    private const uint KeymapFormatXkbV1 = 1;
    private const uint StateReleased = 0;
    private const uint StatePressed = 1;

    private const int RepeatRate = 25;
    private const int RepeatDelay = 600;
    private const uint MfdCloexec = 0x0001;

    /// <summary>Раскладка в текстовом формате XKB: латиница и основные управляющие клавиши.</summary>
    private const string MinimalKeymap = """
        xkb_keymap {
        xkb_keycodes { include "evdev+aliases(qwerty)" };
        xkb_types { include "complete" };
        xkb_compat { include "complete" };
        xkb_symbols { include "pc+us+inet(evdev)" };
        };
        """;

    [LibraryImport("libc", EntryPoint = "memfd_create", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int MemfdCreate(string name, uint flags);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial nint Write(int descriptor, byte[] buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Close(int descriptor);
}
