#pragma warning disable CA1028

namespace Atom.Media;

/// <summary>
/// Тип медиа потока.
/// </summary>
public enum MediaStreamType : byte
{
    /// <summary>Неизвестный тип.</summary>
    Unknown = 0,

    /// <summary>Видео поток.</summary>
    Video = 1,

    /// <summary>Аудио поток.</summary>
    Audio = 2,

    /// <summary>Субтитры.</summary>
    Subtitle = 3,

    /// <summary>Данные (метаданные, тайминги).</summary>
    Data = 4,
}
