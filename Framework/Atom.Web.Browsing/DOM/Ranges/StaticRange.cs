using System.Diagnostics.CodeAnalysis;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет статический диапазон.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="StaticRange"/>.
/// </remarks>
/// <param name="init">Свойства инициализации статического диапазона.</param>
public class StaticRange([NotNull] StaticRangeInit init) : AbstractRange(init.StartContainer, init.StartOffset, init.EndContainer, init.EndOffset), IStaticRange
{
}