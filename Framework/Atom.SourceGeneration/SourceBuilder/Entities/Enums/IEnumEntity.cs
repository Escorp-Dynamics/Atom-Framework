using System.Runtime.CompilerServices;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя для <see langword="enum"/>.
/// </summary>
public interface IEnumEntity<out T> : IEntity<T> where T : IEntity
{
    /// <summary>
    /// Модификатор доступа.
    /// </summary>
    AccessModifier AccessModifier { get; }

    /// <summary>
    /// Тип значений перечисления.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Значения перечисления.
    /// </summary>
    IEnumerable<EnumMember> Values { get; }

    /// <summary>
    /// Указывает, является ли перечисление битовыми флагами.
    /// </summary>
    bool IsFlags { get; }

    /// <summary>
    /// Назначает модификатор доступа.
    /// </summary>
    /// <param name="modifier">Модификатор доступа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithAccessModifier(AccessModifier modifier);

    /// <summary>
    /// Назначает тип значений перечисления.
    /// </summary>
    /// <param name="type">Тип значений перечисления.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithType(string type);

    /// <summary>
    /// Назначает тип значений перечисления.
    /// </summary>
    /// <typeparam name="TType">Тип значений перечисления.</typeparam>
    /// <param name="withNamespaces">Указывает, нужно ли возвращать полное имя типа с пространством имён.</param>
    /// <param name="withNullable">Указывает, нужно ли возвращать имя типа с nullable-модификатором.</param>
    /// <param name="withGenericNullable">Указывает, нужно ли возвращать имя типа дженерика с nullable-модификатором.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithType<TType>(bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true);

    /// <summary>
    /// Указывает, что перечисление будет использоваться как битовая маска.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T AsFlags();

    /// <summary>
    /// Добавляет значения перечисления.
    /// </summary>
    /// <param name="values">Значения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithValue(params IEnumerable<EnumMember> values);

    /// <summary>
    /// Добавляет значение перечисления.
    /// </summary>
    /// <param name="name">Имя значения.</param>
    /// <param name="value">Значение.</param>
    /// <param name="comment">Комментарий.</param>
    /// <param name="attributes">Атрибуты значения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithValue(string name, long value, string comment, params IEnumerable<string> attributes) => WithValue(EnumMember.Create()
        .WithName(name)
        .WithValue(value)
        .WithComment(comment)
        .WithAttribute(attributes)
    );

    /// <summary>
    /// Добавляет значение перечисления.
    /// </summary>
    /// <param name="name">Имя значения.</param>
    /// <param name="value">Значение.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithValue(string name, long value) => WithValue(name, value, string.Empty);

    /// <summary>
    /// Добавляет значение перечисления.
    /// </summary>
    /// <param name="name">Имя значения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithValue(string name) => WithValue(name, -1);

    /// <summary>
    /// Добавляет значение перечисления.
    /// </summary>
    /// <param name="name">Имя значения.</param>
    /// <param name="comment">Комментарий.</param>
    /// <param name="attributes">Атрибуты значения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithValue(string name, string comment, params IEnumerable<string> attributes) => WithValue(name, -1, comment, attributes);
}