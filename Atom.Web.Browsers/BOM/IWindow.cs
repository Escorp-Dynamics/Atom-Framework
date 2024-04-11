using System.Drawing;

namespace Atom.Web.Browsers.BOM;

/// <summary>
/// Представляет окно браузера.
/// </summary>
public interface IWindow : IAsyncDisposable
{
    /// <summary>
    /// Возвращает идентификатор окна.
    /// </summary>
    ulong Id { get; }

    /// <summary>
    /// Получает или устанавливает размер окна.
    /// </summary>
    Size Size { get; set; }

    /// <summary>
    /// Получает или устанавливает позицию окна.
    /// </summary>
    Point Position { get; set; }

    /// <summary>
    /// Открывает новую страницу в окне браузера.
    /// </summary>
    /// <param name="settings">Настройки страницы.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Экземпляр страницы.</returns>
    ValueTask<IPage> OpenPageAsync(PageSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Открывает новую страницу в окне браузера.
    /// </summary>
    /// <param name="settings">Настройки страницы.</param>
    /// <returns>Экземпляр страницы.</returns>
    ValueTask<IPage> OpenPageAsync(PageSettings settings) => OpenPageAsync(settings, CancellationToken.None);

    /// <summary>
    /// Закрывает окно браузера.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает окно браузера.
    /// </summary>
    ValueTask CloseAsync() => CloseAsync(CancellationToken.None);
}