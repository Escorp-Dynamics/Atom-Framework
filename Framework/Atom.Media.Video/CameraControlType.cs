namespace Atom.Media.Video;

/// <summary>
/// Тип контрола камеры (UVC-совместимые контролы видеоустройства).
/// </summary>
public enum CameraControlType
{
    /// <summary>
    /// Яркость изображения.
    /// </summary>
    Brightness,

    /// <summary>
    /// Контрастность изображения.
    /// </summary>
    Contrast,

    /// <summary>
    /// Насыщенность цвета.
    /// </summary>
    Saturation,

    /// <summary>
    /// Оттенок цвета (сдвиг по цветовому кругу).
    /// </summary>
    Hue,

    /// <summary>
    /// Гамма-коррекция.
    /// </summary>
    Gamma,

    /// <summary>
    /// Экспозиция (время выдержки).
    /// </summary>
    Exposure,

    /// <summary>
    /// Усиление сигнала (Gain/ISO).
    /// </summary>
    Gain,

    /// <summary>
    /// Резкость изображения.
    /// </summary>
    Sharpness,
}
