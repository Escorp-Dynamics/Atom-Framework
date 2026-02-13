namespace Atom.Media;

/// <summary>
/// Интерфейс мультиплексора (пишет контейнер).
/// </summary>
public interface IMuxer : IDisposable
{
    /// <summary>
    /// Возвращает true, если контейнер открыт для записи.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Количество добавленных потоков.
    /// </summary>
    int StreamCount { get; }

    /// <summary>
    /// Открывает для записи.
    /// </summary>
    ContainerResult Open(in MuxerParameters parameters);

    /// <summary>
    /// Добавляет видео поток.
    /// </summary>
    /// <returns>Индекс потока.</returns>
    (ContainerResult Result, int StreamIndex) AddVideoStream(in VideoCodecParameters parameters, MediaCodecId codecId);

    /// <summary>
    /// Добавляет аудио поток.
    /// </summary>
    /// <returns>Индекс потока.</returns>
    (ContainerResult Result, int StreamIndex) AddAudioStream(in AudioCodecParameters parameters, MediaCodecId codecId);

    /// <summary>
    /// Записывает заголовок (вызвать после добавления всех потоков).
    /// </summary>
    ContainerResult WriteHeader();

    /// <summary>
    /// Записывает пакет.
    /// </summary>
    ContainerResult WritePacket(in MediaPacket packet);

    /// <summary>
    /// Асинхронно записывает пакет.
    /// </summary>
    ValueTask<ContainerResult> WritePacketAsync(
        MediaPacketBuffer packet,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Записывает trailer и закрывает контейнер.
    /// </summary>
    ContainerResult WriteTrailer();
}
