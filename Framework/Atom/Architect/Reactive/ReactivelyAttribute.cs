namespace Atom.Architect.Reactive;

/// <summary>
/// Автоматически генерирует код событий изменения свойства.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ReactivelyAttribute"/> с именем свойства.
/// </remarks>
/// <param name="propertyName">Имя свойства, для которого будет генерироваться код событий изменения.</param>
/// <param name="accessModifier">Модификатор доступа свойства.</param>
/// <param name="isVirtual">Определяет, будет ли свойство виртуальным.</param>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = default, Inherited = true)]
public sealed class ReactivelyAttribute(string propertyName, AccessModifier accessModifier, bool isVirtual) : Attribute
{
    /// <summary>
    /// Имя свойства, для которого будет генерироваться код событий изменения.
    /// </summary>
    public string PropertyName { get; } = propertyName;

    /// <summary>
    /// Модификатор доступа.
    /// </summary>
    public AccessModifier AccessModifier { get; } = accessModifier;

    /// <summary>
    /// Определяет, будет ли свойство виртуальным.
    /// </summary>
    public bool IsVirtual { get; } = isVirtual;

    /// <summary>
    /// Атрибуты свойства.
    /// </summary>
    public Type[] Attributes { get; set; } = [];

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactivelyAttribute"/>
    /// </summary>
    /// <param name="propertyName">Имя свойства, для которого будет генерироваться код событий изменения.</param>
    /// <param name="accessModifier">Модификатор доступа свойства.</param>
    public ReactivelyAttribute(string propertyName, AccessModifier accessModifier) : this(propertyName, accessModifier, isVirtual: false) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactivelyAttribute"/>
    /// </summary>
    /// <param name="propertyName">Имя свойства, для которого будет генерироваться код событий изменения.</param>
    /// <param name="isVirtual">Определяет, будет ли свойство виртуальным.</param>
    public ReactivelyAttribute(string propertyName, bool isVirtual) : this(propertyName, default, isVirtual) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactivelyAttribute"/>
    /// </summary>
    /// <param name="accessModifier">Модификатор доступа свойства.</param>
    /// <param name="isVirtual">Определяет, будет ли свойство виртуальным.</param>
    public ReactivelyAttribute(AccessModifier accessModifier, bool isVirtual) : this(string.Empty, accessModifier, isVirtual) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactivelyAttribute"/>
    /// </summary>
    /// <param name="accessModifier">Модификатор доступа свойства.</param>
    public ReactivelyAttribute(AccessModifier accessModifier) : this(accessModifier, isVirtual: false) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactivelyAttribute"/>
    /// </summary>
    /// <param name="isVirtual">Определяет, будет ли свойство виртуальным.</param>
    public ReactivelyAttribute(bool isVirtual) : this(string.Empty, isVirtual) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactivelyAttribute"/>
    /// </summary>
    /// <param name="propertyName">Имя свойства, для которого будет генерироваться код событий изменения.</param>
    public ReactivelyAttribute(string propertyName) : this(propertyName, isVirtual: false) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactivelyAttribute"/>
    /// </summary>
    public ReactivelyAttribute() : this(isVirtual: false) { }
}