namespace Atom.Architect.Components;

/// <summary>
/// Представляет базовый интерфейс для реализации компонентной модели.
/// </summary>
/// <typeparam name="TModule">Тип компонента.</typeparam>
public interface IModular<TModule> where TModule : IModule
{
    /// <summary>
    /// Подключает компонент.
    /// </summary>
    /// <param name="module">Экземпляр компонента.</param>
    /// <typeparam name="T">Тип компонента.</typeparam>
    IModular<TModule> Use<T>(T module) where T : TModule;

    /// <summary>
    /// Подключает компонент.
    /// </summary>
    /// <typeparam name="T">Тип компонента.</typeparam>
    IModular<TModule> Use<T>() where T : TModule, new();

    /// <summary>
    /// Отключает компонент.
    /// </summary>
    /// <param name="module">Экземпляр компонента.</param>
    /// <typeparam name="T">Тип компонента.</typeparam>
    IModular<TModule> UnUse<T>(T module) where T : TModule;

    /// <summary>
    /// Отключает компонент.
    /// </summary>
    /// <typeparam name="T">Тип компонента.</typeparam>
    IModular<TModule> UnUse<T>() where T : TModule;

    /// <summary>
    /// Возвращает первый найденный компонент указанного типа.
    /// </summary>
    /// <typeparam name="T">Тип компонента.</typeparam>
    /// <returns>Экземпляр компонента.</returns>
    TModule? Get<T>() where T : TModule;

    /// <summary>
    /// Возвращает коллекцию всех найденных компонентов по указанному типу.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns>Коллекция компонентов.</returns>
    IEnumerable<TModule> GetAll<T>() where T : TModule;
}