using System.Drawing;
using System.Runtime.CompilerServices;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет фрейм страницы как отдельный DOM-контекст.
/// </summary>
public interface IFrame : IDomContext
{
    event MutableEventHandler<IFrame, WebLifecycleEventArgs>? DomContentLoaded;

    event MutableEventHandler<IFrame, WebLifecycleEventArgs>? NavigationCompleted;

    event MutableEventHandler<IFrame, WebLifecycleEventArgs>? PageLoaded;

    /// <summary>
    /// Получает страницу, в которой загружен фрейм.
    /// </summary>
    IWebPage Page { get; }

    /// <summary>
    /// Получает элемент-хост iframe, которому соответствует текущий фрейм.
    /// Для основного фрейма страницы значение равно null.
    /// </summary>
    IElement? Host { get; }

    /// <summary>
    /// Возвращает дочерние фреймы текущего фрейма.
    /// </summary>
    ValueTask<IEnumerable<IFrame>> GetChildFramesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetChildFramesAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IEnumerable<IFrame>> GetChildFramesAsync() => GetChildFramesAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает родительский фрейм.
    /// </summary>
    ValueTask<IFrame?> GetParentFrameAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetParentFrameAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IFrame?> GetParentFrameAsync() => GetParentFrameAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает элемент-хост iframe, которому соответствует текущий фрейм.
    /// Для основного фрейма страницы значение равно null.
    /// </summary>
    ValueTask<IElement?> GetFrameElementAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetFrameElementAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IElement?> GetFrameElementAsync() => GetFrameElementAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает имя фрейма.
    /// </summary>
    ValueTask<string?> GetNameAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetNameAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetNameAsync() => GetNameAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает низкоуровневый дескриптор элемента iframe.
    /// </summary>
    ValueTask<string?> GetFrameElementHandleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetFrameElementHandleAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetFrameElementHandleAsync() => GetFrameElementHandleAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает скриншот фрейма.
    /// </summary>
    ValueTask<Memory<byte>> GetScreenshotAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetScreenshotAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Memory<byte>> GetScreenshotAsync() => GetScreenshotAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак того, что фрейм отсоединен от документа.
    /// </summary>
    ValueTask<bool> IsDetachedAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsDetachedAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsDetachedAsync() => IsDetachedAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак видимости фрейма.
    /// </summary>
    ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsVisibleAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsVisibleAsync() => IsVisibleAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает прямоугольник фрейма на странице.
    /// </summary>
    ValueTask<Rectangle?> GetBoundingBoxAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetBoundingBoxAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Rectangle?> GetBoundingBoxAsync() => GetBoundingBoxAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает контентный фрейм, связанный с текущим iframe.
    /// </summary>
    ValueTask<IFrame?> GetContentFrameAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetContentFrameAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IFrame?> GetContentFrameAsync() => GetContentFrameAsync(CancellationToken.None);
}