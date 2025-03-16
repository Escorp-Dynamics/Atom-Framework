namespace Atom.Architect.Components;

/// <summary>
/// Представляет атрибут для реализации компонента.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = default, Inherited = true)]
public sealed class ComponentAttribute : Attribute;