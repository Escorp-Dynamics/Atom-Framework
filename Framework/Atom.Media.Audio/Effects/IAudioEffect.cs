namespace Atom.Media.Audio.Effects;

/// <summary>
/// Аудио эффект, обрабатывающий семплы в формате float [-1.0, 1.0] (interleaved).
/// </summary>
public interface IAudioEffect
{
    /// <summary>
    /// Определяет, включён ли эффект.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Обрабатывает аудио блок (interleaved float семплы).
    /// </summary>
    /// <param name="samples">Семплы в формате float [-1.0, 1.0], interleaved по каналам.</param>
    /// <param name="channels">Количество аудиоканалов.</param>
    /// <param name="sampleRate">Частота дискретизации в герцах.</param>
    void Process(Span<float> samples, int channels, int sampleRate);

    /// <summary>
    /// Сбрасывает внутреннее состояние эффекта.
    /// </summary>
    void Reset();
}
