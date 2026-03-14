using System.Text.Json;

namespace Atom.Net.Browsing;

/// <summary>
/// Представляет контекст DOM — общий интерфейс для страниц и фреймов.
/// Позволяет выполнять скрипты, получать информацию о содержимом
/// и осуществлять навигацию по вложенным фреймам.
/// </summary>
public interface IDomContext
{
    /// <summary>
    /// URL текущего контекста.
    /// Для страницы — адрес вкладки, для фрейма — значение атрибута <c>src</c>.
    /// </summary>
    ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetUrlAsync(CancellationToken)"/>
    ValueTask<Uri?> GetUrlAsync() => GetUrlAsync(CancellationToken.None);

    /// <summary>
    /// Заголовок текущего контекста.
    /// </summary>
    ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetTitleAsync(CancellationToken)"/>
    ValueTask<string?> GetTitleAsync() => GetTitleAsync(CancellationToken.None);

    /// <summary>
    /// HTML-содержимое текущего контекста.
    /// </summary>
    ValueTask<string?> GetContentAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetContentAsync(CancellationToken)"/>
    ValueTask<string?> GetContentAsync() => GetContentAsync(CancellationToken.None);

    /// <summary>
    /// Выполняет JavaScript-код в текущем контексте и возвращает результат.
    /// </summary>
    /// <param name="script">JavaScript-код.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат выполнения скрипта.</returns>
    ValueTask<JsonElement?> ExecuteAsync(string script, CancellationToken cancellationToken);

    /// <inheritdoc cref="ExecuteAsync(string, CancellationToken)"/>
    ValueTask<JsonElement?> ExecuteAsync(string script) => ExecuteAsync(script, CancellationToken.None);

    /// <summary>
    /// Находит элемент в текущем контексте.
    /// </summary>
    /// <param name="selector">Селектор элемента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Найденный элемент или <see langword="null"/>.</returns>
    ValueTask<IElement?> FindElementAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="FindElementAsync(ElementSelector, CancellationToken)"/>
    ValueTask<IElement?> FindElementAsync(ElementSelector selector) => FindElementAsync(selector, CancellationToken.None);

    /// <summary>
    /// Находит все подходящие элементы в текущем контексте.
    /// </summary>
    /// <param name="selector">Селектор элементов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Найденные элементы.</returns>
    ValueTask<IElement[]> FindElementsAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="FindElementsAsync(ElementSelector, CancellationToken)"/>
    ValueTask<IElement[]> FindElementsAsync(ElementSelector selector) => FindElementsAsync(selector, CancellationToken.None);

    /// <summary>
    /// Ожидает появления элемента в текущем контексте.
    /// </summary>
    /// <param name="selector">Селектор элемента.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Найденный элемент или <see langword="null"/>.</returns>
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan? timeout, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, TimeSpan?, CancellationToken)"/>
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan? timeout)
        => WaitForElementAsync(selector, timeout, CancellationToken.None);

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, TimeSpan?, CancellationToken)"/>
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, timeout: null, cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, TimeSpan?, CancellationToken)"/>
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector)
        => WaitForElementAsync(selector, timeout: null, CancellationToken.None);

    /// <summary>
    /// Вложенные фреймы текущего контекста.
    /// </summary>
    IEnumerable<IFrame> Frames { get; }
}
