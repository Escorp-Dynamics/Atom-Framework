namespace Atom.Media.Filters;

/// <summary>
/// Представляет базовый интерфейс для реализации медиафильтров.
/// </summary>
public interface IFilter
{
    /// <summary>
    /// Имя фильтра.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Длительность эффекта.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Вычисляет текущие данные эффекта.
    /// </summary>
    string Calculate();
}