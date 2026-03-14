namespace Atom.Media.Audio;

/// <summary>
/// Диапазон контрола виртуального микрофона (min, max, default).
/// </summary>
/// <param name="Min">Минимальное значение.</param>
/// <param name="Max">Максимальное значение.</param>
/// <param name="Default">Значение по умолчанию.</param>
public sealed record MicrophoneControlRange(float Min, float Max, float Default);
