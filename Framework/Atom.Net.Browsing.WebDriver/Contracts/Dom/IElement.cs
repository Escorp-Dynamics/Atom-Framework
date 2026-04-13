using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Atom.Hardware.Input;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет DOM-элемент и полный набор операций взаимодействия, чтения состояния и модификации.
/// </summary>
public interface IElement
{
    /// <summary>
    /// Получает текущее опубликованное состояние удаления владельца элемента.
    /// Значение является advisory snapshot и подходит для быстрой проверки жизненного цикла,
    /// но не заменяет fail-fast boundary-guards при конкурентном доступе.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Получает страницу, которой принадлежит элемент.
    /// </summary>
    IWebPage Page { get; }

    /// <summary>
    /// Получает фрейм, которому принадлежит элемент.
    /// </summary>
    IFrame Frame { get; }

    /// <summary>
    /// Выполняет клик по элементу.
    /// </summary>
    ValueTask ClickAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ClickAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask ClickAsync() => ClickAsync(CancellationToken.None);

    /// <summary>
    /// Выполняет клик по элементу с указанными параметрами.
    /// </summary>
    ValueTask ClickAsync(ClickSettings settings, CancellationToken cancellationToken);

    /// <inheritdoc cref="ClickAsync(ClickSettings, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask ClickAsync(ClickSettings settings) => ClickAsync(settings, CancellationToken.None);

    /// <summary>
    /// Наводит курсор на элемент.
    /// </summary>
    ValueTask HoverAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="HoverAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask HoverAsync() => HoverAsync(CancellationToken.None);

    /// <summary>
    /// Прокручивает страницу до появления элемента в зоне видимости.
    /// </summary>
    ValueTask ScrollIntoViewAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ScrollIntoViewAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask ScrollIntoViewAsync() => ScrollIntoViewAsync(CancellationToken.None);

    /// <summary>
    /// Переводит фокус на элемент.
    /// </summary>
    ValueTask FocusAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="FocusAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask FocusAsync() => FocusAsync(CancellationToken.None);

    /// <summary>
    /// Вводит указанный текст в элемент.
    /// </summary>
    ValueTask TypeAsync(string text, CancellationToken cancellationToken);

    /// <inheritdoc cref="TypeAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask TypeAsync(string text) => TypeAsync(text, CancellationToken.None);

    /// <summary>
    /// Нажимает клавишу в контексте элемента.
    /// </summary>
    ValueTask PressAsync(ConsoleKey key, CancellationToken cancellationToken);

    /// <inheritdoc cref="PressAsync(ConsoleKey, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask PressAsync(ConsoleKey key) => PressAsync(key, CancellationToken.None);

    /// <summary>
    /// Выполняет человекоподобный клик по элементу.
    /// </summary>
    ValueTask HumanityClickAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="HumanityClickAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask HumanityClickAsync() => HumanityClickAsync(CancellationToken.None);

    /// <summary>
    /// Выполняет человекоподобный ввод текста в элемент.
    /// </summary>
    ValueTask HumanityTypeAsync(string text, CancellationToken cancellationToken);

    /// <inheritdoc cref="HumanityTypeAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask HumanityTypeAsync(string text) => HumanityTypeAsync(text, CancellationToken.None);

    /// <summary>
    /// Возвращает внутренний текст элемента.
    /// </summary>
    ValueTask<string?> GetInnerTextAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetInnerTextAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetInnerTextAsync() => GetInnerTextAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает внутренний HTML элемента.
    /// </summary>
    ValueTask<string?> GetInnerHtmlAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetInnerHtmlAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetInnerHtmlAsync() => GetInnerHtmlAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает значение элемента.
    /// </summary>
    ValueTask<string?> GetValueAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetValueAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetValueAsync() => GetValueAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает значение атрибута элемента.
    /// </summary>
    ValueTask<string?> GetAttributeAsync(string attributeName, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetAttributeAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetAttributeAsync(string attributeName) => GetAttributeAsync(attributeName, CancellationToken.None);

    /// <summary>
    /// Возвращает признак видимости элемента.
    /// </summary>
    ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsVisibleAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsVisibleAsync() => IsVisibleAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает координаты и размеры элемента.
    /// </summary>
    ValueTask<Rectangle?> GetBoundingBoxAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetBoundingBoxAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Rectangle?> GetBoundingBoxAsync() => GetBoundingBoxAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает вычисленные стили элемента.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetComputedStyleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetComputedStyleAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IReadOnlyDictionary<string, string>> GetComputedStyleAsync() => GetComputedStyleAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак установленного состояния элемента.
    /// </summary>
    ValueTask<bool> IsCheckedAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsCheckedAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsCheckedAsync() => IsCheckedAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак того, что элемент отключен.
    /// </summary>
    ValueTask<bool> IsDisabledAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsDisabledAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsDisabledAsync() => IsDisabledAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает список CSS-классов элемента.
    /// </summary>
    ValueTask<IEnumerable<string>> GetClassListAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetClassListAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IEnumerable<string>> GetClassListAsync() => GetClassListAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает скриншот элемента.
    /// </summary>
    ValueTask<Memory<byte>> GetScreenshotAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetScreenshotAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Memory<byte>> GetScreenshotAsync() => GetScreenshotAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак пересечения элемента с viewport.
    /// </summary>
    ValueTask<bool> IsIntersectingViewportAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsIntersectingViewportAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsIntersectingViewportAsync() => IsIntersectingViewportAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает низкоуровневый дескриптор элемента.
    /// </summary>
    ValueTask<string?> GetElementHandleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetElementHandleAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetElementHandleAsync() => GetElementHandleAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает идентификатор элемента в рамках страницы.
    /// </summary>
    ValueTask<string?> GetIdAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetIdAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetIdAsync() => GetIdAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает значение пользовательского свойства элемента.
    /// </summary>
    ValueTask<string?> GetPropertyAsync(string propertyName, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetPropertyAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetPropertyAsync(string propertyName) => GetPropertyAsync(propertyName, CancellationToken.None);

    /// <summary>
    /// Возвращает признак contenteditable-элемента.
    /// </summary>
    ValueTask<bool> IsContentEditableAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsContentEditableAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsContentEditableAsync() => IsContentEditableAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак возможности перетаскивания элемента.
    /// </summary>
    ValueTask<bool> IsDraggableAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsDraggableAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsDraggableAsync() => IsDraggableAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает значение aria-label элемента.
    /// </summary>
    ValueTask<string?> GetAriaLabelAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetAriaLabelAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetAriaLabelAsync() => GetAriaLabelAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает ARIA-роль элемента.
    /// </summary>
    ValueTask<string?> GetRoleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetRoleAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetRoleAsync() => GetRoleAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает tabindex элемента.
    /// </summary>
    ValueTask<int?> GetTabIndexAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetTabIndexAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<int?> GetTabIndexAsync() => GetTabIndexAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак того, что элемент находится в фокусе.
    /// </summary>
    ValueTask<bool> IsFocusedAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsFocusedAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsFocusedAsync() => IsFocusedAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак возможности редактирования элемента.
    /// </summary>
    ValueTask<bool> IsEditableAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsEditableAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsEditableAsync() => IsEditableAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак выбранного состояния элемента.
    /// </summary>
    ValueTask<bool> IsSelectedAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsSelectedAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsSelectedAsync() => IsSelectedAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает родительский элемент.
    /// </summary>
    ValueTask<IElement?> GetParentElementAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetParentElementAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> GetParentElementAsync() => GetParentElementAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает дочерние элементы.
    /// </summary>
    ValueTask<IEnumerable<IElement>> GetChildElementsAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetChildElementsAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IEnumerable<IElement>> GetChildElementsAsync() => GetChildElementsAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает соседние элементы.
    /// </summary>
    ValueTask<IEnumerable<IElement>> GetSiblingElementsAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetSiblingElementsAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IEnumerable<IElement>> GetSiblingElementsAsync() => GetSiblingElementsAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает путь элемента в DOM.
    /// </summary>
    ValueTask<string?> GetElementPathAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetElementPathAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetElementPathAsync() => GetElementPathAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает пользовательские данные элемента по ключу.
    /// </summary>
    ValueTask<string?> GetCustomDataAsync(string key, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetCustomDataAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetCustomDataAsync(string key) => GetCustomDataAsync(key, CancellationToken.None);

    /// <summary>
    /// Возвращает признак анимации элемента.
    /// </summary>
    ValueTask<bool> IsAnimatingAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsAnimatingAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsAnimatingAsync() => IsAnimatingAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает описание состояния анимации элемента.
    /// </summary>
    ValueTask<string?> GetAnimationStateAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetAnimationStateAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetAnimationStateAsync() => GetAnimationStateAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак переполнения элемента.
    /// </summary>
    ValueTask<bool> IsOverflowingAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsOverflowingAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsOverflowingAsync() => IsOverflowingAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает направление переполнения элемента.
    /// </summary>
    ValueTask<string?> GetOverflowDirectionAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetOverflowDirectionAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetOverflowDirectionAsync() => GetOverflowDirectionAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак клиппинга элемента.
    /// </summary>
    ValueTask<bool> IsClippedAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsClippedAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsClippedAsync() => IsClippedAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает clip-path элемента.
    /// </summary>
    ValueTask<string?> GetClipPathAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetClipPathAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetClipPathAsync() => GetClipPathAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак нахождения указателя над элементом.
    /// </summary>
    ValueTask<bool> IsPointerOverAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsPointerOverAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsPointerOverAsync() => IsPointerOverAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает координаты указателя относительно элемента.
    /// </summary>
    ValueTask<Point?> GetPointerCoordinatesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetPointerCoordinatesAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Point?> GetPointerCoordinatesAsync() => GetPointerCoordinatesAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак нажатой кнопки указателя над элементом.
    /// </summary>
    ValueTask<bool> IsPointerDownAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsPointerDownAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsPointerDownAsync() => IsPointerDownAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает кнопку мыши, задействованную при взаимодействии.
    /// </summary>
    ValueTask<VirtualMouseButton?> GetPointerButtonAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetPointerButtonAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<VirtualMouseButton?> GetPointerButtonAsync() => GetPointerButtonAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак перетаскивания элемента указателем.
    /// </summary>
    ValueTask<bool> IsPointerDraggingAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsPointerDraggingAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsPointerDraggingAsync() => IsPointerDraggingAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает данные drag-and-drop для элемента.
    /// </summary>
    ValueTask<string?> GetDragDataAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetDragDataAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetDragDataAsync() => GetDragDataAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак операции drop над элементом.
    /// </summary>
    ValueTask<bool> IsDroppingAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsDroppingAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsDroppingAsync() => IsDroppingAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает целевой элемент операции drop.
    /// </summary>
    ValueTask<IElement?> GetDropTargetAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetDropTargetAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> GetDropTargetAsync() => GetDropTargetAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает эффект drop-операции.
    /// </summary>
    ValueTask<string?> GetDropEffectAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetDropEffectAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetDropEffectAsync() => GetDropEffectAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак переполнения содержимого элемента.
    /// </summary>
    ValueTask<bool> IsContentOverflowingAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsContentOverflowingAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsContentOverflowingAsync() => IsContentOverflowingAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает направление переполнения содержимого элемента.
    /// </summary>
    ValueTask<string?> GetContentOverflowDirectionAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetContentOverflowDirectionAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetContentOverflowDirectionAsync() => GetContentOverflowDirectionAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак клиппинга содержимого элемента.
    /// </summary>
    ValueTask<bool> IsContentClippedAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsContentClippedAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsContentClippedAsync() => IsContentClippedAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает clip-path содержимого элемента.
    /// </summary>
    ValueTask<string?> GetContentClipPathAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetContentClipPathAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetContentClipPathAsync() => GetContentClipPathAsync(CancellationToken.None);

    /// <summary>
    /// Устанавливает значение элемента.
    /// </summary>
    ValueTask SetValueAsync(string value, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetValueAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetValueAsync(string value) => SetValueAsync(value, CancellationToken.None);

    /// <summary>
    /// Устанавливает атрибут элемента.
    /// </summary>
    ValueTask SetAttributeAsync(string attributeName, string value, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetAttributeAsync(string, string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetAttributeAsync(string attributeName, string value) => SetAttributeAsync(attributeName, value, CancellationToken.None);

    /// <summary>
    /// Устанавливает CSS-стиль элемента.
    /// </summary>
    ValueTask SetStyleAsync(string propertyName, string value, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetStyleAsync(string, string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetStyleAsync(string propertyName, string value) => SetStyleAsync(propertyName, value, CancellationToken.None);

    /// <summary>
    /// Добавляет CSS-класс элементу.
    /// </summary>
    ValueTask AddClassAsync(string className, CancellationToken cancellationToken);

    /// <inheritdoc cref="AddClassAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask AddClassAsync(string className) => AddClassAsync(className, CancellationToken.None);

    /// <summary>
    /// Удаляет CSS-класс у элемента.
    /// </summary>
    ValueTask RemoveClassAsync(string className, CancellationToken cancellationToken);

    /// <inheritdoc cref="RemoveClassAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask RemoveClassAsync(string className) => RemoveClassAsync(className, CancellationToken.None);

    /// <summary>
    /// Переключает CSS-класс у элемента.
    /// </summary>
    ValueTask ToggleClassAsync(string className, CancellationToken cancellationToken);

    /// <inheritdoc cref="ToggleClassAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask ToggleClassAsync(string className) => ToggleClassAsync(className, CancellationToken.None);

    /// <summary>
    /// Заменяет HTML-содержимое элемента.
    /// </summary>
    ValueTask SetContentAsync(string html, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetContentAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetContentAsync(string html) => SetContentAsync(html, CancellationToken.None);

    /// <summary>
    /// Устанавливает пользовательское свойство элемента.
    /// </summary>
    ValueTask SetCustomPropertyAsync(string propertyName, string value, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetCustomPropertyAsync(string, string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetCustomPropertyAsync(string propertyName, string value) => SetCustomPropertyAsync(propertyName, value, CancellationToken.None);

    /// <summary>
    /// Устанавливает пользовательские данные элемента по ключу.
    /// </summary>
    ValueTask SetDataAsync(string key, string value, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetDataAsync(string, string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetDataAsync(string key, string value) => SetDataAsync(key, value, CancellationToken.None);

    /// <summary>
    /// Добавляет обработчик события к элементу.
    /// Поддерживаемые payload-формы bridge-backed runtime включают string, JsonElement и ElementEventArgs.
    /// </summary>
    ValueTask AddEventListenerAsync(string eventName, Delegate handler, CancellationToken cancellationToken);

    /// <inheritdoc cref="AddEventListenerAsync(string, Delegate, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask AddEventListenerAsync(string eventName, Delegate handler) => AddEventListenerAsync(eventName, handler, CancellationToken.None);

    /// <summary>
    /// Удаляет обработчик события у элемента.
    /// </summary>
    ValueTask RemoveEventListenerAsync(string eventName, Delegate handler, CancellationToken cancellationToken);

    /// <inheritdoc cref="RemoveEventListenerAsync(string, Delegate, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask RemoveEventListenerAsync(string eventName, Delegate handler) => RemoveEventListenerAsync(eventName, handler, CancellationToken.None);

    /// <summary>
    /// Выполняет скрипт в контексте элемента и возвращает результат в виде JSON.
    /// </summary>
    ValueTask<JsonElement?> EvaluateAsync(string script, CancellationToken cancellationToken);

    /// <inheritdoc cref="EvaluateAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<JsonElement?> EvaluateAsync(string script) => EvaluateAsync(script, CancellationToken.None);

    /// <summary>
    /// Выполняет скрипт в контексте элемента и возвращает типизированный результат.
    /// </summary>
    ValueTask<TResult?> EvaluateAsync<TResult>(string script, CancellationToken cancellationToken);

    /// <inheritdoc cref="EvaluateAsync{TResult}(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<TResult?> EvaluateAsync<TResult>(string script) => EvaluateAsync<TResult>(script, CancellationToken.None);

    /// <summary>
    /// Возвращает фрейм, связанный с элементом.
    /// </summary>
    ValueTask<IFrame?> GetFrameAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetFrameAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IFrame?> GetFrameAsync() => GetFrameAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает дочерние фреймы элемента.
    /// </summary>
    ValueTask<IEnumerable<IFrame>> GetChildFramesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetChildFramesAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IEnumerable<IFrame>> GetChildFramesAsync() => GetChildFramesAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает родительский фрейм элемента.
    /// </summary>
    ValueTask<IFrame?> GetParentFrameAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetParentFrameAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IFrame?> GetParentFrameAsync() => GetParentFrameAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает shadow root, связанный с элементом.
    /// </summary>
    ValueTask<IShadowRoot?> GetShadowRootAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetShadowRootAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IShadowRoot?> GetShadowRootAsync() => GetShadowRootAsync(CancellationToken.None);
}