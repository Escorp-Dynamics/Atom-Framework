namespace Atom.Buffers;

/// <summary>
/// Представляет атрибут генерации функционала для буферизации.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = default, Inherited = true)]
public sealed class PooledAttribute : Attribute;