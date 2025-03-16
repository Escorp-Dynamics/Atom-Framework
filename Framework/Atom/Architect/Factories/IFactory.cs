namespace Atom.Architect.Factories;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрики.
/// </summary>
public interface IFactory;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрики.
/// </summary>
/// <typeparam name="T">Тип элементов фабрики.</typeparam>
public interface IFactory<T> : IFactory
{
    /// <summary>
    /// Получает элемент.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask<T> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Получает элемент.
    /// </summary>
    ValueTask<T> GetAsync() => GetAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает элемент.
    /// </summary>
    /// <param name="item">Возвращаемый элемент.</param>
    void Return(T item);
}