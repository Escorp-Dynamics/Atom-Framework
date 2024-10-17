namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя для <c>enum</c>.
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
    T WithAccessModifier(AccessModifier modifier);

    /// <summary>
    /// Назначает тип значений перечисления.
    /// </summary>
    /// <param name="type">Тип значений перечисления.</param>
    T WithType(string type);

    /// <summary>
    /// Назначает тип значений перечисления.
    /// </summary>
    /// <typeparam name="TType">Тип значений перечисления.</typeparam>
    T WithType<TType>();

    /// <summary>
    /// Указывает, что перечисление будет использоваться как битовая маска.
    /// </summary>
    T AsFlags();

    /// <summary>
    /// Добавляет значения перечисления.
    /// </summary>
    /// <param name="values">Значения.</param>
    T AddValues(params EnumMember[] values);

    /// <summary>
    /// Добавляет значение перечисления.
    /// </summary>
    /// <param name="value">Значение.</param>
    T AddValue(EnumMember value) => AddValues(value);

    /// <summary>
    /// Добавляет значение перечисления.
    /// </summary>
    /// <param name="name">Имя значения.</param>
    /// <param name="value">Значение.</param>
    /// <param name="comment">Комментарий.</param>
    /// <param name="attributes">Атрибуты значения.</param>
    T AddValue(string name, long value, string comment, params string[] attributes) => AddValue(EnumMember.Create()
        .WithName(name)
        .WithValue(value)
        .WithComment(comment)
        .WithAttributes(attributes)
    );

    /// <summary>
    /// Добавляет значение перечисления.
    /// </summary>
    /// <param name="name">Имя значения.</param>
    /// <param name="value">Значение.</param>
    T AddValue(string name, long value) => AddValue(name, value, string.Empty);

    /// <summary>
    /// Добавляет значение перечисления.
    /// </summary>
    /// <param name="name">Имя значения.</param>
    T AddValue(string name) => AddValue(name, -1);

    /// <summary>
    /// Добавляет значение перечисления.
    /// </summary>
    /// <param name="name">Имя значения.</param>
    /// <param name="comment">Комментарий.</param>
    /// <param name="attributes">Атрибуты значения.</param>
    T AddValue(string name, string comment, params string[] attributes) => AddValue(name, -1, comment, attributes);
}