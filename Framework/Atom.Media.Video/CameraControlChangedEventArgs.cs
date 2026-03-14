namespace Atom.Media.Video;

/// <summary>
/// Аргументы события изменения контрола камеры.
/// </summary>
public sealed class CameraControlChangedEventArgs : EventArgs
{
    /// <summary>
    /// Тип контрола.
    /// </summary>
    public required CameraControlType Control { get; init; }

    /// <summary>
    /// Новое значение контрола.
    /// </summary>
    public required float Value { get; init; }

    /// <summary>
    /// Диапазон контрола (min, max, default). Может быть null, если диапазон неизвестен.
    /// </summary>
    public CameraControlRange? Range { get; init; }
}
