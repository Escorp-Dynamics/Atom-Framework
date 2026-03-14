#pragma warning disable CA1024

namespace Atom.Media;

/// <summary>
/// Фабрика для создания demuxer/muxer.
/// </summary>
public static class ContainerFactory
{
    private static readonly Dictionary<string, Func<IDemuxer>> demuxerFactories = new(StringComparer.Ordinal)
    {
        ["wav"] = static () => new WavDemuxer(),
        ["mp3"] = static () => new Mp3Demuxer(),
        ["ogg"] = static () => new OggDemuxer(),
        ["matroska"] = static () => new MatroskaDemuxer(),
        ["webm"] = static () => new MatroskaDemuxer(),
        ["flac"] = static () => new FlacDemuxer(),
        ["aac"] = static () => new AacDemuxer(),
    };

    private static readonly Dictionary<string, Func<IMuxer>> muxerFactories = new(StringComparer.Ordinal)
    {
        ["wav"] = static () => new WavMuxer(),
        ["ogg"] = static () => new OggMuxer(),
        ["matroska"] = static () => new MatroskaMuxer(),
        ["webm"] = static () => new MatroskaMuxer(),
    };

    /// <summary>
    /// Регистрирует demuxer для формата.
    /// </summary>
    public static void RegisterDemuxer(string formatName, Func<IDemuxer> factory)
    {
        ArgumentNullException.ThrowIfNull(formatName);
        demuxerFactories[formatName.ToLowerInvariant()] = factory;
    }

    /// <summary>
    /// Регистрирует muxer для формата.
    /// </summary>
    public static void RegisterMuxer(string formatName, Func<IMuxer> factory)
    {
        ArgumentNullException.ThrowIfNull(formatName);
        muxerFactories[formatName.ToLowerInvariant()] = factory;
    }

    /// <summary>
    /// Создаёт demuxer для формата.
    /// </summary>
    public static IDemuxer? CreateDemuxer(string formatName)
    {
        ArgumentNullException.ThrowIfNull(formatName);
        return demuxerFactories.TryGetValue(formatName.ToLowerInvariant(), out var factory) ? factory() : null;
    }

    /// <summary>
    /// Создаёт muxer для формата.
    /// </summary>
    public static IMuxer? CreateMuxer(string formatName)
    {
        ArgumentNullException.ThrowIfNull(formatName);
        return muxerFactories.TryGetValue(formatName.ToLowerInvariant(), out var factory) ? factory() : null;
    }

    /// <summary>
    /// Определяет формат по расширению файла.
    /// </summary>
    public static string? GetFormatFromExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return extension.ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" or ".m4a" => "mp4",
            ".webm" => "webm",
            ".mkv" => "matroska",
            ".avi" => "avi",
            ".mov" => "mov",
            ".ts" or ".m2ts" => "mpegts",
            ".flv" => "flv",
            ".ogg" or ".ogv" or ".oga" => "ogg",
            ".wav" => "wav",
            ".mp3" => "mp3",
            ".flac" => "flac",
            ".aac" => "aac",
            _ => null,
        };
    }

    /// <summary>
    /// Возвращает зарегистрированные форматы demuxer.
    /// </summary>
    public static IEnumerable<string> GetRegisteredDemuxers() => demuxerFactories.Keys;

    /// <summary>
    /// Возвращает зарегистрированные форматы muxer.
    /// </summary>
    public static IEnumerable<string> GetRegisteredMuxers() => muxerFactories.Keys;
}
