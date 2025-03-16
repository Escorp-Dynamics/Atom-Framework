namespace Atom.Media.Filters;

/// <summary>
/// Представляет базовую реализацию медиафильтра.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Filter"/>.
/// </remarks>
/// <param name="duration">Длительность эффекта.</param>
public abstract class Filter(TimeSpan duration) : IFilter
{
    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public TimeSpan Duration { get; set; } = duration;

    /// <inheritdoc/>
    public virtual string Calculate() => $"{Name}=";
}