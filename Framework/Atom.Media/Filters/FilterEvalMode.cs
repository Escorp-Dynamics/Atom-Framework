namespace Atom.Media.Filters;

/// <summary>
/// Режим оценки параметров эффекта.
/// </summary>
public enum FilterEvalMode
{
    /// <summary>
    /// Оценивается один раз при инициализации.
    /// </summary>
    Init,
    /// <summary>
    /// Оценивается для каждого кадра.
    /// </summary>
    Frame,
}