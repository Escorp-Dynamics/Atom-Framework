namespace Atom.Media.Audio;

/// <summary>
/// Тип контрола виртуального микрофона.
/// </summary>
public enum MicrophoneControlType
{
    /// <summary>Уровень громкости (0.0–1.0).</summary>
    Volume = 0,

    /// <summary>Отключение звука (0.0 = вкл, 1.0 = mute).</summary>
    Mute = 1,
}
