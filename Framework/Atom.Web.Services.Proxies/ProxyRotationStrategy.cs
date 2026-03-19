namespace Atom.Web.Proxies.Services;

/// <summary>
/// Стратегия выбора следующего прокси из пула.
/// </summary>
public enum ProxyRotationStrategy
{
    /// <summary>
    /// Последовательный обход пула по кругу.
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Случайно перемешивает текущую подходящую выборку и возвращает первые элементы без дублей внутри одного batch.
    /// Между отдельными вызовами состояние курсора не сохраняется, поэтому повторения между вызовами допустимы.
    /// </summary>
    Random,

    /// <summary>
    /// Приоритет более свежих прокси по Alive и Uptime.
    /// </summary>
    PreferFresh,
}