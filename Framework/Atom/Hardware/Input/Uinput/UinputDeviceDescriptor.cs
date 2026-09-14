namespace Atom.Hardware.Input.Uinput;

/// <summary>
/// Описание создаваемого uinput-устройства.
/// </summary>
public sealed record UinputDeviceDescriptor
{
    /// <summary>
    /// Имя устройства, каким его увидит система.
    /// </summary>
    /// <remarks>
    /// Ядро отдаёт его libinput, тот — композитору и дальше браузеру. Имена вроде «virtual» или
    /// «test» здесь были бы следом автоматики, поэтому задавать нужно правдоподобное.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>Типы событий, которые устройство умеет отправлять.</summary>
    public IReadOnlyList<int> EventTypes { get; init; } = [];

    /// <summary>Коды кнопок и клавиш.</summary>
    public IReadOnlyList<int> Keys { get; init; } = [];

    /// <summary>Относительные оси.</summary>
    public IReadOnlyList<int> RelativeAxes { get; init; } = [];

    /// <summary>Абсолютные оси с диапазонами.</summary>
    public IReadOnlyList<UinputAbsoluteAxis> AbsoluteAxes { get; init; } = [];

    /// <summary>
    /// Свойства устройства.
    /// </summary>
    /// <remarks>
    /// Отличают класс устройства: <c lang="text">INPUT_PROP_DIRECT</c> у тачскрина говорит, что касание
    /// приходит туда, куда указали, без курсора-посредника, а <c lang="text">INPUT_PROP_POINTER</c> —
    /// наоборот, у тачпада.
    /// </remarks>
    public IReadOnlyList<int> Properties { get; init; } = [];

    /// <summary>Шина устройства.</summary>
    public ushort BusType { get; init; } = UinputCodes.BUS_USB;

    /// <summary>Идентификатор производителя.</summary>
    public ushort VendorId { get; init; }

    /// <summary>Идентификатор продукта.</summary>
    public ushort ProductId { get; init; }

    /// <summary>Версия устройства.</summary>
    public ushort Version { get; init; } = 1;

    /// <summary>
    /// Пауза после создания, за которую udev регистрирует узел и применяет правила.
    /// </summary>
    public TimeSpan RegistrationDelay { get; init; } = TimeSpan.FromMilliseconds(120);
}

/// <summary>
/// Абсолютная ось устройства.
/// </summary>
/// <param name="Code">Код оси.</param>
/// <param name="Minimum">Нижняя граница.</param>
/// <param name="Maximum">Верхняя граница.</param>
/// <param name="Resolution">Разрешение в единицах на миллиметр; 0 — не указано.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct UinputAbsoluteAxis(ushort Code, int Minimum, int Maximum, int Resolution = 0);
