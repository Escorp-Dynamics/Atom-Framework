using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет базовый DOM-контекст страницы, фрейма или shadow root.
/// </summary>
public interface IDomContext
{
    /// <summary>
    /// Получает текущее опубликованное состояние удаления владельца DOM-контекста.
    /// Значение является advisory snapshot и подходит для быстрой проверки жизненного цикла,
    /// но не заменяет fail-fast boundary-guards при конкурентном доступе.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Получает все доступные фреймы текущего DOM-контекста.
    /// </summary>
    IEnumerable<IFrame> Frames { get; }

    /// <summary>
    /// Возвращает адрес текущего DOM-контекста.
    /// После dispose владельца контекста выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetUrlAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Uri?> GetUrlAsync() => GetUrlAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает заголовок текущего DOM-контекста.
    /// После dispose владельца контекста выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetTitleAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetTitleAsync() => GetTitleAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает HTML-содержимое текущего DOM-контекста.
    /// После dispose владельца контекста выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<string?> GetContentAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetContentAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetContentAsync() => GetContentAsync(CancellationToken.None);

    /// <summary>
    /// Выполняет скрипт в контексте текущего DOM-узла и возвращает результат в виде JSON.
    /// После dispose владельца контекста выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<JsonElement?> EvaluateAsync(string script, CancellationToken cancellationToken);

    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<JsonElement?> EvaluateAsync(string script) => EvaluateAsync(script, CancellationToken.None);

    /// <summary>
    /// Выполняет скрипт в контексте текущего DOM-узла и возвращает типизированный результат.
    /// После dispose владельца контекста выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<TResult?> EvaluateAsync<TResult>(string script, CancellationToken cancellationToken);

    /// <inheritdoc cref="EvaluateAsync{TResult}(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<TResult?> EvaluateAsync<TResult>(string script) => EvaluateAsync<TResult>(script, CancellationToken.None);

    /// <summary>
    /// Ожидает элемент по строковому селектору.
    /// </summary>
    ValueTask<IElement?> WaitForElementAsync(string selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> WaitForElementAsync(string selector) => WaitForElementAsync(selector, CancellationToken.None);

    /// <summary>
    /// Ожидает элемент по строковому селектору с ограничением по времени.
    /// </summary>
    ValueTask<IElement?> WaitForElementAsync(string selector, TimeSpan timeout, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(string, TimeSpan, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> WaitForElementAsync(string selector, TimeSpan timeout) => WaitForElementAsync(selector, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает элемент по строковому селектору с указанным режимом ожидания.
    /// </summary>
    ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(string, WaitForElementKind, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind) => WaitForElementAsync(selector, kind, CancellationToken.None);

    /// <summary>
    /// Ожидает элемент по строковому селектору с указанным режимом и таймаутом.
    /// </summary>
    ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, TimeSpan timeout, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(string, WaitForElementKind, TimeSpan, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, TimeSpan timeout) => WaitForElementAsync(selector, kind, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает элемент по объекту настроек ожидания.
    /// </summary>
    ValueTask<IElement?> WaitForElementAsync(WaitForElementSettings settings, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(WaitForElementSettings, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> WaitForElementAsync(WaitForElementSettings settings) => WaitForElementAsync(settings, CancellationToken.None);

    /// <summary>
    /// Ожидает элемент по универсальному селектору.
    /// </summary>
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector) => WaitForElementAsync(selector, CancellationToken.None);

    /// <summary>
    /// Ожидает элемент по универсальному селектору с ограничением по времени.
    /// </summary>
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan timeout, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, TimeSpan, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan timeout) => WaitForElementAsync(selector, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает элемент по универсальному селектору с указанным режимом ожидания.
    /// </summary>
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, WaitForElementKind, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind) => WaitForElementAsync(selector, kind, CancellationToken.None);

    /// <summary>
    /// Ожидает элемент по универсальному селектору с указанным режимом и таймаутом.
    /// </summary>
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, TimeSpan timeout, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, WaitForElementKind, TimeSpan, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, TimeSpan timeout) => WaitForElementAsync(selector, kind, timeout, CancellationToken.None);

    /// <summary>
    /// Возвращает первый найденный элемент по строковому селектору.
    /// </summary>
    ValueTask<IElement?> GetElementAsync(string selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetElementAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> GetElementAsync(string selector) => GetElementAsync(selector, CancellationToken.None);

    /// <summary>
    /// Возвращает первый найденный элемент по универсальному селектору.
    /// </summary>
    ValueTask<IElement?> GetElementAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetElementAsync(ElementSelector, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> GetElementAsync(ElementSelector selector) => GetElementAsync(selector, CancellationToken.None);

    /// <summary>
    /// Возвращает все найденные элементы по строковому селектору.
    /// </summary>
    ValueTask<IEnumerable<IElement>> GetElementsAsync(string selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetElementsAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IEnumerable<IElement>> GetElementsAsync(string selector) => GetElementsAsync(selector, CancellationToken.None);

    /// <summary>
    /// Возвращает все найденные элементы по универсальному селектору.
    /// </summary>
    ValueTask<IEnumerable<IElement>> GetElementsAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetElementsAsync(ElementSelector, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IEnumerable<IElement>> GetElementsAsync(ElementSelector selector) => GetElementsAsync(selector, CancellationToken.None);

    /// <summary>
    /// Возвращает корневой shadow root по строковому селектору хоста.
    /// </summary>
    ValueTask<IShadowRoot?> GetShadowRootAsync(string selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetShadowRootAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IShadowRoot?> GetShadowRootAsync(string selector) => GetShadowRootAsync(selector, CancellationToken.None);
}