namespace Atom.Media.Audio;

/// <summary>
/// Аргументы события изменения контрола виртуального микрофона.
/// </summary>
public sealed class MicrophoneControlChangedEventArgs : EventArgs
{
    /// <summary>
    /// Тип контрола.
    /// </summary>
    public required MicrophoneControlType Control { get; init; }

    /// <summary>
    /// Текущее значение контрола.
    /// </summary>
    public required float Value { get; init; }

    /// <summary>
    /// Диапазон контрола (если известен).
    /// </summary>
    public MicrophoneControlRange? Range { get; init; }
}
