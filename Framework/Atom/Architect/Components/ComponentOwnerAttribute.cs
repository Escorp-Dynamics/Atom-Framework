namespace Atom.Architect.Components;

/// <summary>
/// Представляет атрибут для генерации компонентной модели.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ComponentOwnerAttribute"/>.
/// </remarks>
/// <param name="type">Тип компонентов.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = true)]
public sealed class ComponentOwnerAttribute(Type type) : Attribute
{
    /// <summary>
    /// Тип компонентов.
    /// </summary>
    public Type Type { get; } = type;
}