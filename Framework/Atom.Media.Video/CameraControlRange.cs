namespace Atom.Media.Video;

/// <summary>
/// Диапазон и значения по умолчанию для контрола камеры.
/// </summary>
/// <param name="Min">Минимальное значение.</param>
/// <param name="Max">Максимальное значение.</param>
/// <param name="Default">Значение по умолчанию.</param>
public sealed record CameraControlRange(float Min, float Max, float Default);
