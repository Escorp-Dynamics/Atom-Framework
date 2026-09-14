using System.Runtime.Versioning;

using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland.Objects;

/// <summary>
/// Объект <c lang="text">wl_seat</c> — набор устройств ввода.
/// </summary>
/// <remarks>
/// ★ Возможности seat наблюдаемы страницей. Заявленный сенсорный ввод делает
/// <c lang="text">navigator.maxTouchPoints</c> ненулевым и включает <c lang="text">ontouchstart</c> — то есть состав
/// seat должен соответствовать личности, под которую мы маскируемся: у настольного профиля
/// касаний быть не должно, у мобильного — наоборот.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandSeat : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_seat";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        var offset = 0;

        switch (message.Opcode)
        {
            case RequestGetPointer:
                Client.Register(new WaylandPointer { Id = message.ReadUInt(ref offset), Version = Version, Client = Client });
                break;

            case RequestGetKeyboard:
                HandleGetKeyboard(message.ReadUInt(ref offset));
                break;

            case RequestGetTouch:
                Client.Register(new WaylandTouch { Id = message.ReadUInt(ref offset), Version = Version, Client = Client });
                break;

            case RequestRelease:
                Client.Unregister(Id);
                break;
        }
    }

    /// <summary>
    /// Объявляет состав устройств ввода.
    /// </summary>
    /// <remarks>
    /// Порядок как в эталонной реализации: сперва возможности, затем имя. Обратный порядок клиент
    /// принимает, но расхождение с привычным поведением наблюдаемо.
    /// </remarks>
    public void SendCapabilities(bool hasTouch)
    {
        var capabilities = CapabilityPointer | CapabilityKeyboard;
        if (hasTouch)
            capabilities |= CapabilityTouch;

        Emit(EventCapabilities, writer => writer.WriteUInt(capabilities));

        if (Version >= 2)
            Emit(EventName, writer => writer.WriteString("seat0"));
    }

    private void HandleGetKeyboard(uint keyboardId)
    {
        var keyboard = new WaylandKeyboard { Id = keyboardId, Version = Version, Client = Client };
        Client.Register(keyboard);
        keyboard.SendKeymap();
    }

    private const ushort RequestGetPointer = 0;
    private const ushort RequestGetKeyboard = 1;
    private const ushort RequestGetTouch = 2;
    private const ushort RequestRelease = 3;

    private const ushort EventCapabilities = 0;
    private const ushort EventName = 1;

    private const uint CapabilityPointer = 1;
    private const uint CapabilityKeyboard = 2;
    private const uint CapabilityTouch = 4;
}

/// <summary>
/// Объект <c lang="text">wl_touch</c> — сенсорный ввод.
/// </summary>
/// <remarks>
/// ★ Ради него всё и делалось. Настоящее касание приходит страницей как
/// <c lang="text">pointerType = "touch"</c> с <c lang="text">isTrusted = true</c>; мышиный клик по документу,
/// заявляющему телефон, невозможен физически и потому виден проверкам.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class WaylandTouch : WaylandObject
{
    /// <inheritdoc/>
    public override string InterfaceName => "wl_touch";

    /// <inheritdoc/>
    public override void HandleRequest(in WaylandMessage message)
    {
        if (message.Opcode == RequestRelease)
            Client.Unregister(Id);
    }

    /// <summary>Начало касания в точке поверхности.</summary>
    public void SendDown(uint serial, uint timestamp, uint surfaceId, int touchId, double x, double y)
        => Emit(EventDown, writer => writer
            .WriteUInt(serial)
            .WriteUInt(timestamp)
            .WriteUInt(surfaceId)
            .WriteInt(touchId)
            .WriteFixed(x)
            .WriteFixed(y));

    /// <summary>Окончание касания.</summary>
    public void SendUp(uint serial, uint timestamp, int touchId)
        => Emit(EventUp, writer => writer
            .WriteUInt(serial)
            .WriteUInt(timestamp)
            .WriteInt(touchId));

    /// <summary>Перемещение пальца без отрыва.</summary>
    public void SendMotion(uint timestamp, int touchId, double x, double y)
        => Emit(EventMotion, writer => writer
            .WriteUInt(timestamp)
            .WriteInt(touchId)
            .WriteFixed(x)
            .WriteFixed(y));

    /// <summary>
    /// Завершает пакет касаний.
    /// </summary>
    /// <remarks>
    /// Без него клиент не обрабатывает касание: события копятся до кадра, как и у указателя.
    /// </remarks>
    public void SendFrame() => Emit(EventFrame);

    private const ushort RequestRelease = 0;

    private const ushort EventDown = 0;
    private const ushort EventUp = 1;
    private const ushort EventMotion = 2;
    private const ushort EventFrame = 3;
}
