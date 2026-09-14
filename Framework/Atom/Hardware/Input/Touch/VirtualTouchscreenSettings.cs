using System.Drawing;

namespace Atom.Hardware.Input.Touch;

/// <summary>
/// Параметры виртуального тачскрина.
/// </summary>
public sealed record VirtualTouchscreenSettings
{
    /// <summary>
    /// Имя устройства, каким его увидит система.
    /// </summary>
    /// <remarks>
    /// Ядро отдаёт это имя в <c lang="text">libinput</c>, а тот — в X11 и дальше в браузер. Имена вида
    /// «virtual» или «test» здесь были бы тем же следом автоматики, что и любой другой, поэтому по
    /// умолчанию берём правдоподобное для встроенного сенсорного экрана.
    /// </remarks>
    public string Name { get; init; } = "ELAN Touchscreen";

    /// <summary>
    /// Разрешение сенсорной матрицы в точках.
    /// </summary>
    /// <remarks>
    /// ★ ОБЯЗАНО совпадать с разрешением выхода композитора. Ядро передаёт координату касания как
    /// долю диапазона матрицы, а композитор разворачивает её обратно уже по размеру выхода: при
    /// расхождении касание уезжает мимо окна и молча теряется. Замер: матрица 1920×1080 против
    /// headless-выхода 1280×720 — ни одно из шести касаний до страницы не дошло; после
    /// выравнивания дошли все.
    /// </remarks>
    public Size ScreenSize { get; init; } = new(1920, 1080);

    /// <summary>
    /// Сколько одновременных касаний объявляет устройство.
    /// </summary>
    public int MaxTouchPoints { get; init; } = 5;

    /// <summary>
    /// Идентификатор производителя USB; null — шина виртуального устройства.
    /// </summary>
    public ushort? UsbVendorId { get; init; }

    /// <summary>
    /// Идентификатор продукта USB.
    /// </summary>
    public ushort? UsbProductId { get; init; }
}
