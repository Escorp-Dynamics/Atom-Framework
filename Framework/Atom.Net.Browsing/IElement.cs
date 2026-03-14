namespace Atom.Net.Browsing;

/// <summary>
/// Представляет элемент DOM.
/// </summary>
public interface IElement
{
    /// <summary>
    /// Внутренний идентификатор элемента.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Выполняет клик по элементу.
    /// </summary>
    ValueTask ClickAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ClickAsync(CancellationToken)"/>
    ValueTask ClickAsync() => ClickAsync(CancellationToken.None);

    /// <summary>
    /// Выполняет двойной клик по элементу.
    /// </summary>
    ValueTask DoubleClickAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="DoubleClickAsync(CancellationToken)"/>
    ValueTask DoubleClickAsync() => DoubleClickAsync(CancellationToken.None);

    /// <summary>
    /// Вводит текст в элемент.
    /// </summary>
    /// <param name="text">Текст для ввода.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask TypeAsync(string text, CancellationToken cancellationToken);

    /// <inheritdoc cref="TypeAsync(string, CancellationToken)"/>
    ValueTask TypeAsync(string text) => TypeAsync(text, CancellationToken.None);

    /// <summary>
    /// Очищает содержимое элемента.
    /// </summary>
    ValueTask ClearAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ClearAsync(CancellationToken)"/>
    ValueTask ClearAsync() => ClearAsync(CancellationToken.None);

    /// <summary>
    /// Наводит курсор на элемент.
    /// </summary>
    ValueTask HoverAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="HoverAsync(CancellationToken)"/>
    ValueTask HoverAsync() => HoverAsync(CancellationToken.None);

    /// <summary>
    /// Устанавливает фокус на элемент.
    /// </summary>
    ValueTask FocusAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="FocusAsync(CancellationToken)"/>
    ValueTask FocusAsync() => FocusAsync(CancellationToken.None);

    /// <summary>
    /// Прокручивает страницу до элемента.
    /// </summary>
    ValueTask ScrollIntoViewAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ScrollIntoViewAsync(CancellationToken)"/>
    ValueTask ScrollIntoViewAsync() => ScrollIntoViewAsync(CancellationToken.None);

    /// <summary>
    /// Выбирает значение в элементе (для <c>select</c>).
    /// </summary>
    /// <param name="value">Значение для выбора.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask SelectAsync(string value, CancellationToken cancellationToken);

    /// <inheritdoc cref="SelectAsync(string, CancellationToken)"/>
    ValueTask SelectAsync(string value) => SelectAsync(value, CancellationToken.None);

    /// <summary>
    /// Переключает состояние чекбокса.
    /// </summary>
    ValueTask CheckAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="CheckAsync(CancellationToken)"/>
    ValueTask CheckAsync() => CheckAsync(CancellationToken.None);

    /// <summary>
    /// Получает значение свойства или атрибута элемента.
    /// </summary>
    /// <param name="propertyName">Имя свойства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask<string?> GetPropertyAsync(string propertyName, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetPropertyAsync(string, CancellationToken)"/>
    ValueTask<string?> GetPropertyAsync(string propertyName) => GetPropertyAsync(propertyName, CancellationToken.None);

    /// <summary>
    /// Находит дочерний элемент внутри текущего.
    /// </summary>
    /// <param name="selector">Селектор элемента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask<IElement?> FindElementAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="FindElementAsync(ElementSelector, CancellationToken)"/>
    ValueTask<IElement?> FindElementAsync(ElementSelector selector) => FindElementAsync(selector, CancellationToken.None);

    /// <summary>
    /// Находит все дочерние элементы внутри текущего.
    /// </summary>
    /// <param name="selector">Селектор элементов.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask<IElement[]> FindElementsAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="FindElementsAsync(ElementSelector, CancellationToken)"/>
    ValueTask<IElement[]> FindElementsAsync(ElementSelector selector) => FindElementsAsync(selector, CancellationToken.None);

    /// <summary>
    /// Открывает теневой корень (Shadow Root) элемента как скоуп,
    /// в котором все DOM-операции выполняются внутри теневого дерева.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Теневой корень или <see langword="null"/>, если элемент не имеет Shadow Root.</returns>
    ValueTask<IShadowRoot?> OpenShadowRootAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="OpenShadowRootAsync(CancellationToken)"/>
    ValueTask<IShadowRoot?> OpenShadowRootAsync() => OpenShadowRootAsync(CancellationToken.None);
}
