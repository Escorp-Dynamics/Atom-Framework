namespace Atom.Media;

/// <summary>
/// Информация о медиа контейнере.
/// </summary>
public readonly record struct ContainerInfo
{
    /// <summary>Формат контейнера (mp4, webm, mkv и т.д.).</summary>
    public required string FormatName { get; init; }

    /// <summary>Длительность в микросекундах.</summary>
    public long DurationUs { get; init; }

    /// <summary>Общий битрейт в bps.</summary>
    public long BitRate { get; init; }

    /// <summary>Размер файла в байтах (0 для потоков).</summary>
    public long FileSize { get; init; }

    /// <summary>Поддерживает ли seek.</summary>
    public bool IsSeekable { get; init; }

    /// <summary>Это живой поток (без известной длительности).</summary>
    public bool IsLiveStream { get; init; }

    /// <summary>Количество потоков.</summary>
    public int StreamCount { get; init; }

    /// <summary>
    /// Длительность как TimeSpan.
    /// </summary>
    public TimeSpan Duration => DurationUs >= 0 ? TimeSpan.FromMicroseconds(DurationUs) : TimeSpan.Zero;
}
