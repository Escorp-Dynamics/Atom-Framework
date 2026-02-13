using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Atom.Media;

/// <summary>
/// Базовый интерфейс кодека.
/// </summary>
public interface ICodec : IDisposable
{
    /// <summary>
    /// Идентификатор кодека.
    /// </summary>
    MediaCodecId CodecId { get; }

    /// <summary>
    /// Уровень аппаратного ускорения для внутренних алгоритмов.
    /// </summary>
    /// <remarks>
    /// По умолчанию <see cref="HardwareAcceleration.Auto"/> — автоматический выбор
    /// лучшего доступного ускорителя. Можно явно ограничить до <see cref="HardwareAcceleration.None"/>
    /// для отладки или <see cref="HardwareAcceleration.Sse41"/> / <see cref="HardwareAcceleration.Avx2"/>.
    /// </remarks>
    HardwareAcceleration Acceleration { get; init; }

    /// <summary>
    /// Человекочитаемое имя кодека.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Возможности кодека.
    /// </summary>
    CodecCapabilities Capabilities { get; }

    /// <summary>
    /// Логгер для трассировки и отладки операций кодека.
    /// </summary>
    /// <remarks>
    /// Если <see langword="null"/>, логирование отключено.
    /// Рекомендуется использовать <see cref="LogLevel.Debug"/> для детальной отладки
    /// и <see cref="LogLevel.Trace"/> для трассировки каждого блока/кадра.
    /// </remarks>
    ILogger? Logger { get; set; }

    /// <summary>
    /// Фабрика метрик для сбора данных о производительности.
    /// </summary>
    /// <remarks>
    /// Если <see langword="null"/>, метрики не собираются.
    /// Метрики включают: время кодирования/декодирования, количество кадров, байты и ошибки.
    /// </remarks>
    IMeterFactory? MeterFactory { get; set; }

    /// <summary>
    /// Возвращает true, если кодек поддерживает декодирование.
    /// </summary>
    bool CanDecode => (Capabilities & CodecCapabilities.Decode) != CodecCapabilities.None;

    /// <summary>
    /// Возвращает true, если кодек поддерживает кодирование.
    /// </summary>
    bool CanEncode => (Capabilities & CodecCapabilities.Encode) != CodecCapabilities.None;

    /// <summary>
    /// Возвращает true, если кодек использует аппаратное ускорение.
    /// </summary>
    bool IsHardwareAccelerated => (Capabilities & CodecCapabilities.HardwareAccelerated) != CodecCapabilities.None;

    /// <summary>
    /// Сбрасывает состояние кодека (для seek или смены потока).
    /// </summary>
    void Reset();
}
