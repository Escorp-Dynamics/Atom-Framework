namespace Atom.Architect.Components;

/// <summary>
/// Представляет базовый интерфейс для реализации компонентов.
/// </summary>
public interface IComponent
{
    /// <summary>
    /// Владелец.
    /// </summary>
    IComponentOwner? Owner { get; }

    /// <summary>
    /// Определяет, присоединён ли компонент к его владельцу.
    /// </summary>
    bool IsAttached => Owner is not null;

    /// <summary>
    /// Определяет, был ли компонент присоединён его же владельцем.
    /// </summary>
    bool IsAttachedByOwner { get; init; }

    /// <summary>
    /// Происходит в момент присоединения компонента к новому владельцу.
    /// </summary>
    event MutableEventHandler<object, ComponentEventArgs>? Attached;

    /// <summary>
    /// Происходит в момент отсоединения компонента от старого владельца.
    /// </summary>
    event MutableEventHandler<object, ComponentEventArgs>? Detached;

    /// <summary>
    /// Присоединяет компонент к новому владельцу.
    /// </summary>
    /// <param name="owner">Новый владелец.</param>
    void AttachTo(IComponentOwner owner);

    /// <summary>
    /// Отсоединяет компонент от старого владельца.
    /// </summary>
    void Detach();
}