namespace Atom.Media;

/// <summary>
/// Параметры muxer'а.
/// </summary>
public readonly record struct MuxerParameters
{
    /// <summary>Имя формата (mp4, webm, mkv).</summary>
    public required string FormatName { get; init; }

    /// <summary>Путь к выходному файлу (null для потока).</summary>
    public string? OutputPath { get; init; }

    /// <summary>Выходной поток (null для файла).</summary>
    public Stream? OutputStream { get; init; }
}
