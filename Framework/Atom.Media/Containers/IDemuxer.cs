namespace Atom.Media;

/// <summary>
/// Интерфейс демультиплексора (читает контейнер).
/// </summary>
public interface IDemuxer : IDisposable
{
    /// <summary>
    /// Информация о контейнере.
    /// </summary>
    ContainerInfo ContainerInfo { get; }

    /// <summary>
    /// Потоки в контейнере.
    /// </summary>
    IReadOnlyList<MediaStreamInfo> Streams { get; }

    /// <summary>
    /// Индекс лучшего видео потока (-1 если нет).
    /// </summary>
    int BestVideoStreamIndex { get; }

    /// <summary>
    /// Индекс лучшего аудио потока (-1 если нет).
    /// </summary>
    int BestAudioStreamIndex { get; }

    /// <summary>
    /// Открывает файл или URL.
    /// </summary>
    ContainerResult Open(string path);

    /// <summary>
    /// Открывает из потока.
    /// </summary>
    ContainerResult Open(Stream stream);

    /// <summary>
    /// Читает следующий пакет.
    /// </summary>
    /// <param name="packet">Буфер для пакета.</param>
    /// <returns>Результат чтения.</returns>
    ContainerResult ReadPacket(MediaPacketBuffer packet);

    /// <summary>
    /// Асинхронно читает следующий пакет.
    /// </summary>
    ValueTask<ContainerResult> ReadPacketAsync(
        MediaPacketBuffer packet,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Перемещается к указанному времени.
    /// </summary>
    /// <param name="timestamp">Целевое время.</param>
    /// <param name="streamIndex">Индекс потока (-1 для default).</param>
    /// <param name="seekToKeyframe">Искать ближайший keyframe.</param>
    ContainerResult Seek(TimeSpan timestamp, int streamIndex = -1, bool seekToKeyframe = true);

    /// <summary>
    /// Сбрасывает состояние (перечитывает с начала).
    /// </summary>
    void Reset();
}
