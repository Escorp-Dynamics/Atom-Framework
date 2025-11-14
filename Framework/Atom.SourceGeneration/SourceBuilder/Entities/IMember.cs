using System.Runtime.CompilerServices;

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithAccessModifier(AccessModifier modifier);

    /// <summary>
    /// Назначает тип.
    /// </summary>
    /// <param name="type">Тип.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithType(string type);

    /// <summary>
    /// Назначает тип.
    /// </summary>
    /// <typeparam name="TType">Тип.</typeparam>
    /// <param name="withNamespaces">Указывает, нужно ли возвращать полное имя типа с пространством имён.</param>
    /// <param name="withNullable">Указывает, нужно ли возвращать имя типа с nullable-модификатором.</param>
    /// <param name="withGenericNullable">Указывает, нужно ли возвращать имя типа дженерика с nullable-модификатором.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithType<TType>(bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true) where TType : allows ref struct => WithType(typeof(TType).GetFriendlyName(withNamespaces, withNullable, withGenericNullable));

    /// <summary>
    /// Определяет, что член должен быть статичным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T AsStatic(bool value);

    /// <summary>
    /// Определяет, что член должен быть статичным.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T AsStatic() => AsStatic(value: true);
}