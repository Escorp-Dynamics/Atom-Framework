// Имена повторяют linux/input-event-codes.h дословно: сверка с заголовком ядра важнее стиля.
#pragma warning disable CA1707, CS1591

namespace Atom.Hardware.Input.Uinput;

/// <summary>
/// Коды подсистемы ввода Linux из <c lang="text">linux/input-event-codes.h</c>.
/// </summary>
public static class UinputCodes
{
    /// <summary>Синхронизация пакета событий.</summary>
    public const ushort EV_SYN = 0x00;

    /// <summary>Кнопки и клавиши.</summary>
    public const ushort EV_KEY = 0x01;

    /// <summary>Относительные перемещения.</summary>
    public const ushort EV_REL = 0x02;

    /// <summary>Абсолютные координаты.</summary>
    public const ushort EV_ABS = 0x03;

    /// <summary>Конец пакета: до него ядро события не публикует.</summary>
    public const ushort SYN_REPORT = 0x00;

    public const ushort BTN_LEFT = 0x110;
    public const ushort BTN_RIGHT = 0x111;
    public const ushort BTN_MIDDLE = 0x112;

    /// <summary>Признак контакта с сенсорной поверхностью.</summary>
    public const ushort BTN_TOUCH = 0x14A;

    public const ushort ABS_X = 0x00;
    public const ushort ABS_Y = 0x01;

    public const ushort REL_X = 0x00;
    public const ushort REL_Y = 0x01;
    public const ushort REL_WHEEL = 0x08;
    public const ushort REL_HWHEEL = 0x06;

    /// <summary>Слот мультитача: протокол B адресует касания по слотам.</summary>
    public const ushort ABS_MT_SLOT = 0x2F;

    public const ushort ABS_MT_TOUCH_MAJOR = 0x30;
    public const ushort ABS_MT_POSITION_X = 0x35;
    public const ushort ABS_MT_POSITION_Y = 0x36;

    /// <summary>Идентификатор касания; -1 закрывает касание.</summary>
    public const ushort ABS_MT_TRACKING_ID = 0x39;

    public const ushort ABS_MT_PRESSURE = 0x3A;

    /// <summary>Прямой ввод: касание приходит туда, куда указали.</summary>
    public const int INPUT_PROP_DIRECT = 0x01;

    /// <summary>Косвенный ввод: устройство двигает курсор.</summary>
    public const int INPUT_PROP_POINTER = 0x00;

    public const ushort BUS_USB = 0x03;
    public const ushort BUS_VIRTUAL = 0x06;
}
