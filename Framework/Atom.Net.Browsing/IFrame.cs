namespace Atom.Net.Browsing;

/// <summary>
/// Представляет iframe (встроенный фрейм) внутри веб-страницы.
/// Фрейм — изолированный DOM-контекст, поддерживающий вложенность.
/// </summary>
public interface IFrame : IDomContext
{
    /// <summary>
    /// Имя фрейма (атрибут <c>name</c>), если указано.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// URL источника фрейма (атрибут <c>src</c>).
    /// </summary>
    Uri? Source { get; }
}
