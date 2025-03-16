using System.Diagnostics.CodeAnalysis;

namespace Atom.Architect.Components;

/// <summary>
/// Представляет базовый интерфейс для реализации владельцев компонентов.
/// </summary>
public interface IComponentOwner
{
    /// <summary>
    /// Определяет, находится ли компонент в использовании.
    /// </summary>
    /// <typeparam name="T">Тип компонента.</typeparam>
    bool Has<T>() where T : IComponent;

    /// <summary>
    /// Определяет, находится ли компонент в использовании.
    /// </summary>
    /// <param name="component">Искомый компонент.</param>
    /// <typeparam name="T">Тип компонента.</typeparam>
    bool Has<T>(T component) where T : IComponent;

    /// <summary>
    /// Пытается получить используемый компонент.
    /// </summary>
    /// <param name="component">Полученный компонент.</param>
    /// <typeparam name="T">Тип компонента.</typeparam>
    bool TryGet<T>(out T? component) where T : IComponent;

    /// <summary>
    /// Возвращает компонент, если он находится в использовании.
    /// </summary>
    /// <typeparam name="T">Тип компонента.</typeparam>
    T Get<T>() where T : IComponent;

    /// <summary>
    /// Пытается получить все компоненты в использовании.
    /// </summary>
    /// <param name="components">Используемые компоненты.</param>
    /// <typeparam name="T">Тип компонента.</typeparam>
    bool TryGetAll<T>(out IEnumerable<T> components) where T : IComponent;

    /// <summary>
    /// Получает все компоненты в использовании.
    /// </summary>
    /// <typeparam name="T">Тип компонента.</typeparam>
    IEnumerable<T> GetAll<T>() where T : IComponent;

    /// <summary>
    /// Определяет, поддерживается ли компонент.
    /// </summary>
    /// <typeparam name="T">Тип компонента.</typeparam>
    bool IsSupported<T>() where T : IComponent;
}

/// <summary>
/// Представляет базовый интерфейс для реализации владельцев компонентов.
/// </summary>
/// <typeparam name="TOwner">Тип владельца компонентов.</typeparam>
public interface IComponentOwner<out TOwner> : IComponentOwner
{
    /// <summary>
    /// Добавляет компонент в использование.
    /// </summary>
    /// <typeparam name="T">Тип компонента.</typeparam>
    TOwner Use<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : IComponent, new();

    /// <summary>
    /// Добавляет компонент в использование.
    /// </summary>
    /// <param name="component">Добавляемый компонент.</param>
    /// <typeparam name="T">Тип компонента.</typeparam>
    TOwner Use<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(T component) where T : IComponent;

    /// <summary>
    /// Исключает компонент из использования.
    /// </summary>
    /// <typeparam name="T">Тип компонента.</typeparam>
    TOwner UnUse<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : IComponent;

    /// <summary>
    /// Исключает компонент из использования.
    /// </summary>
    /// <param name="component">Исключаемый компонент.</param>
    /// <typeparam name="T">Тип компонента.</typeparam>
    TOwner UnUse<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(T component) where T : IComponent;
}