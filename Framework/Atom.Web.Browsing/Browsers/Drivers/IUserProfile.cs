namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет базовый интерфейс для настроек профиля браузера.
/// </summary>
public interface IUserProfile
{
    /// <summary>
    /// Настройки профиля по умолчанию.
    /// </summary>
    abstract static IUserProfile Default { get; }

    /// <summary>
    /// Сохраняет профиль по указанному пути.
    /// </summary>
    /// <param name="path">Путь сохранения.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask SaveAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Сохраняет профиль по указанному пути.
    /// </summary>
    /// <param name="path">Путь сохранения.</param>
    ValueTask SaveAsync(string path) => SaveAsync(path, CancellationToken.None);
}