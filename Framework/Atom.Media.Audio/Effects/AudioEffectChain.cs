namespace Atom.Media.Audio.Effects;

/// <summary>
/// Цепочка аудио эффектов. Применяет эффекты последовательно в порядке добавления.
/// </summary>
public sealed class AudioEffectChain : IAudioEffect
{
    private readonly List<IAudioEffect> effects = [];

    /// <summary>
    /// Определяет, включена ли цепочка.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Список эффектов в цепочке.
    /// </summary>
    public IReadOnlyList<IAudioEffect> Effects => effects;

    /// <summary>
    /// Добавляет эффект в конец цепочки.
    /// </summary>
    /// <param name="effect">Эффект для добавления.</param>
    public void Add(IAudioEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effects.Add(effect);
    }

    /// <summary>
    /// Удаляет эффект из цепочки.
    /// </summary>
    /// <param name="effect">Эффект для удаления.</param>
    /// <returns>true, если эффект был найден и удалён.</returns>
    public bool Remove(IAudioEffect effect) =>
        effects.Remove(effect);

    /// <summary>
    /// Удаляет все эффекты из цепочки.
    /// </summary>
    public void Clear() =>
        effects.Clear();

    /// <inheritdoc/>
    public void Process(Span<float> samples, int channels, int sampleRate)
    {
        if (!IsEnabled) return;

        foreach (var effect in effects)
        {
            if (effect.IsEnabled)
            {
                effect.Process(samples, channels, sampleRate);
            }
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        foreach (var effect in effects)
        {
            effect.Reset();
        }
    }
}
