#pragma warning disable CA1024

namespace Atom.Media;

/// <summary>
/// Реестр кодеков — фабрика для создания кодеков по ID.
/// </summary>
public static class CodecRegistry
{
    private static readonly Dictionary<MediaCodecId, Func<IVideoCodec>> videoCodecFactories = new()
    {
        [MediaCodecId.RawRgb24] = static () => new RawVideoCodec(),
        [MediaCodecId.RawRgba32] = static () => new RawVideoCodec(),
        [MediaCodecId.RawBgr24] = static () => new RawVideoCodec(),
        [MediaCodecId.RawBgra32] = static () => new RawVideoCodec(),
        [MediaCodecId.RawYuv420P] = static () => new RawVideoCodec(),
        [MediaCodecId.RawYuv422P] = static () => new RawVideoCodec(),
        [MediaCodecId.RawYuv444P] = static () => new RawVideoCodec(),
        [MediaCodecId.RawNv12] = static () => new RawVideoCodec(),
        [MediaCodecId.RawNv21] = static () => new RawVideoCodec(),
    };

    private static readonly Dictionary<MediaCodecId, Func<IImageCodec>> imageCodecFactories = new()
    {
        [MediaCodecId.Png] = static () => new PngCodec(),
        [MediaCodecId.WebP] = static () => new WebpCodec(),
    };

    private static readonly Dictionary<MediaCodecId, Func<IAudioCodec>> audioCodecFactories = new()
    {
        [MediaCodecId.PcmS16Le] = static () => new RawAudioCodec(),
        [MediaCodecId.PcmS16Be] = static () => new RawAudioCodec(),
        [MediaCodecId.PcmS32Le] = static () => new RawAudioCodec(),
        [MediaCodecId.PcmS32Be] = static () => new RawAudioCodec(),
        [MediaCodecId.PcmF32Le] = static () => new RawAudioCodec(),
        [MediaCodecId.PcmF32Be] = static () => new RawAudioCodec(),
        [MediaCodecId.PcmF64Le] = static () => new RawAudioCodec(),
        [MediaCodecId.PcmU8] = static () => new RawAudioCodec(),
        [MediaCodecId.PcmALaw] = static () => new RawAudioCodec(),
        [MediaCodecId.PcmMuLaw] = static () => new RawAudioCodec(),
        [MediaCodecId.Flac] = static () => new FlacDecoder(),
        [MediaCodecId.Alac] = static () => new AlacDecoder(),
    };

    /// <summary>
    /// Регистрирует фабрику видеокодека.
    /// </summary>
    public static void RegisterVideoCodec(MediaCodecId codecId, Func<IVideoCodec> factory) => videoCodecFactories[codecId] = factory;

    /// <summary>
    /// Регистрирует фабрику аудиокодека.
    /// </summary>
    public static void RegisterAudioCodec(MediaCodecId codecId, Func<IAudioCodec> factory) => audioCodecFactories[codecId] = factory;

    /// <summary>
    /// Регистрирует фабрику image-кодека.
    /// </summary>
    public static void RegisterImageCodec(MediaCodecId codecId, Func<IImageCodec> factory) => imageCodecFactories[codecId] = factory;

    /// <summary>
    /// Создаёт видеокодек по ID.
    /// </summary>
    /// <returns>Кодек или null, если не зарегистрирован.</returns>
    public static IVideoCodec? CreateVideoCodec(MediaCodecId codecId)
        => videoCodecFactories.TryGetValue(codecId, out var factory) ? factory() : null;

    /// <summary>
    /// Создаёт аудиокодек по ID.
    /// </summary>
    /// <returns>Кодек или null, если не зарегистрирован.</returns>
    public static IAudioCodec? CreateAudioCodec(MediaCodecId codecId)
        => audioCodecFactories.TryGetValue(codecId, out var factory) ? factory() : null;

    /// <summary>
    /// Создаёт image-кодек по ID.
    /// </summary>
    public static IImageCodec? CreateImageCodec(MediaCodecId codecId)
        => imageCodecFactories.TryGetValue(codecId, out var factory) ? factory() : null;

    /// <summary>
    /// Создаёт image-кодек по расширению файла.
    /// </summary>
    public static IImageCodec? CreateImageCodec(string extension)
        => CreateImageCodec(GetImageCodecId(extension));

    /// <summary>
    /// Проверяет, зарегистрирован ли видеокодек.
    /// </summary>
    public static bool IsVideoCodecRegistered(MediaCodecId codecId)
        => videoCodecFactories.ContainsKey(codecId);

    /// <summary>
    /// Проверяет, зарегистрирован ли аудиокодек.
    /// </summary>
    public static bool IsAudioCodecRegistered(MediaCodecId codecId)
        => audioCodecFactories.ContainsKey(codecId);

    /// <summary>
    /// Проверяет, зарегистрирован ли image-кодек.
    /// </summary>
    public static bool IsImageCodecRegistered(MediaCodecId codecId)
        => imageCodecFactories.ContainsKey(codecId);

    /// <summary>
    /// Возвращает все зарегистрированные видеокодеки.
    /// </summary>
    public static IEnumerable<MediaCodecId> GetRegisteredVideoCodecs()
        => videoCodecFactories.Keys;

    /// <summary>
    /// Возвращает все зарегистрированные аудиокодеки.
    /// </summary>
    public static IEnumerable<MediaCodecId> GetRegisteredAudioCodecs()
        => audioCodecFactories.Keys;

    /// <summary>
    /// Возвращает все зарегистрированные image-кодеки.
    /// </summary>
    public static IEnumerable<MediaCodecId> GetRegisteredImageCodecs()
        => imageCodecFactories.Keys;

    /// <summary>
    /// Определяет image codec ID по расширению.
    /// </summary>
    public static MediaCodecId GetImageCodecId(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        return extension.ToUpperInvariant() switch
        {
            ".PNG" => MediaCodecId.Png,
            ".WEBP" => MediaCodecId.WebP,
            _ => MediaCodecId.Unknown,
        };
    }

    /// <summary>
    /// Определяет ID кодека по FOURCC.
    /// </summary>
    public static MediaCodecId FromFourCC(uint fourcc) => fourcc switch
    {
        // Video
        0x34363248 => MediaCodecId.H264,      // 'H264'
        0x31637661 => MediaCodecId.H264,      // 'avc1'
        0x35363248 => MediaCodecId.H265,      // 'H265'
        0x31637668 => MediaCodecId.H265,      // 'hvc1'
        0x30385056 => MediaCodecId.Vp8,       // 'VP80'
        0x30395056 => MediaCodecId.Vp9,       // 'VP90'
        0x31305641 => MediaCodecId.Av1,       // 'AV01'
        0x4745504A => MediaCodecId.Mjpeg,     // 'JPEG'
        0x47504A4D => MediaCodecId.Mjpeg,     // 'MJPG'

        // Audio
        0x6D703461 => MediaCodecId.Aac,       // 'mp4a'
        0x7375704F => MediaCodecId.Opus,      // 'Opus'
        0x33706D2E => MediaCodecId.Mp3,       // '.mp3'

        _ => MediaCodecId.Unknown,
    };

    /// <summary>
    /// Определяет ID кодека по MIME типу.
    /// </summary>
    public static MediaCodecId FromMimeType(string mimeType) => mimeType switch
    {
        // Video
        "video/avc" or "video/h264" => MediaCodecId.H264,
        "video/hevc" or "video/h265" => MediaCodecId.H265,
        "video/vp8" => MediaCodecId.Vp8,
        "video/vp9" => MediaCodecId.Vp9,
        "video/av1" or "video/av01" => MediaCodecId.Av1,
        "image/jpeg" or "video/mjpeg" => MediaCodecId.Mjpeg,
        "image/png" => MediaCodecId.Png,
        "image/bmp" => MediaCodecId.Bmp,
        "image/webp" => MediaCodecId.WebP,

        // Audio
        "audio/aac" or "audio/mp4a-latm" => MediaCodecId.Aac,
        "audio/opus" => MediaCodecId.Opus,
        "audio/mpeg" or "audio/mp3" => MediaCodecId.Mp3,
        "audio/vorbis" or "audio/ogg" => MediaCodecId.Vorbis,
        "audio/flac" => MediaCodecId.Flac,

        // Raw
        "audio/pcm" or "audio/L16" => MediaCodecId.PcmS16Le,
        "video/raw" => MediaCodecId.RawRgb24,

        _ => MediaCodecId.Unknown,
    };
}
