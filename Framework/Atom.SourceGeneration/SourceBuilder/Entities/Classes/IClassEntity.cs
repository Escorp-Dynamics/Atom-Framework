using System.Runtime.CompilerServices;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя класса.
/// </summary>
/// <typeparam name="T">Тип строителя.</typeparam>
public interface IClassEntity<out T> : IInterfaceEntity<T> where T : IEntity
{
    /// <summary>
    /// Поля.
    /// </summary>
    IEnumerable<FieldMember> Fields { get; }

    /// <summary>
    /// Является ли статическим.
    /// </summary>
    bool IsStatic { get; }

    /// <summary>
    /// Является ли запечатанным.
    /// </summary>
    bool IsSealed { get; }

    /// <summary>
    /// Добавляет поля.
    /// </summary>
    /// <param name="fields">Поля.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithField(params IEnumerable<FieldMember> fields);

    /// <summary>
    /// Добавляет поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <param name="value">Значение поля.</param>
    /// <typeparam name="TType">Тип поля.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithField<TType>(string name, string? value) => WithField(FieldMember.Create<TType>(name, value));

    /// <summary>
    /// Добавляет поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <param name="value">Значение поля.</param>
    /// <typeparam name="TType">Тип поля.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithField<TType>(string name, TType value) => WithField(FieldMember.Create(name, value));

    /// <summary>
    /// Добавляет поле.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <typeparam name="TType">Тип поля.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithField<TType>(string name) => WithField(FieldMember.Create<TType>(name));

    /// <summary>
    /// Указывает, является ли статическим.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T AsStatic(bool value);

    /// <summary>
    /// Указывает, является ли статическим.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T AsStatic() => AsStatic(value: true);

    /// <summary>
    /// Указывает, является ли запечатанным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T AsSealed(bool value);

    /// <summary>
    /// Указывает, является ли запечатанным.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T AsSealed() => AsSealed(value: true);
}