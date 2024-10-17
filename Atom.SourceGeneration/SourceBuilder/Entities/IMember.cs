namespace Atom.SourceGeneration;

/// <summary>
/// Представляет базового строителя члена сущности.
/// </summary>
/// <typeparam name="T">Тип строителя члена сущности.</typeparam>
public interface IMember<out T> : IEntity<T> where T : IEntity
{
    /// <summary>
    /// Модификатор доступа.
    /// </summary>
    AccessModifier AccessModifier { get; }

    /// <summary>
    /// Тип.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Является ли статичным.
    /// </summary>
    bool IsStatic { get; }

    /// <summary>
    /// Назначает модификатор доступа.
    /// </summary>
    /// <param name="modifier">Модификатор доступа.</param>
    T WithAccessModifier(AccessModifier modifier);

    /// <summary>
    /// Назначает тип.
    /// </summary>
    /// <param name="type">Тип.</param>
    T WithType(string type);

    /// <summary>
    /// Назначает тип.
    /// </summary>
    /// <typeparam name="TType">Тип.</typeparam>
    T WithType<TType>() => WithType(typeof(TType).GetFriendlyName());

    /// <summary>
    /// Определяет, что член должен быть статичным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    T AsStatic(bool value);

    /// <summary>
    /// Определяет, что член должен быть статичным.
    /// </summary>
    T AsStatic() => AsStatic(true);
}