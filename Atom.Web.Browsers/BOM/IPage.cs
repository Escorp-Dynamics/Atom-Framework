namespace Atom.Web.Browsers.BOM;

/// <summary>
/// Представляет страницу в браузере.
/// </summary>
public interface IPage : IAsyncDisposable
{
    /// <summary>
    /// Идентификатор страницы.
    /// </summary>
    ulong Id { get; }

    /// <summary>
    /// Закрывает страницу.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает страницу.
    /// </summary>
    ValueTask CloseAsync() => CloseAsync(CancellationToken.None);
}